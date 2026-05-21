using Majik.Core.Api;
using Majik.Core.CardData;
using Majik.Core.CardData.Database;
using Majik.Core.Events;

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

        // Card data: singleton DbContext (uses default on-disk path) wrapped
        // in a caching decorator. Tests override these via ConfigureTestServices.
        services.AddSingleton<CardDbContext>();
        services.AddSingleton<ICardRepository>(sp =>
            new CachingCardRepository(new DbCardRepository(sp.GetRequiredService<CardDbContext>())));

        // ServerGameFactory takes ICardRepository so it can run the full binder
        // pipeline at game-start. Registered after ICardRepository so the DI
        // container can satisfy the constructor dependency.
        services.AddSingleton<ServerGameFactory>(sp =>
            new ServerGameFactory(
                sp.GetRequiredService<GameRegistry>(),
                sp.GetRequiredService<ICardRepository>()));

        return services;
    }
}
