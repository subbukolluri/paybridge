using Microsoft.AspNetCore.Mvc;

namespace PayBridge.ProviderService.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ProviderController : ControllerBase
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<ProviderController> _logger;
    private readonly IConfiguration _configuration;
    private static readonly Random Rng = new();

    public ProviderController(
        IHttpClientFactory httpClientFactory,
        ILogger<ProviderController> logger,
        IConfiguration configuration)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
        _configuration = configuration;
    }

    [HttpPost("process")]
    public async Task<IActionResult> ProcessPayment([FromBody] ProviderRequest request)
    {
        _logger.LogInformation(
            "Provider processing payment {PaymentId} for {Amount} {Currency}",
            request.PaymentId, request.Amount, request.Currency);

        await Task.Delay(Rng.Next(100, 500));

        var providerTxId = $"prov_{Guid.NewGuid():N}"[..20];
        var traceparent = Request.Headers["traceparent"].ToString();

        _logger.LogInformation(
            "Provider accepted payment {PaymentId}, TxId={ProviderTxId}",
            request.PaymentId, providerTxId);

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(Rng.Next(1000, 3000));
                var success = Rng.NextDouble() < 0.9;
                var status = success ? "SUCCESS" : "FAILED";

                var callbackUrl = _configuration["PaymentApi:WebhookUrl"] ?? "http://localhost:5000";
                var webhookPayload = new
                {
                    ProviderTransactionId = providerTxId,
                    Status = status,
                    Timestamp = DateTime.UtcNow,
                    Metadata = new Dictionary<string, string>
                    {
                        ["paybridge_payment_id"] = request.PaymentId,
                        ["traceparent"] = traceparent
                    }
                };

                var client = _httpClientFactory.CreateClient("webhook");
                await client.PostAsJsonAsync($"{callbackUrl}/webhooks/provider", webhookPayload);

                _logger.LogInformation(
                    "Webhook sent for {PaymentId}: Status={Status}",
                    request.PaymentId, status);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send webhook for payment {PaymentId}", request.PaymentId);
            }
        });

        return Ok(new { ProviderTransactionId = providerTxId });
    }
}

public record ProviderRequest(
    string PaymentId,
    string MerchantId,
    decimal Amount,
    string Currency,
    string PaymentMethod,
    string? CallbackUrl
);
