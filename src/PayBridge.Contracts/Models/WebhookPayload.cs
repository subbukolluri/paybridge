namespace PayBridge.Contracts.Models;

public record WebhookPayload(
    string ProviderTransactionId,
    string Status,
    DateTime Timestamp,
    Dictionary<string, string> Metadata
);
