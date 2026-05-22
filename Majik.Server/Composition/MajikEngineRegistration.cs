using Majik.Bot.Diagnostics;
using Majik.Core.Api;
using Majik.Core.CardData;
using Majik.Core.CardData.Database;
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
/// Cards backing store:
/// - If <c>Cards:BaseUrl</c> is configured (e.g. <c>http://majik-cards:10000</c>),
///   the repository is <see cref="HttpCardRepository"/> talking to the
///   majik-cards private service. This is the production path once the
///   cards extraction is fully rolled out and the SQLite disk is detached.
/// - Otherwise, falls back to the in-process <see cref="DbCardRepository"/>
///   against the local SQLite file. Lets local dev and existing prod boots
///   work unchanged until <c>Cards:BaseUrl</c> is set.
/// Both modes wear the same <see cref="CachingCardRepository"/> decorator
/// so callers see identical lookup semantics either way.
/// </summary>
public static class MajikEngineRegistration
{
    public static IServiceCollection AddMajikEngine(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<GameRegistry>();

        var cardsBaseUrl = configuration["Cards:BaseUrl"];
        var cardsToken = configuration["Cards:InternalToken"];

        if (!string.IsNullOrWhiteSpace(cardsBaseUrl))
        {
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
        }
        else
        {
            // Local SQLite path — kept for backwards compatibility until the
            // render.yaml change cuts the disk off this service. Factory-per-call
            // DbContext (EF Core DbContext is NOT thread-safe — a shared singleton
            // would race under concurrent requests). DbCardRepository creates a
            // fresh CardDbContext via the factory delegate inside each public
            // method. Tests override this via ConfigureTestServices.
            services.AddSingleton<ICardRepository>(_ =>
                new CachingCardRepository(new DbCardRepository(() => new CardDbContext())));

            // Index bootstrap only matters when this process owns the SQLite file.
            services.AddHostedService<CardDbIndexBootstrapper>();
        }

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
                botDecisionSink: sp.GetService<IBotDecisionSink>()));

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
