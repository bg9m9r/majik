using Majik.Server.Decks;
using Majik.Server.Matches;

namespace Majik.Server.Composition;

/// <summary>DI wiring for sub-project #3 Deck CRUD. Replaces the
/// StubDeckLoader registration from MatchRegistration with RealDeckLoader.</summary>
public static class DeckRegistration
{
    public static IServiceCollection AddMajikDecks(this IServiceCollection services, IConfiguration config)
    {
        if (!MongoRegistration.IsConfigured(config))
            return services;

        services.AddSingleton<DeckRepository>(sp =>
            new DeckRepository(sp.GetRequiredService<MongoDB.Driver.IMongoDatabase>()));
        services.AddSingleton<DeckValidationService>();
        services.AddScoped<DeckService>();
        services.AddScoped<IDeckLoader, RealDeckLoader>();
        services.AddHostedService<DeckIndexInitializer>();
        return services;
    }
}
