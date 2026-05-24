using Majik.Server.Matches;
using Microsoft.Extensions.Logging;

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
        // Per-process replay buffer. In-memory only — no persistence in
        // MVP; finished-match buffers are retained under an LRU cap
        // (MatchReplayBuffer.MaxRetainedMatches) so a server restart
        // wipes history. Promote to Mongo if real games push past the
        // per-match entry cap on a regular basis.
        services.AddSingleton<MatchReplayBuffer>();
        // Engine → SignalR bridge. Singleton so the same instance holds
        // subscriptions across the request lifetime of MatchService
        // (scoped) — every match attaches once at facade-create and
        // detaches at terminal state.
        services.AddSingleton<MatchFacadeBridge>();
        // Bot-match scheduler: drives the bot's roll + play/draw follow-up
        // with brief delays so the SignalR-fed UI lingers on Rolling long
        // enough for the user to see the dice. Singleton so the same set
        // of scheduled callbacks is shared across requests (matches the
        // pattern used by MatchTimeoutScheduler).
        services.AddSingleton<IBotMatchScheduler>(sp => new BotMatchScheduler(
            sp,
            sp.GetService<ILogger<BotMatchScheduler>>()));
        services.AddSingleton(sp => new MatchTimeoutScheduler(
            async (matchId, holder, ct) =>
            {
                using var scope = sp.CreateScope();
                var svc = scope.ServiceProvider.GetRequiredService<MatchService>();
                await svc.OnTimeoutAsync(matchId, holder, ct);
            }));
        services.AddSingleton<IMatchOwnership, MatchOwnership>();
        services.AddHostedService<MatchOwnershipHeartbeat>();
        // Forwarder doubles as a hosted service so its per-process reply
        // channel gets subscribed at startup. The same singleton instance
        // satisfies both registrations.
        services.AddSingleton<MatchCommandForwarder>();
        services.AddSingleton<IMatchCommandForwarder>(sp => sp.GetRequiredService<MatchCommandForwarder>());
        services.AddHostedService(sp => sp.GetRequiredService<MatchCommandForwarder>());
        services.AddScoped<MatchService>();
        services.AddHostedService<MatchIndexInitializer>();
        services.AddHostedService<MatchCleanupService>();
        return services;
    }
}
