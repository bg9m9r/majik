using Majik.Server.Profiles;
using MongoDB.Driver;

namespace Majik.Server.Composition;

/// <summary>
/// Wires MongoDB for new entities (profiles, decks, matches).
///
/// `Mongo:ConnectionString` empty = disabled — `IMongoDatabase` registered
/// as null sentinel and the endpoints short-circuit with 503. Matches
/// the empty-auth pattern in <see cref="AuthRegistration"/>.
/// </summary>
public static class MongoRegistration
{
    public const string ConnectionStringKey = "Mongo:ConnectionString";
    public const string DatabaseKey = "Mongo:Database";
    public const string DefaultDatabaseName = "majik-dev";

    public static IServiceCollection AddMajikMongo(
        this IServiceCollection services,
        IConfiguration config)
    {
        var connectionString = config[ConnectionStringKey];
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            // Sentinel: register null so endpoints can detect & 503.
            // AddSingleton<T?> won't compile (class constraint); use ServiceDescriptor directly.
            services.Add(ServiceDescriptor.Singleton(typeof(IMongoDatabase), (IMongoDatabase?)null!));
            services.Add(ServiceDescriptor.Singleton(typeof(UserProfileRepository), (UserProfileRepository?)null!));
            return services;
        }

        var databaseName = config[DatabaseKey];
        if (string.IsNullOrWhiteSpace(databaseName))
        {
            databaseName = DefaultDatabaseName;
        }

        services.AddSingleton<IMongoClient>(_ => new MongoClient(connectionString));
        services.AddSingleton<IMongoDatabase>(sp =>
            sp.GetRequiredService<IMongoClient>().GetDatabase(databaseName));
        services.AddSingleton<UserProfileRepository>(sp =>
            new UserProfileRepository(sp.GetRequiredService<IMongoDatabase>()));
        services.AddHostedService<ProfileIndexInitializer>();
        return services;
    }

    public static bool IsConfigured(IConfiguration config) =>
        !string.IsNullOrWhiteSpace(config[ConnectionStringKey]);
}
