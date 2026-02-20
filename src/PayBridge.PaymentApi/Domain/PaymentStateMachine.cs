using PayBridge.Contracts.Enums;

namespace PayBridge.PaymentApi.Domain;

public static class PaymentStateMachine
{
    private static readonly Dictionary<PaymentStatus, HashSet<PaymentStatus>> AllowedTransitions = new()
    {
        [PaymentStatus.Created] = new() { PaymentStatus.FraudChecking },
        [PaymentStatus.FraudChecking] = new() { PaymentStatus.Submitted, PaymentStatus.Failed },
        [PaymentStatus.Submitted] = new() { PaymentStatus.Completed, PaymentStatus.Failed },
        [PaymentStatus.Completed] = new() { PaymentStatus.Refunded },
        [PaymentStatus.Failed] = new(),
        [PaymentStatus.Refunded] = new()
    };

    public static bool IsValidTransition(PaymentStatus from, PaymentStatus to)
    {
        return AllowedTransitions.TryGetValue(from, out var allowed) && allowed.Contains(to);
    }

    public static bool IsTerminal(PaymentStatus status)
    {
        return status is PaymentStatus.Failed or PaymentStatus.Refunded;
    }
}
