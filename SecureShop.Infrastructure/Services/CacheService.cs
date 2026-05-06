using System.Text.Json;
using SecureShop.Application.Interfaces;
using StackExchange.Redis;

namespace SecureShop.Infrastructure.Services;

/// <summary>
/// Redis-backed implementation of <see cref="ICacheService"/>.
/// Values are serialised to JSON strings before being stored in Redis
/// and deserialised on retrieval.
/// </summary>
public class CacheService : ICacheService
{
    private readonly IConnectionMultiplexer _redis;

    // Pre-resolved database handle; kept as a field to avoid a GetDatabase() call per operation.
    private readonly IDatabase _db;

    /// <summary>
    /// Initialises the service and resolves the default Redis database.
    /// </summary>
    /// <param name="redis">Open multiplexer connection injected by DI.</param>
    public CacheService(IConnectionMultiplexer redis)
    {
        _redis = redis;
        _db = redis.GetDatabase();
    }

    /// <inheritdoc />
    public async Task<T?> GetAsync<T>(string key)
    {
        var value = await _db.StringGetAsync(key);

        // Return default when the key doesn't exist or has expired.
        return value.IsNullOrEmpty ? default : JsonSerializer.Deserialize<T>(value!);
    }

    /// <inheritdoc />
    public async Task SetAsync<T>(string key, T value, TimeSpan? expiry = null)
    {
        var json = JsonSerializer.Serialize(value);

        // Default TTL of 5 minutes applies when the caller doesn't specify one.
        await _db.StringSetAsync(key, json, expiry ?? TimeSpan.FromMinutes(5));
    }

    /// <inheritdoc />
    public async Task RemoveAsync(string key)
    {
        await _db.KeyDeleteAsync(key);
    }

    /// <inheritdoc />
    public async Task RemoveByPrefixAsync(string prefix)
    {
        // SCAN through all keys matching the prefix on the first Redis endpoint.
        // This avoids KEYS which is blocking and unsuitable for production.
        var server = _redis.GetServer(_redis.GetEndPoints().First());
        var keys = server.Keys(pattern: $"{prefix}*").ToArray();

        // Batch-delete in a single pipeline call to minimise round-trips.
        if (keys.Length > 0)
            await _db.KeyDeleteAsync(keys);
    }
}
