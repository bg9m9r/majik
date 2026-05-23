using Majik.Bot.Diagnostics;
using Majik.Core.Api;
using Majik.Core.CardData;
using Majik.Core.Events;
using Majik.Server.Cards;

namespace Majik.Server.Composition;

/// <summary>
/// Registers the Majik engine components in the ASP.NET Core DI
/// container. The engine itself stays free of ASP.NET dependencies —
/// this file is the only place where the two worlds meet.
///
/// Scope decisions (Phase 3 v1):
/// - GameRegistry singleton: one process, many games, in-memory only.
/// - EventBus is per-game (a facade owns its own bus), so no global bus
///   is registered here.
///
/// Cards backing store: <see cref="HttpCardRepository"/> against the
/// majik-cards private service. <c>Cards:BaseUrl</c> is required —
/// startup throws if unset. Tests override <see cref="ICardRepository"/>
/// via <c>ConfigureTestServices</c> to point at an in-memory SQLite.
/// </summary>
public static class MajikEngineRegistration
{
    public static IServiceCollection AddMajikEngine(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<GameRegistry>();

        var cardsBaseUrl = configuration["Cards:BaseUrl"];
        var cardsToken = configuration["Cards:InternalToken"];

        if (string.IsNullOrWhiteSpace(cardsBaseUrl))
        {
            throw new InvalidOperationException(
                "Cards:BaseUrl is required. Point Majik.Server at the majik-cards " +
                "private service (e.g. http://majik-cards:10000 in prod, " +
                "http://localhost:5180 in local dev).");
        }

        services.AddHttpClient<HttpCardRepository>(client =>
        {
            client.BaseAddress = new Uri(cardsBaseUrl);
            client.Timeout = TimeSpan.FromSeconds(15);
            if (!string.IsNullOrEmpty(cardsToken))
            {
                client.DefaultRequestHeaders.Add(InternalTokenHeader.Name, cardsToken);
            }
        });

        services.AddSingleton<ICardRepository>(sp =>
            new CachingCardRepository(sp.GetRequiredService<HttpCardRepository>()));

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
        // botDecisionLoggingEnabled propagates the same flag MatchService
        // reads when deciding whether to compose a per-match
        // SignalrBotDecisionSink — keeps the wire channel + stdout logger
        // in lockstep instead of letting them drift apart.
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

/// <summary>Header name shared between Majik.Server and Majik.CardsServer.</summary>
internal static class InternalTokenHeader
{
    public const string Name = "X-Internal-Token";
}
