using Majik.Server.Decks;
using Majik.Server.Matches;

namespace Majik.Server.Composition;

/// <summary>DI wiring for Deck CRUD. Registers MongoDB-backed repository,
/// validation, scoped service, and the production <see cref="IDeckLoader"/>.</summary>
public static class DeckRegistration
{
    public static IServiceCollection AddMajikDecks(this IServiceCollection services, IConfiguration config)
    {
        // DeckTextParser depends only on ICardRepository (SQLite, always available).
        services.AddScoped<DeckTextParser>();

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
