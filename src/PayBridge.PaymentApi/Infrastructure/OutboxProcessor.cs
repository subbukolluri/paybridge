using System.Diagnostics;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PayBridge.Contracts.Events;
using PayBridge.PaymentApi.Services;
using PayBridge.PaymentApi.Telemetry;

namespace PayBridge.PaymentApi.Infrastructure;

/// <summary>
/// Polls OutboxMessages and publishes pending events to RabbitMQ.
/// Ensures at-least-once delivery even when the broker is temporarily unavailable.
/// </summary>
public class OutboxProcessor : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IEventPublisher _eventPublisher;
    private readonly ILogger<OutboxProcessor> _logger;

    private const int BatchSize = 20;
    private const int MaxRetries = 5;
    private static readonly TimeSpan PollingInterval = TimeSpan.FromSeconds(1);

    public OutboxProcessor(
        IServiceScopeFactory scopeFactory,
        IEventPublisher eventPublisher,
        ILogger<OutboxProcessor> logger)
    {
        _scopeFactory = scopeFactory;
        _eventPublisher = eventPublisher;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("OutboxProcessor started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessBatchAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "OutboxProcessor error, will retry next cycle");
            }

            await Task.Delay(PollingInterval, stoppingToken);
        }
    }

    private async Task ProcessBatchAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PayBridgeDbContext>();

        var pending = await db.OutboxMessages
            .Where(m => m.ProcessedAt == null && m.RetryCount < MaxRetries)
            .OrderBy(m => m.CreatedAt)
            .Take(BatchSize)
            .ToListAsync(ct);

        if (pending.Count == 0)
            return;

        foreach (var message in pending)
        {
            try
            {
                using var activity = DiagnosticConfig.ActivitySource.StartActivity(
                    "outbox.publish", ActivityKind.Producer);
                activity?.SetTag("messaging.event_type", message.EventType);
                activity?.SetTag("messaging.event_id", message.EventId.ToString());

                var @event = JsonSerializer.Deserialize<PaymentEvent>(message.Payload);
                if (@event == null)
                {
                    _logger.LogWarning("Failed to deserialize outbox message {MessageId}, skipping", message.Id);
                    message.RetryCount = MaxRetries;
                    continue;
                }

                await _eventPublisher.PublishAsync(@event, ct);
                message.ProcessedAt = DateTime.UtcNow;

                _logger.LogInformation(
                    "Outbox message {MessageId} published: {EventType} for payment {PaymentId}",
                    message.Id, message.EventType, @event.PaymentId);
            }
            catch (Exception ex)
            {
                message.RetryCount++;
                _logger.LogWarning(ex,
                    "Failed to publish outbox message {MessageId} (attempt {RetryCount}/{MaxRetries})",
                    message.Id, message.RetryCount, MaxRetries);
            }
        }

        await db.SaveChangesAsync(ct);
    }
}
