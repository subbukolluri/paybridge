namespace PayBridge.PaymentApi.Services;

public record ProviderSubmitResult(bool Accepted, string? ProviderTransactionId, string? Error);

public interface IProviderClient
{
    Task<ProviderSubmitResult> SubmitPaymentAsync(Guid paymentId, string merchantId,
        decimal amount, string currency, string paymentMethod,
        CancellationToken ct = default);
}
