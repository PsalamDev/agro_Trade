using System.Text.Json;
using AgroTrade.Application.Common;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AgroTrade.Infrastructure.Caching;

/// <summary>
/// Cache-aside over <see cref="IDistributedCache"/>, so the same code runs against the in-process
/// memory cache in development and against Redis in a scaled deployment (the provider is chosen in
/// <c>AddCaching</c>). Values are JSON-serialised.
/// </summary>
/// <remarks>
/// Every operation is best-effort and bounded. A cache that is down, misconfigured, or holding a
/// value written by an older shape must never fail the request that wanted it, and it must not hold
/// that request open either: each call is capped at
/// <see cref="CacheOptions.OperationTimeoutMilliseconds"/>, after which a read becomes a miss and a
/// write is dropped, both logged. After a failure the cache is skipped entirely for
/// <see cref="CacheOptions.FailureCooldownSeconds"/> so a down backend is paid for once, not on every
/// request. A shared cache in front of the database should not become a new way for a request to
/// hang or to slow down.
/// </remarks>
public sealed class DistributedCacheService(
    IDistributedCache cache,
    IOptions<CacheOptions> options,
    ILogger<DistributedCacheService> logger) : ICache
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly TimeSpan _operationTimeout =
        TimeSpan.FromMilliseconds(Math.Max(1, options.Value.OperationTimeoutMilliseconds));

    private readonly TimeSpan _cooldown =
        TimeSpan.FromSeconds(Math.Max(0, options.Value.FailureCooldownSeconds));

    // Ticks until which the cache is bypassed after a failure; 0 means "healthy, use the cache".
    private long _bypassUntilTicks;

    public async Task<T?> GetAsync<T>(string key, CancellationToken ct = default)
        where T : class
    {
        if (IsBypassed)
        {
            return null;
        }

        try
        {
            var payload = await RunAsync(token => cache.GetStringAsync(key, token), ct);
            return payload is null ? null : JsonSerializer.Deserialize<T>(payload, Json);
        }
        catch (Exception ex) when (IsCacheFailure(ex, ct))
        {
            Trip();
            logger.LogWarning(ex, "Cache read failed for {Key}; falling back to the source.", key);
            return null;
        }
    }

    public async Task SetAsync<T>(string key, T value, TimeSpan ttl, CancellationToken ct = default)
        where T : class
    {
        if (IsBypassed)
        {
            return;
        }

        try
        {
            var payload = JsonSerializer.Serialize(value, Json);
            await RunAsync(
                token => cache.SetStringAsync(key, payload, new DistributedCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = ttl,
                }, token),
                ct);
        }
        catch (Exception ex) when (IsCacheFailure(ex, ct))
        {
            Trip();
            logger.LogWarning(ex, "Cache write failed for {Key}; the value was not cached.", key);
        }
    }

    public async Task RemoveAsync(string key, CancellationToken ct = default)
    {
        if (IsBypassed)
        {
            return;
        }

        try
        {
            await RunAsync(token => cache.RemoveAsync(key, token), ct);
        }
        catch (Exception ex) when (IsCacheFailure(ex, ct))
        {
            Trip();
            logger.LogWarning(ex, "Cache eviction failed for {Key}.", key);
        }
    }

    public async Task<T> GetOrCreateAsync<T>(
        string key,
        Func<CancellationToken, Task<T>> factory,
        TimeSpan ttl,
        CancellationToken ct = default)
        where T : class
    {
        if (await GetAsync<T>(key, ct) is { } cached)
        {
            return cached;
        }

        var value = await factory(ct);
        await SetAsync(key, value, ttl, ct);
        return value;
    }

    private bool IsBypassed =>
        _cooldown > TimeSpan.Zero && DateTime.UtcNow.Ticks < Interlocked.Read(ref _bypassUntilTicks);

    private void Trip()
    {
        if (_cooldown > TimeSpan.Zero)
        {
            Interlocked.Exchange(ref _bypassUntilTicks, DateTime.UtcNow.Add(_cooldown).Ticks);
        }
    }

    /// <summary>
    /// Runs a cache call under a deadline so an unreachable backend cannot hold a request open.
    /// The caller's own cancellation still wins: it cancels the linked token immediately.
    /// </summary>
    private async Task<T> RunAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken ct)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(_operationTimeout);
        return await operation(deadline.Token);
    }

    /// <inheritdoc cref="RunAsync{T}(Func{CancellationToken, Task{T}}, CancellationToken)"/>
    private async Task RunAsync(Func<CancellationToken, Task> operation, CancellationToken ct)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(_operationTimeout);
        await operation(deadline.Token);
    }

    /// <summary>
    /// Whether a failure belongs to the cache rather than the caller. A caller cancellation must
    /// propagate; a deadline this class imposed is just a miss.
    /// </summary>
    private static bool IsCacheFailure(Exception ex, CancellationToken ct) =>
        ex is not OperationCanceledException || !ct.IsCancellationRequested;
}
