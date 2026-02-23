using Microsoft.EntityFrameworkCore;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using PayBridge.FraudDetection;
using PayBridge.PaymentApi.HealthChecks;
using PayBridge.PaymentApi.Infrastructure;
using PayBridge.PaymentApi.Services;
using PayBridge.Logging;
using PayBridge.PaymentApi.Telemetry;
using Polly;
using RabbitMQ.Client;
using Serilog;
using StackExchange.Redis;
using Serilog.Formatting.Compact;
using Serilog.Sinks.Grafana.Loki;

var builder = WebApplication.CreateBuilder(args);

// ── Serilog ──────────────────────────────────────────────────────────────────
var lokiUrl = builder.Configuration["Loki:Url"] ?? "http://localhost:3100";

Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .Enrich.FromLogContext()
    .Enrich.WithProperty("Service", DiagnosticConfig.ServiceName)
    .WriteTo.Console(new CompactJsonFormatter())
    .WriteTo.GrafanaLoki(lokiUrl, labels: new[]
    {
        new Serilog.Sinks.Grafana.Loki.LokiLabel { Key = "service", Value = DiagnosticConfig.ServiceName }
    })
    .CreateLogger();

builder.Host.UseSerilog();

// ── EF Core (SQL Server) ────────────────────────────────────────────────────
builder.Services.AddDbContext<PayBridgeDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

// ── gRPC Fraud Client ────────────────────────────────────────────────────────
builder.Services.AddGrpcClient<FraudDetectionSvc.FraudDetectionSvcClient>(o =>
{
    o.Address = new Uri(builder.Configuration["FraudService:Url"] ?? "http://localhost:5001");
});

// ── HTTP Provider Client ─────────────────────────────────────────────────────
builder.Services.AddHttpClient<IProviderClient, ProviderClient>(client =>
{
    client.BaseAddress = new Uri(builder.Configuration["ProviderService:Url"] ?? "http://localhost:5002");
    client.Timeout = TimeSpan.FromSeconds(5);
});

// ── Redis ────────────────────────────────────────────────────────────────────
builder.Services.AddSingleton<IConnectionMultiplexer>(_ =>
    ConnectionMultiplexer.Connect(builder.Configuration.GetConnectionString("Redis") ?? "localhost:6379"));

// ── RabbitMQ (Polly retry on connection refused at startup) ───────────────────
builder.Services.AddSingleton<IConnection>(_ =>
{
    var factory = new ConnectionFactory
    {
        HostName = builder.Configuration["RabbitMQ:Host"] ?? "localhost",
        Port = int.TryParse(builder.Configuration["RabbitMQ:Port"], out var port) ? port : 5672,
        UserName = builder.Configuration["RabbitMQ:UserName"] ?? "guest",
        Password = builder.Configuration["RabbitMQ:Password"] ?? "guest"
    };
    const int maxRetries = 10;
    var policy = Policy
        .Handle<Exception>()
        .WaitAndRetry(
            maxRetries,
            attempt => TimeSpan.FromSeconds(attempt * 2),
            onRetry: (ex, delay, attempt, _) =>
                Log.Warning(ex, "RabbitMQ connection attempt {Attempt}/{Max} failed, retrying in {Delay}s…",
                    attempt, maxRetries, delay.TotalSeconds));
    return policy.Execute(() => factory.CreateConnectionAsync().GetAwaiter().GetResult());
});

// ── Logging ──────────────────────────────────────────────────────────────────
builder.Services.AddPayBridgeLogging(builder.Configuration);

// ── Services ─────────────────────────────────────────────────────────────────
builder.Services.AddSingleton<PaymentMetrics>();
builder.Services.AddSingleton<IEventPublisher, RabbitMqEventPublisher>();
builder.Services.AddScoped<IPaymentRepository, PaymentRepository>();
builder.Services.AddScoped<IIdempotencyService, IdempotencyService>();
builder.Services.AddScoped<IFraudClient, FraudClient>();
builder.Services.AddScoped<PaymentOrchestrator>();
builder.Services.AddHostedService<OutboxProcessor>();
builder.Services.AddSingleton<RabbitMQHealthCheck>();

// ── OpenTelemetry ────────────────────────────────────────────────────────────
var otelEndpoint = builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"] ?? "http://localhost:4317";

builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource.AddService(DiagnosticConfig.ServiceName))
    .WithTracing(tracing => tracing
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddGrpcClientInstrumentation()
        .AddSource(DiagnosticConfig.ServiceName)
        .AddOtlpExporter(o => o.Endpoint = new Uri(otelEndpoint)))
    .WithMetrics(metrics => metrics
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddMeter(PaymentMetrics.MeterName)
        .AddPrometheusExporter()
        .AddOtlpExporter(o => o.Endpoint = new Uri(otelEndpoint)));

// ── Health Checks ────────────────────────────────────────────────────────────
builder.Services.AddHealthChecks()
    .AddSqlServer(
        builder.Configuration.GetConnectionString("DefaultConnection") ?? "",
        name: "sqlserver",
        tags: new[] { "critical", "ready" })
    .AddRedis(
        builder.Configuration.GetConnectionString("Redis") ?? "localhost:6379",
        name: "redis",
        tags: new[] { "important", "ready" })
    .AddCheck<RabbitMQHealthCheck>("rabbitmq", tags: new[] { "critical", "ready" });

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// ── Apply EF Core Migrations ─────────────────────────────────────────────────
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<PayBridgeDbContext>();
    var retries = 5;
    for (var i = 0; i < retries; i++)
    {
        try
        {
            Log.Information("Applying database migrations (attempt {Attempt}/{MaxRetries})…", i + 1, retries);
            await db.Database.MigrateAsync();
            Log.Information("Database migrations applied successfully");
            break;
        }
        catch (Exception ex) when (i < retries - 1)
        {
            Log.Warning(ex, "Migration attempt {Attempt} failed, retrying in {Delay}s…", i + 1, (i + 1) * 2);
            await Task.Delay(TimeSpan.FromSeconds((i + 1) * 2));
        }
    }
}

// ── Middleware ────────────────────────────────────────────────────────────────
app.UseSwagger();
app.UseSwaggerUI();
app.UseSerilogRequestLogging();
app.MapControllers();
app.MapPrometheusScrapingEndpoint();

app.MapHealthChecks("/health/live", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = _ => false
});

app.MapHealthChecks("/health/ready", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready")
});

app.Run();

public partial class Program { }
