using Serilog;
using Serilog.Formatting.Compact;
using Serilog.Sinks.Grafana.Loki;

const string ServiceName = "PayBridge.ProviderService";

var builder = WebApplication.CreateBuilder(args);

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

builder.Host.UseSerilog();
builder.Services.AddHttpClient("webhook");
builder.Services.AddControllers();

var app = builder.Build();
app.UseSerilogRequestLogging();
app.MapControllers();

app.Run();
