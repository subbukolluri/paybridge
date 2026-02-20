using Microsoft.EntityFrameworkCore;
using PayBridge.PaymentApi.Domain;
using PayBridge.PaymentApi.Services;

namespace PayBridge.PaymentApi.Infrastructure;

public class PaymentRepository : IPaymentRepository
{
    private readonly PayBridgeDbContext _db;

    public PaymentRepository(PayBridgeDbContext db)
    {
        _db = db;
    }

    public async Task<Payment> CreateAsync(Payment payment, CancellationToken ct = default)
    {
        _db.Payments.Add(payment);
        await _db.SaveChangesAsync(ct);
        return payment;
    }

    public async Task<Payment?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        return await _db.Payments
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == id, ct);
    }

    public async Task<Payment?> GetByIdempotencyKeyAsync(
        string merchantId, string idempotencyKey, CancellationToken ct = default)
    {
        return await _db.Payments
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.MerchantId == merchantId
                                   && p.IdempotencyKey == idempotencyKey, ct);
    }

    public async Task UpdateAsync(Payment payment, CancellationToken ct = default)
    {
        _db.Payments.Update(payment);
        await _db.SaveChangesAsync(ct);
    }
}
