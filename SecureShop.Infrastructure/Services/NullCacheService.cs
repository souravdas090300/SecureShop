using SecureShop.Application.Interfaces;

namespace SecureShop.Infrastructure.Services;

/// <summary>
/// No-op cache used when Redis is not configured.
/// Every read returns null (cache miss), writes and deletes are silently ignored.
/// This lets the app run locally without Redis — data always comes from the database.
/// FIX: README claimed "Redis optional — falls back gracefully".
///      Previously this was false. Now it is true.
/// </summary>
public class NullCacheService : ICacheService
{
    public Task<T?> GetAsync<T>(string key) =>
        Task.FromResult<T?>(default);

    public Task SetAsync<T>(string key, T value, TimeSpan? expiry = null) =>
        Task.CompletedTask;

    public Task RemoveAsync(string key) =>
        Task.CompletedTask;

    public Task RemoveByPrefixAsync(string prefix) =>
        Task.CompletedTask;
}
