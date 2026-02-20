using PayBridge.Contracts.Enums;

namespace PayBridge.Contracts.Models;

public record PaymentResponse(
    Guid Id,
    string MerchantId,
    decimal Amount,
    string Currency,
    PaymentStatus Status,
    string? ProviderTransactionId,
    string? FailureReason,
    DateTime CreatedAt,
    DateTime? CompletedAt
);
