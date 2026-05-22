namespace Majik.Server.Composition;

/// <summary>
/// SignalR hub registration. Adds the Redis backplane when
/// <c>Redis:ConnectionString</c> is set — this lets hub group sends
/// (e.g. <c>Clients.Group("match:{id}")</c>) fan out across multiple
/// majik-api replicas. With it unset, behavior is identical to the
/// stock in-memory backplane (single-replica mode).
///
/// Prerequisite for scaling <c>numInstances</c> on Render past 1:
/// without the backplane, a connection on replica A would never see
/// events emitted by code running on replica B.
/// </summary>
public static class SignalRRegistration
{
    /// <summary>Configuration key the connection string is read from.</summary>
    public const string ConnectionStringKey = "Redis:ConnectionString";

    /// <summary>Channel prefix Redis pub/sub uses for SignalR messages.
    /// Keeps the namespace clear if/when other systems share the same Redis.</summary>
    public const string ChannelPrefix = "majik:signalr";

    public static IServiceCollection AddMajikSignalR(this IServiceCollection services, IConfiguration configuration)
    {
        var connStr = configuration[ConnectionStringKey];
        var signalR = services.AddSignalR();

        if (!string.IsNullOrWhiteSpace(connStr))
        {
            signalR.AddStackExchangeRedis(connStr, opts =>
            {
                opts.Configuration.ChannelPrefix = StackExchange.Redis.RedisChannel.Literal(ChannelPrefix);
            });
        }

        return services;
    }
}
