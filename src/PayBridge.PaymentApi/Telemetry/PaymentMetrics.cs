using System.Diagnostics.Metrics;

namespace PayBridge.PaymentApi.Telemetry;

public sealed class PaymentMetrics
{
    public const string MeterName = "PayBridge.Payments";

    private static int _circuitBreakerState;

    private readonly Counter<long> _requestsTotal;
    private readonly Counter<long> _successTotal;
    private readonly Counter<long> _fraudRejectionTotal;
    private readonly Counter<long> _providerTimeoutTotal;
    private readonly Histogram<double> _paymentLatencyMs;
    private readonly Histogram<double> _providerLatencyMs;

    public PaymentMetrics(IMeterFactory meterFactory)
    {
        var meter = meterFactory.Create(MeterName);

        _requestsTotal = meter.CreateCounter<long>(
            "payment_requests_total",
            description: "Total payment requests received");

        _successTotal = meter.CreateCounter<long>(
            "payment_success_total",
            description: "Total successful payments");

        _fraudRejectionTotal = meter.CreateCounter<long>(
            "fraud_rejection_total",
            description: "Total fraud-rejected payments");

        _providerTimeoutTotal = meter.CreateCounter<long>(
            "provider_timeout_total",
            description: "Total provider timeout events");

        _paymentLatencyMs = meter.CreateHistogram<double>(
            "payment_latency_ms",
            unit: "ms",
            description: "End-to-end payment processing latency");

        _providerLatencyMs = meter.CreateHistogram<double>(
            "provider_latency_ms",
            unit: "ms",
            description: "Provider call latency");

        meter.CreateObservableGauge(
            "circuit_breaker_state",
            () => _circuitBreakerState,
            description: "Provider circuit breaker state (0=closed, 1=half-open, 2=open)");
    }

    public void RecordRequest() => _requestsTotal.Add(1);
    public void RecordSuccess() => _successTotal.Add(1);
    public void RecordFraudRejection() => _fraudRejectionTotal.Add(1);
    public void RecordProviderTimeout() => _providerTimeoutTotal.Add(1);
    public void RecordPaymentLatency(double ms) => _paymentLatencyMs.Record(ms);
    public void RecordProviderLatency(double ms) => _providerLatencyMs.Record(ms);

    public static void SetCircuitBreakerOpen() => Interlocked.Exchange(ref _circuitBreakerState, 2);
    public static void SetCircuitBreakerHalfOpen() => Interlocked.Exchange(ref _circuitBreakerState, 1);
    public static void SetCircuitBreakerClosed() => Interlocked.Exchange(ref _circuitBreakerState, 0);
}
