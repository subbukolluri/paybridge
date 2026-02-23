using Microsoft.EntityFrameworkCore;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using PayBridge.SettlementConsumer.Infrastructure;
using PayBridge.SettlementConsumer.Workers;
using RabbitMQ.Client;
using Serilog;
using Serilog.Formatting.Compact;
using Serilog.Sinks.Grafana.Loki;

const string ServiceName = "PayBridge.SettlementConsumer";

var builder = Host.CreateApplicationBuilder(args);

// ── Serilog (structured logging, same pattern as PaymentApi / FraudService) ────
var lokiUrl = builder.Configuration["Loki:Url"] ?? "http://localhost:3100";

Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .Enrich.FromLogContext()
    .Enrich.WithProperty("Service", ServiceName)
    .WriteTo.Console(new CompactJsonFormatter())
    .WriteTo.GrafanaLoki(lokiUrl, labels: new[]
    {
        new Serilog.Sinks.Grafana.Loki.LokiLabel { Key = "service", Value = ServiceName }
    })
    .CreateLogger();

builder.Services.AddSerilog();

// ── EF Core (SQL Server) ────────────────────────────────────────────────────
builder.Services.AddDbContext<SettlementDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

// ── RabbitMQ ─────────────────────────────────────────────────────────────────
builder.Services.AddSingleton<IConnection>(_ =>
{
    var factory = new ConnectionFactory
    {
        HostName = builder.Configuration["RabbitMQ:Host"] ?? "localhost",
        Port = int.TryParse(builder.Configuration["RabbitMQ:Port"], out var port) ? port : 5672,
        UserName = builder.Configuration["RabbitMQ:UserName"] ?? "guest",
        Password = builder.Configuration["RabbitMQ:Password"] ?? "guest",

        // DispatchConsumersAsync = true,          // REQUIRED
        AutomaticRecoveryEnabled = true
    };

    return factory.CreateConnectionAsync().GetAwaiter().GetResult();
});

// ── OpenTelemetry ────────────────────────────────────────────────────────────
var otelEndpoint = builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"] ?? "http://localhost:4317";

builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource.AddService(ServiceName))
    .WithTracing(tracing => tracing
        .AddSource(ServiceName)
        .AddOtlpExporter(o => o.Endpoint = new Uri(otelEndpoint)));

// ── Worker ───────────────────────────────────────────────────────────────────
builder.Services.AddHostedService<SettlementWorker>();

var host = builder.Build();

// ── Apply EF Core Migrations ─────────────────────────────────────────────────
using (var scope = host.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<SettlementDbContext>();
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

await host.RunAsync();
