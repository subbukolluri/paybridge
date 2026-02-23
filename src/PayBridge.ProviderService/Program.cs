using Serilog;
using Serilog.Formatting.Compact;

var builder = WebApplication.CreateBuilder(args);

Log.Logger = new LoggerConfiguration()
    .Enrich.FromLogContext()
    .Enrich.WithProperty("Service", "PayBridge.ProviderService")
    .WriteTo.Console(new CompactJsonFormatter())
    .CreateLogger();

builder.Host.UseSerilog();
builder.Services.AddHttpClient("webhook");
builder.Services.AddControllers();

var app = builder.Build();
app.UseSerilogRequestLogging();
app.MapControllers();

app.Run();
