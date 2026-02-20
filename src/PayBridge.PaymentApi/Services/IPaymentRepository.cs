using PayBridge.PaymentApi.Domain;

namespace PayBridge.PaymentApi.Services;

public interface IPaymentRepository
{
    Task<Payment> CreateAsync(Payment payment, CancellationToken ct = default);
    Task<Payment?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<Payment?> GetByIdempotencyKeyAsync(string merchantId, string idempotencyKey, CancellationToken ct = default);
    Task UpdateAsync(Payment payment, CancellationToken ct = default);
}
