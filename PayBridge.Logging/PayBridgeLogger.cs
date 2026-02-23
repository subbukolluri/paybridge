using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace PayBridge.Logging;

/// <summary>
/// Structured logger that adds TraceId and ServiceName to scope so they appear as dimensions in Loki/App Insights.
/// Forwards to ILogger with template + params (no interpolation).
/// </summary>
public sealed class PayBridgeLogger : IAppLogger
{
    private readonly ILogger<PayBridgeLogger> _logger;
    private readonly LoggerSettings _settings;

    public PayBridgeLogger(ILogger<PayBridgeLogger> logger, IOptions<LoggerSettings> options)
    {
        _logger = logger;
        _settings = options.Value;
    }

    public void LogInformation(string messageTemplate, string? traceId = null, params object?[] messageTemplateValues)
    {
        var trace = traceId ?? Activity.Current?.Id ?? string.Empty;
        using (_logger.BeginScope(CreateScope(trace)))
        {
            _logger.LogInformation(messageTemplate, messageTemplateValues);
        }
    }

    public void LogWarning(string messageTemplate, string? traceId = null, params object?[] messageTemplateValues)
    {
        var trace = traceId ?? Activity.Current?.Id ?? string.Empty;
        using (_logger.BeginScope(CreateScope(trace)))
        {
            _logger.LogWarning(messageTemplate, messageTemplateValues);
        }
    }

    public void LogError(string messageTemplate, string? traceId = null, params object?[] messageTemplateValues)
    {
        var trace = traceId ?? Activity.Current?.Id ?? string.Empty;
        using (_logger.BeginScope(CreateScope(trace)))
        {
            _logger.LogError(messageTemplate, messageTemplateValues);
        }
    }

    public void LogError(Exception ex, string messageTemplate, string? traceId = null, params object?[] messageTemplateValues)
    {
        var trace = traceId ?? Activity.Current?.Id ?? string.Empty;
        using (_logger.BeginScope(CreateScope(trace)))
        {
            _logger.LogError(ex, messageTemplate, messageTemplateValues);
        }
    }

    private Dictionary<string, object> CreateScope(string traceId)
    {
        var scope = new Dictionary<string, object>
        {
            ["TraceId"] = traceId
        };
        if (!string.IsNullOrEmpty(_settings.ServiceName))
            scope["Service"] = _settings.ServiceName;
        return scope;
    }
}
