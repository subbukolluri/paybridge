using PayBridge.Contracts.Enums;

namespace PayBridge.PaymentApi.Domain;

public class Payment
{
    public Guid Id { get; set; }
    public string MerchantId { get; set; } = default!;
    public string TenantId { get; set; } = default!;
    public string IdempotencyKey { get; set; } = default!;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = default!;
    public PaymentMethod Method { get; set; }
    public PaymentStatus Status { get; set; }
    public string? ProviderTransactionId { get; set; }
    public string? FailureReason { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? CompletedAt { get; set; }

    /// <summary>
    /// Stores the traceparent header so webhooks can continue the trace.
    /// </summary>
    public string? TraceParent { get; set; }

    public void TransitionTo(PaymentStatus newStatus)
    {
        if (!PaymentStateMachine.IsValidTransition(Status, newStatus))
            throw new InvalidOperationException(
                $"Invalid state transition: {Status} -> {newStatus}");

        Status = newStatus;

        if (newStatus is PaymentStatus.Completed or PaymentStatus.Failed or PaymentStatus.Refunded)
            CompletedAt = DateTime.UtcNow;
    }
}
