using System.Diagnostics;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PayBridge.Contracts.Enums;
using PayBridge.Contracts.Events;
using PayBridge.Contracts.Models;
using PayBridge.Logging;
using PayBridge.PaymentApi.Domain;
using PayBridge.PaymentApi.Infrastructure;
using PayBridge.PaymentApi.Telemetry;

namespace PayBridge.PaymentApi.Services;

public class PaymentOrchestrator
{
    private readonly IPaymentRepository _repository;
    private readonly PayBridgeDbContext _db;
    private readonly IFraudClient _fraudClient;
    private readonly IProviderClient _providerClient;
    private readonly PaymentMetrics _metrics;
    private readonly IAppLogger _appLogger;

    public PaymentOrchestrator(
        IPaymentRepository repository,
        PayBridgeDbContext db,
        IFraudClient fraudClient,
        IProviderClient providerClient,
        PaymentMetrics metrics,
        IAppLogger appLogger)
    {
        _repository = repository;
        _db = db;
        _fraudClient = fraudClient;
        _providerClient = providerClient;
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
                EnqueueOutboxMessage(payment, "PaymentFailed");
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
            EnqueueOutboxMessage(payment, "PaymentFailed");
            await _repository.UpdateAsync(payment, ct);

            return ToResponse(payment);
        }

        // ── Submit to provider ─────────────────────────────────────────────────
        payment.TransitionTo(PaymentStatus.Submitted);
        await _repository.UpdateAsync(payment, ct);

        try
        {
            var providerResult = await _providerClient.SubmitPaymentAsync(
                payment.Id, request.MerchantId, request.Amount,
                request.Currency, request.Method.ToString(), ct);

            if (providerResult.ProviderTransactionId != null)
                payment.ProviderTransactionId = providerResult.ProviderTransactionId;

            if (!providerResult.Accepted)
            {
                payment.TransitionTo(PaymentStatus.Failed);
                payment.FailureReason = $"Provider rejected: {providerResult.Error}";
                EnqueueOutboxMessage(payment, "PaymentFailed");
                await _repository.UpdateAsync(payment, ct);
                return ToResponse(payment);
            }
        }
        catch (Exception ex)
        {
            _appLogger.LogError(ex, "Provider call failed for payment {PaymentId}", null, payment.Id);
            payment.TransitionTo(PaymentStatus.Failed);
            payment.FailureReason = "Provider call failed";
            EnqueueOutboxMessage(payment, "PaymentFailed");
            await _repository.UpdateAsync(payment, ct);
            return ToResponse(payment);
        }

        EnqueueOutboxMessage(payment, "PaymentInitiated");
        await _repository.UpdateAsync(payment, ct);

        sw.Stop();
        _metrics.RecordPaymentLatency(sw.Elapsed.TotalMilliseconds);

        _appLogger.LogInformation(
            "Payment {PaymentId} processing complete with status {Status}",
            null, payment.Id, payment.Status);

        return ToResponse(payment);
    }

    /// <summary>
    /// Returns the traceparent stored on the Payment when it was created.
    /// Used when processing webhooks from third-party providers that do not echo trace context
    /// — we restore the trace from our DB so the webhook span links to the original payment trace.
    /// </summary>
    public async Task<string?> GetStoredTraceParentAsync(Guid paymentId, CancellationToken ct = default)
    {
        var payment = await _repository.GetByIdAsync(paymentId, ct);
        return payment?.TraceParent;
    }

    public async Task HandleWebhookAsync(string providerTransactionId, string status,
        string paymentIdStr, CancellationToken ct = default)
    {
        if (!Guid.TryParse(paymentIdStr, out var paymentId))
        {
            _appLogger.LogWarning("Invalid payment ID in webhook: {PaymentId}", null, paymentIdStr);
            return;
        }

        var payment = await _repository.GetByIdAsync(paymentId, ct);
        if (payment == null)
        {
            _appLogger.LogWarning("Payment {PaymentId} not found for webhook", null, paymentId);
            return;
        }

        if (PaymentStateMachine.IsTerminal(payment.Status) || payment.Status == PaymentStatus.Completed)
        {
            _appLogger.LogInformation(
                "Payment {PaymentId} already in terminal/completed state {Status}, ignoring webhook",
                null, paymentId, payment.Status);
            return;
        }

        payment.ProviderTransactionId = providerTransactionId;

        var newStatus = status.ToUpperInvariant() switch
        {
            "SUCCESS" => PaymentStatus.Completed,
            "FAILED" => PaymentStatus.Failed,
            _ => (PaymentStatus?)null
        };

        if (newStatus == null)
        {
            _appLogger.LogWarning("Unknown webhook status: {Status}", null, status);
            return;
        }

        try
        {
            payment.TransitionTo(newStatus.Value);
        }
        catch (InvalidOperationException)
        {
            _appLogger.LogWarning(
                "Invalid state transition from webhook for payment {PaymentId}: {From} -> {To}",
                null, paymentId, payment.Status, newStatus);
            return;
        }

        if (newStatus == PaymentStatus.Failed)
            payment.FailureReason = "Provider reported failure";

        var eventType = newStatus == PaymentStatus.Completed ? "PaymentCompleted" : "PaymentFailed";
        EnqueueOutboxMessage(payment, eventType);
        await _repository.UpdateAsync(payment, ct);

        if (newStatus == PaymentStatus.Completed)
            _metrics.RecordSuccess();

        _appLogger.LogInformation("Webhook processed for payment {PaymentId}: {Status}",
            null, paymentId, newStatus);
    }

    public async Task<PaymentResponse?> GetPaymentAsync(Guid paymentId, CancellationToken ct = default)
    {
        var payment = await _repository.GetByIdAsync(paymentId, ct);
        return payment == null ? null : ToResponse(payment);
    }

    private static PaymentResponse ToResponse(Payment p) => new(
        p.Id, p.MerchantId, p.Amount, p.Currency, p.Status,
        p.ProviderTransactionId, p.FailureReason, p.CreatedAt, p.CompletedAt);

    private void EnqueueOutboxMessage(Payment payment, string eventType)
    {
        var @event = new PaymentEvent(
            EventId: Guid.NewGuid(),
            PaymentId: payment.Id,
            MerchantId: payment.MerchantId,
            TenantId: payment.TenantId,
            EventType: eventType,
            Amount: payment.Amount,
            Currency: payment.Currency,
            ProviderTransactionId: payment.ProviderTransactionId,
            FailureReason: payment.FailureReason,
            Timestamp: DateTime.UtcNow);

        _db.OutboxMessages.Add(new OutboxMessage
        {
            EventId = @event.EventId,
            EventType = eventType,
            Payload = JsonSerializer.Serialize(@event),
            CreatedAt = DateTime.UtcNow,
            TraceParent = Activity.Current?.Id
        });
    }

    private static bool IsDuplicateKeyException(DbUpdateException ex)
    {
        return ex.InnerException?.Message.Contains("unique", StringComparison.OrdinalIgnoreCase) == true
            || ex.InnerException?.Message.Contains("duplicate", StringComparison.OrdinalIgnoreCase) == true;
    }
}
