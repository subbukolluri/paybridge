using System.Diagnostics;
using System.Net.Http.Json;
using PayBridge.PaymentApi.Telemetry;

namespace PayBridge.PaymentApi.Services;

public class ProviderClient : IProviderClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<ProviderClient> _logger;
    private readonly PaymentMetrics _metrics;

    public ProviderClient(HttpClient httpClient, ILogger<ProviderClient> logger, PaymentMetrics metrics)
    {
        _httpClient = httpClient;
        _logger = logger;
        _metrics = metrics;
    }

    public async Task<ProviderSubmitResult> SubmitPaymentAsync(Guid paymentId, string merchantId,
        decimal amount, string currency, string paymentMethod,
        CancellationToken ct = default)
    {
        _logger.LogInformation("Submitting payment {PaymentId} to provider", paymentId);

        var sw = Stopwatch.StartNew();
        try
        {
            var payload = new
            {
                PaymentId = paymentId.ToString(),
                MerchantId = merchantId,
                Amount = amount,
                Currency = currency,
                PaymentMethod = paymentMethod,
                CallbackUrl = "/webhooks/provider"
            };

            var response = await _httpClient.PostAsJsonAsync("/api/provider/process", payload, ct);
            sw.Stop();
            _metrics.RecordProviderLatency(sw.Elapsed.TotalMilliseconds);

            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync(ct);
                _logger.LogWarning("Provider rejected payment {PaymentId}: {Error}", paymentId, error);
                return new ProviderSubmitResult(false, null, error);
            }

            var result = await response.Content.ReadFromJsonAsync<ProviderResponse>(ct);
            _logger.LogInformation("Provider accepted payment {PaymentId}: TxId={TxId}",
                paymentId, result?.ProviderTransactionId);

            return new ProviderSubmitResult(true, result?.ProviderTransactionId, null);
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            sw.Stop();
            _metrics.RecordProviderTimeout();
            _logger.LogWarning("Provider call timed out for payment {PaymentId}", paymentId);
            return new ProviderSubmitResult(true, null, null);
        }
    }

    private record ProviderResponse(string ProviderTransactionId);
}
