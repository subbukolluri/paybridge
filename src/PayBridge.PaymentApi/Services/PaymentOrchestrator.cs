using Microsoft.EntityFrameworkCore;
using PayBridge.Contracts.Enums;
using PayBridge.Contracts.Models;
using PayBridge.PaymentApi.Domain;

namespace PayBridge.PaymentApi.Services;

public class PaymentOrchestrator
{
    private readonly IPaymentRepository _repository;
    private readonly ILogger<PaymentOrchestrator> _logger;

    public PaymentOrchestrator(IPaymentRepository repository, ILogger<PaymentOrchestrator> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    public async Task<PaymentResponse> ProcessPaymentAsync(
        CreatePaymentRequest request, CancellationToken ct = default)
    {
        var payment = new Payment
        {
            Id = Guid.NewGuid(),
            MerchantId = request.MerchantId,
            TenantId = request.MerchantId,
            IdempotencyKey = request.IdempotencyKey,
            Amount = request.Amount,
            Currency = request.Currency,
            Method = request.Method,
            Status = PaymentStatus.Created,
            CreatedAt = DateTime.UtcNow
        };

        try
        {
            await _repository.CreateAsync(payment, ct);
        }
        catch (DbUpdateException ex) when (IsDuplicateKeyException(ex))
        {
            _logger.LogInformation("Duplicate idempotency key detected via DB constraint");
            var existing = await _repository.GetByIdempotencyKeyAsync(
                request.MerchantId, request.IdempotencyKey, ct);
            return ToResponse(existing!);
        }

        _logger.LogInformation(
            "Payment {PaymentId} created for merchant {MerchantId}, amount {Amount} {Currency}",
            payment.Id, payment.MerchantId, payment.Amount, payment.Currency);

        return ToResponse(payment);
    }

    public async Task<PaymentResponse?> GetPaymentAsync(Guid paymentId, CancellationToken ct = default)
    {
        var payment = await _repository.GetByIdAsync(paymentId, ct);
        return payment == null ? null : ToResponse(payment);
    }

    private static PaymentResponse ToResponse(Payment p) => new(
        p.Id, p.MerchantId, p.Amount, p.Currency, p.Status,
        p.ProviderTransactionId, p.FailureReason, p.CreatedAt, p.CompletedAt);

    private static bool IsDuplicateKeyException(DbUpdateException ex)
    {
        return ex.InnerException?.Message.Contains("unique", StringComparison.OrdinalIgnoreCase) == true
            || ex.InnerException?.Message.Contains("duplicate", StringComparison.OrdinalIgnoreCase) == true;
    }
}
