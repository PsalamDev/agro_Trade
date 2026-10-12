namespace AgroTrade.Infrastructure.Caching;

/// <summary>
/// Caching knobs (plan §15 — Redis caching). Redis is opted in with
/// <c>ConnectionStrings:Redis</c>; with nothing configured the app runs on an in-process
/// distributed memory cache, which is enough for development, tests and a single node.
/// </summary>
public sealed class CacheOptions
{
    public const string SectionName = "Cache";

    /// <summary>
    /// Key namespace applied by the Redis provider, so one server can host several environments
    /// without collisions. The in-memory provider is process-local and ignores it.
    /// </summary>
    public string KeyPrefix { get; set; } = "agrotrade:";

    /// <summary>Default absolute TTL for a cached read whose caller does not pick its own.</summary>
    public int DefaultTtlSeconds { get; set; } = 300;

    /// <summary>
    /// Ceiling on a single cache call. A slow or unreachable cache is abandoned after this and the
    /// request falls through to the source of truth. Redis also uses it as its connect/sync timeout.
    /// </summary>
    public int OperationTimeoutMilliseconds { get; set; } = 500;

    /// <summary>
    /// After a cache failure the cache is skipped for this many seconds, so a backend that is down is
    /// paid for once rather than on every request. Zero disables the cooldown.
    /// </summary>
    public int FailureCooldownSeconds { get; set; } = 30;
}
