using PayBridge.Contracts.Enums;

namespace PayBridge.Contracts.Models;

public record CreatePaymentRequest(
    string MerchantId,
    string IdempotencyKey,
    decimal Amount,
    string Currency,
    string CustomerEmail,
    PaymentMethod Method,
    Dictionary<string, string>? Metadata
);
