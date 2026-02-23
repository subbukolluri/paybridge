using Microsoft.Extensions.Diagnostics.HealthChecks;
using RabbitMQ.Client;

namespace PayBridge.PaymentApi.HealthChecks;

public sealed class RabbitMQHealthCheck : IHealthCheck
{
    private readonly IConnection _connection;

    public RabbitMQHealthCheck(IConnection connection)
    {
        _connection = connection;
    }

    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        if (_connection.IsOpen)
            return Task.FromResult(HealthCheckResult.Healthy("RabbitMQ connection is open."));

        return Task.FromResult(HealthCheckResult.Unhealthy("RabbitMQ connection is not open."));
    }
}
