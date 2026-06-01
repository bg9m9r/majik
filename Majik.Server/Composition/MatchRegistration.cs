using Majik.Core.Api;
using Majik.Server.Matches;
using Majik.Server.Matches.Persistence;
using Microsoft.Extensions.Logging;

namespace Majik.Server.Composition;

public static class MatchRegistration
{
    public static IServiceCollection AddMajikMatches(this IServiceCollection services, IConfiguration config)
    {
        if (!MongoRegistration.IsConfigured(config))
            return services;

        // PLAN 08 (body) — durable engine-state persistence, gated behind the
        // EnginePersistence:Enabled flag (DEFAULT OFF). With the flag off the
        // coordinator no-ops every call, so no durable writes happen and the
        // claim→rehydrate path is skipped — behaviour is exactly as today and the
        // deploy posture is unchanged until an operator flips the flag.
        AddEnginePersistence(services, config);

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

    /// <summary>
    /// PLAN 08 (body) — wire the durable-persistence stores + coordinator. The
    /// EnginePersistence:Enabled flag is bound from config (DEFAULT FALSE). When
    /// the flag is ON, the durable stores are Mongo-backed (the same database the
    /// matches use) and their indexes are ensured at startup; when OFF, in-memory
    /// stores are registered so the graph still composes but nothing is written
    /// (the coordinator no-ops on the flag anyway). The coordinator itself is
    /// always registered so MatchService can resolve it; its <c>Enabled</c>
    /// property is the single gate the request path checks.
    /// </summary>
    private static void AddEnginePersistence(IServiceCollection services, IConfiguration config)
    {
        services.Configure<EnginePersistenceOptions>(
            config.GetSection(EnginePersistenceOptions.SectionName));

        var enabled = config.GetValue<bool>(
            $"{EnginePersistenceOptions.SectionName}:Enabled");

        if (enabled)
        {
            services.AddSingleton<MongoEngineCommandLogStore>(sp =>
                new MongoEngineCommandLogStore(sp.GetRequiredService<MongoDB.Driver.IMongoDatabase>()));
            services.AddSingleton<MongoEngineCheckpointStore>(sp =>
                new MongoEngineCheckpointStore(sp.GetRequiredService<MongoDB.Driver.IMongoDatabase>()));
            services.AddSingleton<IEngineCommandLogStore>(sp =>
                sp.GetRequiredService<MongoEngineCommandLogStore>());
            services.AddSingleton<IEngineCheckpointStore>(sp =>
                sp.GetRequiredService<MongoEngineCheckpointStore>());
            services.AddHostedService<EnginePersistenceIndexInitializer>();
        }
        else
        {
            // Flag off — in-memory stores so the graph composes; never written
            // (the coordinator gates on Enabled before any store call).
            services.AddSingleton<IEngineCommandLogStore, InMemoryEngineCommandLogStore>();
            services.AddSingleton<IEngineCheckpointStore, InMemoryEngineCheckpointStore>();
        }

        services.AddSingleton<EnginePersistenceCoordinator>(sp =>
            new EnginePersistenceCoordinator(
                sp.GetRequiredService<IEngineCommandLogStore>(),
                sp.GetRequiredService<IEngineCheckpointStore>(),
                sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<EnginePersistenceOptions>>(),
                clock: null,
                logger: sp.GetService<ILogger<EnginePersistenceCoordinator>>()));
    }
}
