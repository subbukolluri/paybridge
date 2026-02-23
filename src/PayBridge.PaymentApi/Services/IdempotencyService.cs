using StackExchange.Redis;

namespace PayBridge.PaymentApi.Services;

public class IdempotencyService : IIdempotencyService
{
    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<IdempotencyService> _logger;
    private static readonly TimeSpan Ttl = TimeSpan.FromHours(24);

    public IdempotencyService(IConnectionMultiplexer redis, ILogger<IdempotencyService> logger)
    {
        _redis = redis;
        _logger = logger;
    }

    public async Task<Guid?> TryGetExistingPaymentAsync(string merchantId, string idempotencyKey,
        CancellationToken ct = default)
    {
        try
        {
            var db = _redis.GetDatabase();
            var key = BuildKey(merchantId, idempotencyKey);
            var value = await db.StringGetAsync(key);

            if (value.HasValue && Guid.TryParse(value!, out var existingId))
            {
                _logger.LogInformation(
                    "Idempotency cache hit for {MerchantId}:{IdempotencyKey} -> {PaymentId}",
                    merchantId, idempotencyKey, existingId);
                return existingId;
            }

            return null;
        }
        catch (Exception ex)
        {
            // Redis is optional — fall through to DB constraint
            _logger.LogWarning(ex, "Redis unavailable for idempotency check, falling back to DB");
            return null;
        }
    }

    public async Task SetAsync(string merchantId, string idempotencyKey, Guid paymentId,
        CancellationToken ct = default)
    {
        try
        {
            var db = _redis.GetDatabase();
            var key = BuildKey(merchantId, idempotencyKey);
            await db.StringSetAsync(key, paymentId.ToString(), Ttl);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Redis unavailable for idempotency set, DB constraint is backup");
        }
    }

    private static string BuildKey(string merchantId, string idempotencyKey)
        => $"payment:idempotency:{merchantId}:{idempotencyKey}";
}
