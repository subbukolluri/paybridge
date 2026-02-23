namespace PayBridge.PaymentApi.Services;

public record FraudCheckResult(bool Approved, double RiskScore, string Reason);

public interface IFraudClient
{
    Task<FraudCheckResult> CheckAsync(Guid paymentId, string merchantId, decimal amount,
        string currency, string customerEmail, string paymentMethod,
        CancellationToken ct = default);
}
