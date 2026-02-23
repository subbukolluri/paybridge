using PayBridge.FraudDetection;
using PayBridge.Logging;

namespace PayBridge.PaymentApi.Services;

public class FraudClient : IFraudClient
{
    private readonly FraudDetectionSvc.FraudDetectionSvcClient _client;
    private readonly IAppLogger _appLogger;

    public FraudClient(FraudDetectionSvc.FraudDetectionSvcClient client, IAppLogger appLogger)
    {
        _client = client;
        _appLogger = appLogger;
    }

    public async Task<FraudCheckResult> CheckAsync(Guid paymentId, string merchantId,
        decimal amount, string currency, string customerEmail, string paymentMethod,
        CancellationToken ct = default)
    {
        _appLogger.LogInformation(
            "Calling fraud service for payment {PaymentId}",
            null, paymentId);

        var request = new FraudCheckRequest
        {
            PaymentId = paymentId.ToString(),
            MerchantId = merchantId,
            Amount = (double)amount,
            Currency = currency,
            CustomerEmail = customerEmail,
            PaymentMethod = paymentMethod
        };

        var response = await _client.CheckTransactionAsync(request, cancellationToken: ct);

        _appLogger.LogInformation(
            "Fraud check result for {PaymentId}: Approved={Approved}, RiskScore={RiskScore}",
            null, paymentId, response.Approved, response.RiskScore);

        return new FraudCheckResult(response.Approved, response.RiskScore, response.Reason);
    }
}
