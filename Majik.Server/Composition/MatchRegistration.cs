using Majik.Core.Api;
using Majik.Server.Matches;
using Microsoft.Extensions.Logging;

namespace Majik.Server.Composition;

public static class MatchRegistration
{
    public static IServiceCollection AddMajikMatches(this IServiceCollection services, IConfiguration config)
    {
        if (!MongoRegistration.IsConfigured(config))
            return services;

        // Wire the per-match EventBus error sink to a structured logger.
        // Without this, GameFacade leaves EventBus.OnHandlerError null and
        // every engine handler exception (trigger / SBA / wire-bridge) is
        // swallowed silently — a thrown handler would freeze a match with
        // no diagnostic. Set once at composition; the static hook is
        // process-wide (matches GameFacade's per-game bus construction).
        services.AddSingleton<GameFacadeErrorSinkInitializer>();
        services.AddHostedService(sp => sp.GetRequiredService<GameFacadeErrorSinkInitializer>());

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
        // Slice 5a — per-(matchId, sub) auto-pass prefs. Process-local;
        // a restart resets every entry to AutoPassPrefs.Default (which is
        // safe — the engine's auto-pass gate is narrow + opt-out). The
        // store is read on every priority window inside the engine's
        // PriorityLoop, so singleton lifetime is required.
        services.AddSingleton<AutoPassPrefsStore>();
        // Engine → SignalR bridge. Singleton so the same instance holds
        // subscriptions across the request lifetime of MatchService
        // (scoped) — every match attaches once at facade-create and
        // detaches at terminal state.
        //
        // The clock-handoff callback keeps the server clock holder aligned
        // with the engine's active player (CR 117 / 103.7). It opens a fresh
        // DI scope to resolve the scoped MatchService and calls
        // OnPriorityPassedAsync — same pattern as MatchTimeoutScheduler
        // below. Without it the holder set in PlayDrawAsync freezes forever
        // and the wrong player's clock burns.
        // Engine-wedge watchdog (W3/W7). Per-match no-progress timer the bridge
        // arms on Attach, bumps on every engine event (and bot-thinking), and
        // cancels on Detach. NoProgressSeconds is config-bound (default 90); a
        // hang fires onEngineErrored(Hang). Singleton so the same per-match timer
        // dict is shared across requests (matches the other schedulers above).
        var watchdogNoProgressSeconds = config.GetValue<int>("Watchdog:NoProgressSeconds", 90);
        services.AddSingleton(sp => new MatchEngineWatchdog(
            sp.GetRequiredService<ILogger<MatchEngineWatchdog>>(),
            watchdogNoProgressSeconds));
        services.AddSingleton<MatchFacadeBridge>(sp =>
            new MatchFacadeBridge(
                sp.GetRequiredService<IMatchHubPublisher>(),
                sp.GetRequiredService<ILogger<MatchFacadeBridge>>(),
                sp.GetService<MatchReplayBuffer>(),
                onActivePlayerChanged: async (matchId, newHolderSub, expectedPrevHolderSub, ct) =>
                {
                    using var scope = sp.CreateScope();
                    var svc = scope.ServiceProvider.GetRequiredService<MatchService>();
                    // expectedPrevHolderSub threads the prior holder through as a
                    // compare-and-swap guard so out-of-order handoffs can't
                    // double-bill the same seat (C1).
                    await svc.OnPriorityPassedAsync(matchId, newHolderSub, ct, expectedPrevHolderSub);
                },
                watchdog: sp.GetRequiredService<MatchEngineWatchdog>(),
                // W7 — route engine fault/hang to the scoped MatchService via a
                // fresh DI scope (the singleton bridge can't hold the scoped
                // service). Same pattern as onActivePlayerChanged / the timeout
                // scheduler below. OnEngineErrorAsync CAS-transitions Playing →
                // Errored exactly once; double-termination races no-op.
                onEngineErrored: async (matchId, reason, fault, ct) =>
                {
                    using var scope = sp.CreateScope();
                    var svc = scope.ServiceProvider.GetRequiredService<MatchService>();
                    await svc.OnEngineErrorAsync(matchId, reason, fault, ct);
                }));
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
            },
            sp.GetService<ILogger<MatchTimeoutScheduler>>()));
        services.AddScoped<MatchService>();
        services.AddHostedService<MatchIndexInitializer>();
        services.AddHostedService<MatchCleanupService>();
        return services;
    }
}
