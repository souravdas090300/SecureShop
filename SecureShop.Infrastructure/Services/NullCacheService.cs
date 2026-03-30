using SecureShop.Application.Interfaces;

namespace SecureShop.Infrastructure.Services;

/// <summary>
/// Null implementation of ICacheService for when Redis is unavailable.
/// Used in development when Redis connection fails - allows app to start.
/// </summary>
public class NullCacheService : ICacheService
{
    public Task<T?> GetAsync<T>(string key)
    {
        return Task.FromResult<T?>(default);
    }

    public Task SetAsync<T>(string key, T value, TimeSpan? expiry = null)
    {
        return Task.CompletedTask;
    }

    public Task RemoveAsync(string key)
    {
        return Task.CompletedTask;
    }
}
