using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using PayBridge.FraudService.Services;
using PayBridge.Logging;
using Serilog;
using Serilog.Formatting.Compact;
using Serilog.Sinks.Grafana.Loki;

const string ServiceName = "PayBridge.FraudService";

var builder = WebApplication.CreateBuilder(args);

// ── Serilog ──────────────────────────────────────────────────────────────────
var lokiUrl = builder.Configuration["Loki:Url"] ?? "http://localhost:3100";

Log.Logger = new LoggerConfiguration()
    .Enrich.FromLogContext()
    .Enrich.WithProperty("Service", ServiceName)
    .WriteTo.Console(new CompactJsonFormatter())
    .WriteTo.GrafanaLoki(lokiUrl, labels: new[]
    {
        new Serilog.Sinks.Grafana.Loki.LokiLabel { Key = "service", Value = ServiceName }
    })
    .CreateLogger();

builder.Host.UseSerilog();

builder.Services.AddPayBridgeLogging(builder.Configuration);
builder.Services.AddGrpc();

// ── OpenTelemetry ────────────────────────────────────────────────────────────
var otelEndpoint = builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"] ?? "http://localhost:4317";

builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource.AddService(ServiceName))
    .WithTracing(tracing => tracing
        .AddAspNetCoreInstrumentation()
        .AddOtlpExporter(o => o.Endpoint = new Uri(otelEndpoint)));

var app = builder.Build();

app.UseSerilogRequestLogging();
app.MapGrpcService<FraudDetectionService>();

app.Run();
