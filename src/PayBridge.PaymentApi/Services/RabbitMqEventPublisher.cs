using PayBridge.Contracts.Events;
using RabbitMQ.Client;
using System.Text;
using System.Text.Json;

namespace PayBridge.PaymentApi.Services;

public class RabbitMqEventPublisher : IEventPublisher, IAsyncDisposable
{
    private readonly IConnection _connection;
    private readonly ILogger<RabbitMqEventPublisher> _logger;
    private IChannel? _channel;

    private const string ExchangeName = "payment.events";

    public RabbitMqEventPublisher(
        IConnection connection,
        ILogger<RabbitMqEventPublisher> logger)
    {
        _connection = connection;
        _logger = logger;
    }

    private async Task<IChannel> GetChannelAsync()
    {
        if (_channel is not null)
            return _channel;

        _channel = await _connection.CreateChannelAsync();

        await _channel.ExchangeDeclareAsync(
            exchange: ExchangeName,
            type: ExchangeType.Topic,
            durable: true);

        return _channel;
    }

    public async Task PublishAsync(PaymentEvent @event, CancellationToken ct = default)
    {
        var channel = await GetChannelAsync();

        var routingKey = @event.EventType switch
        {
            "PaymentInitiated" => "payment.initiated",
            "PaymentCompleted" => "payment.completed",
            "PaymentFailed" => "payment.failed",
            _ => "payment.unknown"
        };

        var properties = new BasicProperties
        {
            ContentType = "application/json",
            DeliveryMode = DeliveryModes.Persistent,
            MessageId = @event.EventId.ToString(),
            Headers = new Dictionary<string, object>()
        };

        var body = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(@event));

        await channel.BasicPublishAsync(
            exchange: ExchangeName,
            routingKey: routingKey,
            mandatory: false,
            basicProperties: properties,
            body: body,
            cancellationToken: ct);

        _logger.LogInformation(
            "Published {EventType} for payment {PaymentId}",
            @event.EventType,
            @event.PaymentId);
    }

    public async ValueTask DisposeAsync()
    {
        if (_channel is not null)
            await _channel.DisposeAsync();
    }
}
