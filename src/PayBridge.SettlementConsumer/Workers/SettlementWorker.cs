using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OpenTelemetry;
using OpenTelemetry.Context.Propagation;
using PayBridge.Contracts.Events;
using PayBridge.SettlementConsumer.Infrastructure;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace PayBridge.SettlementConsumer.Workers;

public class SettlementWorker : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IConnection _connection;
    private readonly ILogger<SettlementWorker> _logger;

    private IChannel? _channel;

    private const string ExchangeName = "payment.events";
    private const string QueueName = "settlement.queue";
    private const string DeadLetterExchange = "settlement.deadletter";
    private const string DeadLetterQueue = "settlement.deadletter.queue";

    private static readonly ActivitySource ActivitySource = new("PayBridge.SettlementConsumer");
    private static readonly TextMapPropagator Propagator = Propagators.DefaultTextMapPropagator;

    public SettlementWorker(
        IServiceProvider serviceProvider,
        IConnection connection,
        ILogger<SettlementWorker> logger)
    {
        _serviceProvider = serviceProvider;
        _connection = connection;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _channel = await _connection.CreateChannelAsync(null, stoppingToken);

        // ── Dead Letter Setup ────────────────────────────────
        await _channel.ExchangeDeclareAsync(
            exchange: DeadLetterExchange,
            type: ExchangeType.Fanout,
            durable: true,
            cancellationToken: stoppingToken);

        await _channel.QueueDeclareAsync(
            queue: DeadLetterQueue,
            durable: true,
            exclusive: false,
            autoDelete: false,
            cancellationToken: stoppingToken);

        await _channel.QueueBindAsync(
            queue: DeadLetterQueue,
            exchange: DeadLetterExchange,
            routingKey: "",
            cancellationToken: stoppingToken);

        // ── Main Exchange & Queue ────────────────────────────
        await _channel.ExchangeDeclareAsync(
            exchange: ExchangeName,
            type: ExchangeType.Topic,
            durable: true,
            cancellationToken: stoppingToken);

        await _channel.QueueDeclareAsync(
            queue: QueueName,
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: new Dictionary<string, object>
            {
                ["x-dead-letter-exchange"] = DeadLetterExchange
            },
            cancellationToken: stoppingToken);

        await _channel.QueueBindAsync(QueueName, ExchangeName, "payment.initiated", cancellationToken: stoppingToken);
        await _channel.QueueBindAsync(QueueName, ExchangeName, "payment.completed", cancellationToken: stoppingToken);
        await _channel.QueueBindAsync(QueueName, ExchangeName, "payment.failed", cancellationToken: stoppingToken);

        await _channel.BasicQosAsync(
            prefetchSize: 0,
            prefetchCount: 10,
            global: false,
            cancellationToken: stoppingToken);

        var consumer = new AsyncEventingBasicConsumer(_channel);

        consumer.ReceivedAsync += async (sender, ea) =>
        {
            try
            {
                await ProcessMessageAsync(ea, stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unhandled error processing message, NACKing");

                await _channel.BasicNackAsync(
                    ea.DeliveryTag,
                    multiple: false,
                    requeue: false,
                    cancellationToken: stoppingToken);
            }
        };

        await _channel.BasicConsumeAsync(
            queue: QueueName,
            autoAck: false,
            consumer: consumer,
            cancellationToken: stoppingToken);

        _logger.LogInformation("Settlement consumer started, listening on {Queue}", QueueName);

        // Keep service alive
        await Task.Delay(Timeout.Infinite, stoppingToken);
    }

    private async Task ProcessMessageAsync(
        BasicDeliverEventArgs ea,
        CancellationToken ct)
    {
        if (_channel == null)
            return;

        var parentContext = Propagator.Extract(default, ea.BasicProperties.Headers,
            (headers, key) =>
            {
                if (headers != null && headers.TryGetValue(key, out var value))
                {
                    if (value is byte[] bytes)
                        return new[] { Encoding.UTF8.GetString(bytes) };
                }
                return Enumerable.Empty<string>();
            });

        using var activity = ActivitySource.StartActivity(
            "process settlement",
            ActivityKind.Consumer,
            parentContext.ActivityContext);

        activity?.SetTag("messaging.system", "rabbitmq");
        activity?.SetTag("messaging.destination", ExchangeName);
        activity?.SetTag("messaging.rabbitmq.routing_key", ea.RoutingKey);

        var body = Encoding.UTF8.GetString(ea.Body.ToArray());

        var paymentEvent = JsonSerializer.Deserialize<PaymentEvent>(
            body,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        if (paymentEvent == null)
        {
            _logger.LogWarning("Failed to deserialize payment event, discarding");

            await _channel.BasicAckAsync(ea.DeliveryTag, false, ct);
            return;
        }

        activity?.SetTag("payment.id", paymentEvent.PaymentId.ToString());
        activity?.SetTag("payment.event_type", paymentEvent.EventType);

        _logger.LogInformation(
            "Processing {EventType} for payment {PaymentId}",
            paymentEvent.EventType,
            paymentEvent.PaymentId);

        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SettlementDbContext>();

        var finalStatus = paymentEvent.EventType switch
        {
            "PaymentInitiated" => 1,
            "PaymentCompleted" => 3,
            "PaymentFailed" => 4,
            _ => 0
        };

        var record = new SettlementRecord
        {
            EventId = paymentEvent.EventId,
            PaymentId = paymentEvent.PaymentId,
            MerchantId = paymentEvent.MerchantId,
            TenantId = paymentEvent.TenantId,
            EventType = paymentEvent.EventType,
            Amount = paymentEvent.Amount,
            Currency = paymentEvent.Currency,
            FinalStatus = finalStatus,
            ProviderTransactionId = paymentEvent.ProviderTransactionId,
            FailureReason = paymentEvent.FailureReason,
            EventTimestamp = paymentEvent.Timestamp,
            PersistedAt = DateTime.UtcNow
        };

        try
        {
            db.SettlementRecords.Add(record);
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (IsDuplicateKey(ex))
        {
            _logger.LogInformation(
                "Duplicate event {EventId}, skipping",
                paymentEvent.EventId);
        }

        await _channel.BasicAckAsync(ea.DeliveryTag, false, ct);
    }

    private static bool IsDuplicateKey(DbUpdateException ex)
    {
        return ex.InnerException?.Message.Contains("unique", StringComparison.OrdinalIgnoreCase) == true
            || ex.InnerException?.Message.Contains("duplicate", StringComparison.OrdinalIgnoreCase) == true;
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_channel != null)
            await _channel.DisposeAsync();

        await base.StopAsync(cancellationToken);
    }
}