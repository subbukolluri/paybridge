using System.Diagnostics;

namespace PayBridge.PaymentApi.Telemetry;

public static class DiagnosticConfig
{
    public const string ServiceName = "PayBridge.PaymentApi";
    public static readonly ActivitySource ActivitySource = new(ServiceName);
}
