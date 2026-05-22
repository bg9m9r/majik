using Majik.Core.CardData;
using Majik.Core.CardData.Database;

namespace Majik.CardsServer;

/// <summary>
/// Entry point for the majik-cards private service. Declared as an explicit
/// class with a static <c>Main</c> (not top-level statements) so this
/// assembly does not synthesize a global <c>Program</c> type — Majik.Server
/// already exposes one, and Microsoft.AspNetCore.Mvc.Testing makes both
/// visible to the test project; a shared name would cause CS0433.
/// </summary>
public sealed class CardsServerEntryPoint
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        // One singleton repository — DbCardRepository opens a fresh CardDbContext per
        // public method (EF DbContext is NOT thread-safe). Wrapping in CachingCardRepository
        // is left to the caller side (Majik.Server) so the cache lives in the same
        // process as the consumers; caching here would still require a network hop.
        builder.Services.AddSingleton<ICardRepository>(_ =>
            new DbCardRepository(() => new CardDbContext()));

        builder.Services.AddHostedService<CardDbIndexBootstrapper>();
        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddOpenApi();

        var app = builder.Build();

        // Log token mode once at boot so misconfigured deploys are visible in logs.
        var tokenConfigured = !string.IsNullOrEmpty(builder.Configuration["Cards:InternalToken"]);
        app.Logger.LogInformation(
            "Majik.CardsServer starting. InternalToken {Mode}",
            tokenConfigured ? "REQUIRED" : "DISABLED (dev mode)");

        app.MapOpenApi();
        app.UseMiddleware<InternalTokenMiddleware>();

        app.MapGet("/healthz", () => Results.Ok(new { status = "ok" }))
           .AllowAnonymous();

        app.MapCardsInternalEndpoints();

        app.Run();
    }
}
