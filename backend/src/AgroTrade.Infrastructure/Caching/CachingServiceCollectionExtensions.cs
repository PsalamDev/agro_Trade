using AgroTrade.Application.Common;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;

namespace AgroTrade.Infrastructure.Caching;

public static class CachingServiceCollectionExtensions
{
    /// <summary>
    /// Registers the cache-aside store. Redis backs it when a connection string is configured
    /// (<c>ConnectionStrings:Redis</c>); otherwise an in-process distributed memory cache does.
    /// The memory default keeps the app buildable and runnable with no Redis server present, which
    /// is also how the integration suite runs.
    /// </summary>
    public static IServiceCollection AddCaching(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<CacheOptions>()
            .Bind(configuration.GetSection(CacheOptions.SectionName));

        var options = configuration.GetSection(CacheOptions.SectionName).Get<CacheOptions>() ?? new CacheOptions();
        var redis = configuration.GetConnectionString("Redis");

        if (!string.IsNullOrWhiteSpace(redis))
        {
            // Parsed up front so a malformed connection string fails at startup rather than on the
            // first request. AbortOnConnectFail is off so a Redis blip does not stop the app serving;
            // together with the per-call deadline in DistributedCacheService, an unreachable Redis
            // degrades to a fast miss instead of a stalled request.
            var redisOptions = ConfigurationOptions.Parse(redis);
            redisOptions.AbortOnConnectFail = false;
            redisOptions.ConnectTimeout = options.OperationTimeoutMilliseconds;
            redisOptions.SyncTimeout = options.OperationTimeoutMilliseconds;
            redisOptions.ConnectRetry = 1;

            services.AddStackExchangeRedisCache(o =>
            {
                o.ConfigurationOptions = redisOptions;
                o.InstanceName = options.KeyPrefix;
            });
        }
        else
        {
            services.AddDistributedMemoryCache();
        }

        services.AddSingleton<ICache, DistributedCacheService>();
        return services;
    }
}
