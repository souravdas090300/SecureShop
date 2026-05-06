namespace SecureShop.Application.Interfaces;

/// <summary>
/// Provides a strongly-typed, async cache abstraction over an underlying store
/// (Redis in production, an in-memory no-op in development without Redis).
/// All keys are plain strings; values are serialised to JSON before storage.
/// </summary>
public interface ICacheService
{
    /// <summary>
    /// Retrieves a cached value by key.
    /// </summary>
    /// <typeparam name="T">Expected CLR type of the cached value.</typeparam>
    /// <param name="key">Unique cache key.</param>
    /// <returns>The deserialised value, or <c>null</c> / <c>default</c> on a cache miss.</returns>
    Task<T?> GetAsync<T>(string key);

    /// <summary>
    /// Stores a value in the cache under the given key.
    /// </summary>
    /// <typeparam name="T">CLR type of the value to cache.</typeparam>
    /// <param name="key">Unique cache key.</param>
    /// <param name="value">Object to serialise and cache.</param>
    /// <param name="expiry">
    /// How long the entry should live; defaults to 5 minutes when <c>null</c>.
    /// </param>
    Task SetAsync<T>(string key, T value, TimeSpan? expiry = null);

    /// <summary>Removes a single entry identified by <paramref name="key"/>.</summary>
    /// <param name="key">Cache key to delete.</param>
    Task RemoveAsync(string key);

    /// <summary>
    /// Removes all cache entries whose keys begin with <paramref name="prefix"/>.
    /// Used to invalidate an entire category of results (e.g. all product list pages)
    /// after a write operation.
    /// </summary>
    /// <param name="prefix">Key prefix to match against.</param>
    Task RemoveByPrefixAsync(string prefix);
}