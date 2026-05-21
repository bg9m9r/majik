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
/// </summary>
public static class MajikEngineRegistration
{
    public static IServiceCollection AddMajikEngine(this IServiceCollection services)
    {
        services.AddSingleton<GameRegistry>();

        // Card data: factory-per-call DbContext (EF Core DbContext is NOT
        // thread-safe — a shared singleton would race under concurrent
        // requests). DbCardRepository creates a fresh CardDbContext via the
        // factory delegate inside each public method. Wrapped in a caching
        // decorator that itself remains singleton-safe.
        // Tests override these via ConfigureTestServices.
        services.AddSingleton<ICardRepository>(_ =>
            new CachingCardRepository(new DbCardRepository(() => new CardDbContext())));

        // ServerGameFactory takes ICardRepository so it can run the full binder
        // pipeline at game-start. Registered after ICardRepository so the DI
        // container can satisfy the constructor dependency.
        services.AddSingleton<ServerGameFactory>(sp =>
            new ServerGameFactory(
                sp.GetRequiredService<GameRegistry>(),
                sp.GetRequiredService<ICardRepository>()));

        // One-time index bootstrap at startup. Idempotent.
        services.AddHostedService<CardDbIndexBootstrapper>();

        // Validate bot decks against ICardRepository at startup. Logs only;
        // never throws — keeps the server bootable even if a single bot
        // archetype references an unimplemented card.
        services.AddHostedService<Majik.Bot.Decks.BotDeckValidator>();

        return services;
    }
}
