using Majik.Server.Matches;

namespace Majik.Server.Composition;

public static class MatchRegistration
{
    public static IServiceCollection AddMajikMatches(this IServiceCollection services, IConfiguration config)
    {
        if (!MongoRegistration.IsConfigured(config))
            return services;

        services.AddSingleton<MatchRepository>(sp =>
            new MatchRepository(sp.GetRequiredService<MongoDB.Driver.IMongoDatabase>()));
        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<IRandomSource, SystemRandomSource>();
        services.AddSingleton<DiceRoller>();
        services.AddSingleton<IMatchHubPublisher, MatchHubPublisher>();
        services.AddSingleton(sp => new MatchTimeoutScheduler(
            async (matchId, holder, ct) =>
            {
                using var scope = sp.CreateScope();
                var svc = scope.ServiceProvider.GetRequiredService<MatchService>();
                await svc.OnTimeoutAsync(matchId, holder, ct);
            }));
        services.AddSingleton<IMatchOwnership, MatchOwnership>();
        services.AddHostedService<MatchOwnershipHeartbeat>();
        services.AddScoped<MatchService>();
        services.AddHostedService<MatchIndexInitializer>();
        services.AddHostedService<MatchCleanupService>();
        return services;
    }
}
