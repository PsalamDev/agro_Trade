using System.Diagnostics;
using AgroTrade.Infrastructure.Caching;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AgroTrade.Tests.Unit;

/// <summary>
/// The cache-aside store that sits in front of the database.
/// </summary>
/// <remarks>
/// Two claims are load-bearing. A value written is a value read back — the JSON round-trip and the
/// provider agree. And a cache that has failed is a miss, never an error: the database stays the
/// source of truth even when Redis is having a bad day. The suite runs the same implementation here
/// that production runs against Redis, over an in-process <see cref="MemoryDistributedCache"/>.
/// </remarks>
public sealed class DistributedCacheServiceTests
{
    private static readonly TimeSpan DefaultTtl = TimeSpan.FromMinutes(5);

    [Fact]
    public async Task SetThenGet_RoundTripsAValue()
    {
        var cache = Cache();
        var value = new CachedThing(Guid.NewGuid(), "Cocoa", ["NG", "GH"]);

        await cache.SetAsync("k", value, DefaultTtl);
        var read = await cache.GetAsync<CachedThing>("k");

        Assert.NotNull(read);
        Assert.Equal(value.Id, read!.Id);
        Assert.Equal("Cocoa", read.Name);
        Assert.Equal(new[] { "NG", "GH" }, read.Countries);
    }

    [Fact]
    public async Task Get_OnAMiss_ReturnsNull()
    {
        Assert.Null(await Cache().GetAsync<CachedThing>("absent"));
    }

    [Fact]
    public async Task Remove_EvictsTheKey()
    {
        var cache = Cache();
        await cache.SetAsync("k", new CachedThing(Guid.NewGuid(), "x", []), DefaultTtl);

        await cache.RemoveAsync("k");

        Assert.Null(await cache.GetAsync<CachedThing>("k"));
    }

    [Fact]
    public async Task GetOrCreate_RunsTheFactoryOnce_ThenServesFromTheCache()
    {
        var cache = Cache();
        var calls = 0;
        Task<CachedThing> Factory(CancellationToken _)
        {
            calls++;
            return Task.FromResult(new CachedThing(Guid.Empty, "a", []));
        }

        await cache.GetOrCreateAsync("k", Factory, DefaultTtl);
        var second = await cache.GetOrCreateAsync("k", Factory, DefaultTtl);

        Assert.Equal(1, calls);
        Assert.Equal("a", second.Name);
    }

    [Fact]
    public async Task GetOrCreate_WhenTheCacheIsUnreachable_StillReturnsTheFactoryValue()
    {
        var cache = Cache(new UnreachableDistributedCache());

        var value = await cache.GetOrCreateAsync(
            "k",
            _ => Task.FromResult(new CachedThing(Guid.Empty, "fresh", [])),
            DefaultTtl);

        Assert.Equal("fresh", value.Name);
    }

    [Fact]
    public async Task GetOrCreate_WhenTheCacheHangs_FallsBackWithoutWaitingForIt()
    {
        // A cache that accepts the connection and then never answers is the case a database-backed
        // request must not inherit as its own latency.
        var cache = Cache(new HangingDistributedCache(), operationTimeoutMs: 50);
        var stopwatch = Stopwatch.StartNew();

        var value = await cache.GetOrCreateAsync(
            "k",
            _ => Task.FromResult(new CachedThing(Guid.Empty, "fresh", [])),
            DefaultTtl);

        stopwatch.Stop();
        Assert.Equal("fresh", value.Name);
        Assert.True(stopwatch.ElapsedMilliseconds < 2000, $"expected a fast fallback, took {stopwatch.ElapsedMilliseconds} ms");
    }

    [Fact]
    public async Task GetOrCreate_AfterAFailure_BypassesTheCacheUntilTheCooldown()
    {
        // One slow failure should not become a latency tax on every later request.
        var inner = new UnreachableDistributedCache();
        var cache = Cache(inner, operationTimeoutMs: 50);

        await cache.GetOrCreateAsync("k", _ => Task.FromResult(new CachedThing(Guid.Empty, "a", [])), DefaultTtl);
        var afterFirst = inner.CallCount;
        Assert.True(afterFirst > 0, "the first request should have tried the cache.");

        var second = await cache.GetOrCreateAsync("k2", _ => Task.FromResult(new CachedThing(Guid.Empty, "b", [])), DefaultTtl);

        Assert.Equal(afterFirst, inner.CallCount);
        Assert.Equal("b", second.Name);
    }

    [Fact]
    public async Task ExpiredEntry_ReadsAsAMiss()
    {
        var cache = Cache();
        await cache.SetAsync("k", new CachedThing(Guid.Empty, "x", []), TimeSpan.FromMilliseconds(50));

        await Task.Delay(300);

        Assert.Null(await cache.GetAsync<CachedThing>("k"));
    }

    private static DistributedCacheService Cache(
        IDistributedCache? inner = null,
        int operationTimeoutMs = 500,
        int cooldownSeconds = 30) =>
        new(
            inner ?? InProcessCache(),
            Options.Create(new CacheOptions
            {
                OperationTimeoutMilliseconds = operationTimeoutMs,
                FailureCooldownSeconds = cooldownSeconds,
            }),
            NullLogger<DistributedCacheService>.Instance);

    /// <summary>
    /// The same provider the app falls back to when no Redis is configured
    /// (<c>AddDistributedMemoryCache</c>), so these tests exercise the real wiring.
    /// </summary>
    private static IDistributedCache InProcessCache() =>
        new ServiceCollection()
            .AddDistributedMemoryCache()
            .BuildServiceProvider()
            .GetRequiredService<IDistributedCache>();

    public sealed record CachedThing(Guid Id, string Name, string[] Countries);

    /// <summary>A cache that is always down, the way Redis is during an outage.</summary>
    private sealed class UnreachableDistributedCache : IDistributedCache
    {
        public int CallCount;

        private InvalidOperationException Down()
        {
            Interlocked.Increment(ref CallCount);
            return new InvalidOperationException("redis is unreachable");
        }

        public byte[]? Get(string key) => throw Down();

        public Task<byte[]?> GetAsync(string key, CancellationToken token = default) => throw Down();

        public void Refresh(string key) => throw Down();

        public Task RefreshAsync(string key, CancellationToken token = default) => throw Down();

        public void Remove(string key) => throw Down();

        public Task RemoveAsync(string key, CancellationToken token = default) => throw Down();

        public void Set(string key, byte[] value, DistributedCacheEntryOptions options) => throw Down();

        public Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options, CancellationToken token = default) => throw Down();
    }

    /// <summary>A cache that connects and then never answers, the way a wedged Redis behaves.</summary>
    private sealed class HangingDistributedCache : IDistributedCache
    {
        public byte[]? Get(string key) => throw new NotSupportedException();

        public async Task<byte[]?> GetAsync(string key, CancellationToken token = default)
        {
            await Task.Delay(Timeout.Infinite, token);
            return null;
        }

        public void Refresh(string key) => throw new NotSupportedException();

        public Task RefreshAsync(string key, CancellationToken token = default) => Task.Delay(Timeout.Infinite, token);

        public void Remove(string key) => throw new NotSupportedException();

        public Task RemoveAsync(string key, CancellationToken token = default) => Task.Delay(Timeout.Infinite, token);

        public void Set(string key, byte[] value, DistributedCacheEntryOptions options) => throw new NotSupportedException();

        public Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options, CancellationToken token = default) => Task.Delay(Timeout.Infinite, token);
    }
}
