using Microsoft.EntityFrameworkCore;
using PayBridge.PaymentApi.Infrastructure;
using PayBridge.PaymentApi.Services;
using Serilog;
using Serilog.Formatting.Compact;

var builder = WebApplication.CreateBuilder(args);

// ── Serilog ──────────────────────────────────────────────────────────────────
Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .Enrich.FromLogContext()
    .Enrich.WithProperty("Service", "PayBridge.PaymentApi")
    .WriteTo.Console(new CompactJsonFormatter())
    .CreateLogger();

builder.Host.UseSerilog();

// ── EF Core (SQL Server) ────────────────────────────────────────────────────
builder.Services.AddDbContext<PayBridgeDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

// ── Services ─────────────────────────────────────────────────────────────────
builder.Services.AddScoped<IPaymentRepository, PaymentRepository>();
builder.Services.AddScoped<PaymentOrchestrator>();

builder.Services.AddControllers();

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
app.UseSerilogRequestLogging();
app.MapControllers();

app.Run();

public partial class Program { }
