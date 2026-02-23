using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace PayBridge.Logging;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers IAppLogger and binds LoggerSettings from the "Logger" config section.
    /// </summary>
    public static IServiceCollection AddPayBridgeLogging(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<LoggerSettings>(configuration.GetSection("Logger"));
        services.AddSingleton<IAppLogger, PayBridgeLogger>();
        return services;
    }
}
