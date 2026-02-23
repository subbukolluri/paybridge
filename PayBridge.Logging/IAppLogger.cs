namespace PayBridge.Logging;

/// <summary>
/// Structured application logging with template placeholders and correlation (TraceId).
/// Use {Named} placeholders in the message template; pass values as parameters. No string interpolation.
/// </summary>
public interface IAppLogger
{
    void LogInformation(string messageTemplate, string? traceId = null, params object?[] messageTemplateValues);
    void LogWarning(string messageTemplate, string? traceId = null, params object?[] messageTemplateValues);
    void LogError(string messageTemplate, string? traceId = null, params object?[] messageTemplateValues);
    void LogError(Exception ex, string messageTemplate, string? traceId = null, params object?[] messageTemplateValues);
}
