namespace PayBridge.Contracts.Enums;

public enum PaymentStatus
{
    Created,
    FraudChecking,
    Submitted,
    Completed,
    Failed,
    Refunded
}
