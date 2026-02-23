using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using PayBridge.Contracts.Enums;
using PayBridge.Contracts.Models;
using PayBridge.Logging;
using PayBridge.PaymentApi.Domain;
using PayBridge.PaymentApi.Telemetry;

namespace PayBridge.PaymentApi.Services;

public class PaymentOrchestrator
{
    private readonly IPaymentRepository _repository;
    private readonly IFraudClient _fraudClient;
    private readonly PaymentMetrics _metrics;
    private readonly IAppLogger _appLogger;

    public PaymentOrchestrator(
        IPaymentRepository repository,
        IFraudClient fraudClient,
        PaymentMetrics metrics,
        IAppLogger appLogger)
    {
        _repository = repository;
        _fraudClient = fraudClient;
        _metrics = metrics;
        _appLogger = appLogger;
    }

    public async Task<PaymentResponse> ProcessPaymentAsync(
        CreatePaymentRequest request, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        _metrics.RecordRequest();

        using var activity = DiagnosticConfig.ActivitySource.StartActivity("ProcessPayment");
        activity?.SetTag("payment.merchant_id", request.MerchantId);
        activity?.SetTag("payment.currency", request.Currency);
        activity?.SetTag("payment.method", request.Method.ToString());

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
            CreatedAt = DateTime.UtcNow,
            TraceParent = Activity.Current?.Id
        };

        activity?.SetTag("payment.id", payment.Id.ToString());

        try
        {
            await _repository.CreateAsync(payment, ct);
        }
        catch (DbUpdateException ex) when (IsDuplicateKeyException(ex))
        {
            _appLogger.LogInformation(
                "Duplicate idempotency key detected via DB constraint for {MerchantId}/{IdempotencyKey}",
                null, request.MerchantId, request.IdempotencyKey);
            var existing = await _repository.GetByIdempotencyKeyAsync(
                request.MerchantId, request.IdempotencyKey, ct);
            return ToResponse(existing!);
        }

        // ── Fraud check ──────────────────────────────────────────────────────
        payment.TransitionTo(PaymentStatus.FraudChecking);
        await _repository.UpdateAsync(payment, ct);

        try
        {
            var fraudResult = await _fraudClient.CheckAsync(
                payment.Id, request.MerchantId, request.Amount,
                request.Currency, request.CustomerEmail, request.Method.ToString(), ct);

            if (!fraudResult.Approved)
            {
                _metrics.RecordFraudRejection();
                payment.TransitionTo(PaymentStatus.Failed);
                payment.FailureReason = $"Fraud rejected: {fraudResult.Reason} (score: {fraudResult.RiskScore:F2})";
                await _repository.UpdateAsync(payment, ct);

                _appLogger.LogInformation(
                    "Payment {PaymentId} rejected by fraud service",
                    null, payment.Id);
                return ToResponse(payment);
            }
        }
        catch (Exception ex)
        {
            _appLogger.LogError(ex,
                "Fraud service failed for payment {PaymentId}",
                null, payment.Id);
            payment.TransitionTo(PaymentStatus.Failed);
            payment.FailureReason = "Fraud service unavailable";
            await _repository.UpdateAsync(payment, ct);

            return ToResponse(payment);
        }

        // ── Fraud approved — mark as submitted (provider integration comes next)
        payment.TransitionTo(PaymentStatus.Submitted);
        await _repository.UpdateAsync(payment, ct);

        sw.Stop();
        _metrics.RecordPaymentLatency(sw.Elapsed.TotalMilliseconds);

        _appLogger.LogInformation(
            "Payment {PaymentId} processing complete with status {Status}",
            null, payment.Id, payment.Status);

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
