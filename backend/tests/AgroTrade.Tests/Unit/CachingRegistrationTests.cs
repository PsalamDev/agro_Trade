using AgroTrade.Application.Common;
using AgroTrade.Infrastructure.Caching;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AgroTrade.Tests.Unit;

/// <summary>
/// Which provider <see cref="CachingServiceCollectionExtensions.AddCaching"/> wires up.
/// </summary>
/// <remarks>
/// Redis is opt-in. With no connection string the app must still start and cache, over the
/// in-process memory cache — otherwise every test run and a fresh developer machine would need a
/// Redis server before anything worked.
/// </remarks>
public sealed class CachingRegistrationTests
{
    [Fact]
    public void WithNoRedisConnectionString_UsesTheInProcessMemoryCache()
    {
        var services = new ServiceCollection();

        services.AddCaching(Config(redis: null));

        Assert.Contains(services, d =>
            d.ServiceType == typeof(IDistributedCache) && d.ImplementationType == typeof(MemoryDistributedCache));
        Assert.Contains(services, d => d.ServiceType == typeof(ICache));
    }

    [Fact]
    public void WithARedisConnectionString_UsesTheRedisProvider()
    {
        var services = new ServiceCollection();

        services.AddCaching(Config(redis: "localhost:6379"));

        // The registration names an internal Redis-backed type (RedisCacheImpl) that cannot be
        // referenced directly, so its namespace is what pins it to the Redis package.
        var descriptor = Assert.Single(services, d => d.ServiceType == typeof(IDistributedCache));
        Assert.NotNull(descriptor.ImplementationType);
        Assert.Equal("Microsoft.Extensions.Caching.StackExchangeRedis", descriptor.ImplementationType!.Namespace);
        Assert.Contains(services, d => d.ServiceType == typeof(ICache));
    }

    private static IConfiguration Config(string? redis) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Redis"] = redis,
            })
            .Build();
}
