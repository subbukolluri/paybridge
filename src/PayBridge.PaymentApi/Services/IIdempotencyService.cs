namespace PayBridge.PaymentApi.Services;

public interface IIdempotencyService
{
    /// <summary>
    /// Tries to get an existing payment ID for the given idempotency key.
    /// Returns null if no existing payment found (i.e., this is a new request).
    /// </summary>
    Task<Guid?> TryGetExistingPaymentAsync(string merchantId, string idempotencyKey,
        CancellationToken ct = default);

    /// <summary>
    /// Caches a newly created payment's idempotency key in Redis.
    /// </summary>
    Task SetAsync(string merchantId, string idempotencyKey, Guid paymentId,
        CancellationToken ct = default);
}
