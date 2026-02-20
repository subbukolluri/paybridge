namespace PayBridge.Contracts.Events;

public record PaymentEvent(
    Guid EventId,
    Guid PaymentId,
    string MerchantId,
    string TenantId,
    string EventType,
    decimal Amount,
    string Currency,
    string? ProviderTransactionId,
    string? FailureReason,
    DateTime Timestamp
);
