using StackExchange.Redis;

namespace Majik.Server.Composition;

/// <summary>
/// Registers a shared <see cref="IConnectionMultiplexer"/> for non-SignalR
/// Redis usage (ownership claims, cross-replica command forwarding). The
/// SignalR backplane manages its own multiplexer internally — keeping them
/// separate avoids accidentally coupling backplane lifecycle to app code.
/// Only registers when <see cref="SignalRRegistration.ConnectionStringKey"/>
/// is set; consumers must accept <c>IConnectionMultiplexer?</c> so they
/// behave sensibly in local-dev / test mode without Redis.
/// </summary>
public static class RedisRegistration
{
    public static IServiceCollection AddMajikRedis(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<IInstanceIdProvider, InstanceIdProvider>();

        var connStr = configuration[SignalRRegistration.ConnectionStringKey];
        if (string.IsNullOrWhiteSpace(connStr))
        {
            return services;
        }

        services.AddSingleton<IConnectionMultiplexer>(_ =>
            ConnectionMultiplexer.Connect(connStr));
        return services;
    }
}
