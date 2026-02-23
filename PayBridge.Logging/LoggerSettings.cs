namespace PayBridge.Logging;

/// <summary>
/// Configuration for the structured logger. Bind from "Logging" or "Logger" section in appsettings.
/// </summary>
public class LoggerSettings
{
    /// <summary>
    /// Service or application name (e.g. PayBridge.PaymentApi). Included in log scope for filtering in Loki/Grafana.
    /// </summary>
    public string ServiceName { get; set; } = string.Empty;
}
