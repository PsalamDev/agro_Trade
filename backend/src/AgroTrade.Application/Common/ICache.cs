namespace AgroTrade.Application.Common;

/// <summary>
/// Cache-aside store for read-heavy, rarely-changing data (plan §15 — Redis caching).
/// Implementations namespace and serialise values. A miss and a cache failure are deliberately
/// indistinguishable to a caller: when the cache cannot help, the source of truth is consulted.
/// </summary>
/// <remarks>
/// Values are reference types (DTOs, lists). A caller that writes the underlying data is
/// responsible for evicting the key it affects — see <c>CatalogService</c> for the pattern.
/// </remarks>
public interface ICache
{
    /// <summary>Returns the cached value, or <c>null</c> on a miss or when the cache is unavailable.</summary>
    Task<T?> GetAsync<T>(string key, CancellationToken ct = default)
        where T : class;

    /// <summary>Stores <paramref name="value"/> under <paramref name="key"/> for <paramref name="ttl"/>.</summary>
    Task SetAsync<T>(string key, T value, TimeSpan ttl, CancellationToken ct = default)
        where T : class;

    /// <summary>Evicts a key. Called whenever the data behind it is written.</summary>
    Task RemoveAsync(string key, CancellationToken ct = default);

    /// <summary>
    /// Returns the cached value for <paramref name="key"/>, or runs <paramref name="factory"/> and
    /// caches its result on a miss. A cache read or write that fails never surfaces to the caller:
    /// the factory result is returned regardless.
    /// </summary>
    Task<T> GetOrCreateAsync<T>(
        string key,
        Func<CancellationToken, Task<T>> factory,
        TimeSpan ttl,
        CancellationToken ct = default)
        where T : class;
}
