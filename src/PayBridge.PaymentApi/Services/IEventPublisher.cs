using PayBridge.Contracts.Events;

namespace PayBridge.PaymentApi.Services;

public interface IEventPublisher
{
    Task PublishAsync(PaymentEvent @event, CancellationToken ct = default);
}
