using Majik.Bot.Diagnostics;
using Majik.Core.Api;
using Majik.Core.CardData;
using Majik.Core.Events;

namespace Majik.Server.Composition;

/// <summary>
/// Registers the Majik engine components in the ASP.NET Core DI
/// container. The engine itself stays free of ASP.NET dependencies —
/// this file is the only place where the two worlds meet.
///
/// Scope decisions:
/// - GameRegistry singleton: one process, many games, in-memory only.
/// - EventBus is per-game (a facade owns its own bus), so no global bus
///   is registered here.
///
/// Cards backing store: <see cref="EmbeddedCardRepository"/> against the
/// gzipped Modern-legal JSON resource bundled into <c>Majik.Core</c>.
/// In-process, no SQLite, no internal HTTP hop. The repository handles
/// its own thread-safe lazy load + per-name caching, so the previous
/// CachingCardRepository decorator was deleted along with the HTTP and
/// SQLite repositories.
/// </summary>
public static class MajikEngineRegistration
{
    public static IServiceCollection AddMajikEngine(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<GameRegistry>();

        services.AddSingleton<ICardRepository>(_ => new EmbeddedCardRepository());

        // Optional decision-logging sink for the bot. Default off in prod
        // (zero overhead — BotConfig falls back to NullBotDecisionSink).
        // Enable in dev via appsettings: "Bot:DecisionLogging:Enabled": true.
        var decisionLoggingEnabled = configuration.GetValue<bool>("Bot:DecisionLogging:Enabled");
        if (decisionLoggingEnabled)
        {
            services.AddSingleton<IBotDecisionSink>(sp =>
            {
                var loggerFactory = sp.GetRequiredService<Microsoft.Extensions.Logging.ILoggerFactory>();
                return new LoggerBotDecisionSink(loggerFactory.CreateLogger("Majik.Bot.Decision"));
            });
        }

        // ServerGameFactory takes ICardRepository so it can run the full binder
        // pipeline at game-start. Registered after ICardRepository so the DI
        // container can satisfy the constructor dependency.
        services.AddSingleton<ServerGameFactory>(sp =>
            new ServerGameFactory(
                sp.GetRequiredService<GameRegistry>(),
                sp.GetRequiredService<ICardRepository>(),
                botDecisionSink: sp.GetService<IBotDecisionSink>(),
                botDecisionLoggingEnabled: decisionLoggingEnabled));

        // Validate bot decks against ICardRepository at startup. Logs only;
        // never throws — keeps the server bootable even if a single bot
        // archetype references an unimplemented card.
        services.AddHostedService<Majik.Bot.Decks.BotDeckValidator>();

        return services;
    }
}
