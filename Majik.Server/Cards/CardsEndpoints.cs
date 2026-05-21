using Majik.Core.CardData;
using Majik.Core.CardData.Database;
using Majik.Server.Composition;
using Microsoft.AspNetCore.Mvc;

namespace Majik.Server.Cards;

/// <summary>Maps <c>/cards</c> endpoints for searching the card database.
/// Auth: <see cref="AuthRegistration.AsPlayerPolicy"/>.</summary>
public static class CardsEndpoints
{
    private const int DefaultLimit = 50;
    private const int MaxLimit = 200;

    public static IEndpointRouteBuilder MapCardsEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/cards")
            .RequireAuthorization(AuthRegistration.AsPlayerPolicy)
            .WithTags("Cards");

        group.MapGet("/", Search)
             .WithName("SearchCards")
             .Produces<IReadOnlyList<CardDto>>(StatusCodes.Status200OK)
             .Produces<CardsError>(StatusCodes.Status400BadRequest)
             .Produces(StatusCodes.Status401Unauthorized);

        group.MapPost("/by-name", GetByNames)
             .WithName("GetCardsByName")
             .Produces<IReadOnlyList<CardDto>>(StatusCodes.Status200OK)
             .Produces<CardsError>(StatusCodes.Status400BadRequest)
             .Produces(StatusCodes.Status401Unauthorized);

        return routes;
    }

    private const int MaxByNameCount = 200;

    private static IResult GetByNames(
        [FromBody] CardsByNameRequest body,
        [FromServices] ICardRepository repo,
        [FromServices] ILoggerFactory loggerFactory)
    {
        var logger = loggerFactory.CreateLogger("Majik.Cards");
        if (body?.Names == null || body.Names.Count == 0)
            return Results.Ok(Array.Empty<CardDto>());
        if (body.Names.Count > MaxByNameCount)
            return Results.BadRequest(new CardsError("too-many-names", $"max {MaxByNameCount}"));
        try
        {
            var cards = repo.GetByNames(body.Names);
            var dtos = new List<CardDto>(cards.Count);
            foreach (var c in cards.OrderBy(c => c.Name))
            {
                try { dtos.Add(ToDto(c)); }
                catch (Exception ex) { logger.LogError(ex, "ToDto failed for '{Name}'", c.Name); }
            }
            return Results.Ok(dtos);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "GetByNames failed. count={Count}", body.Names.Count);
            return Results.Json(new CardsError("by-name-failed", ex.Message), statusCode: 500);
        }
    }

    private static IResult Search(
        [FromQuery] string? q,
        [FromQuery] bool? implementedOnly,
        [FromQuery] int? limit,
        [FromQuery(Name = "colors")] string[]? colors,
        [FromQuery(Name = "types")] string[]? types,
        [FromQuery(Name = "cmc")] int[]? cmc,
        [FromServices] ICardRepository repo,
        [FromServices] ILoggerFactory loggerFactory)
    {
        var logger = loggerFactory.CreateLogger("Majik.Cards");
        var effectiveLimit = limit ?? DefaultLimit;

        if (effectiveLimit < 1 || effectiveLimit > MaxLimit)
            return Results.BadRequest(new CardsError("invalid-limit", "1..200"));

        IReadOnlyList<CardEntity> cards;
        try
        {
            cards = repo.Search(
                q, implementedOnly ?? false, effectiveLimit,
                colors: colors,
                types: types,
                cmcBuckets: cmc);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Card search failed. q={Q} impl={Impl} limit={Limit} colors=[{Colors}] types=[{Types}] cmc=[{Cmc}]",
                q, implementedOnly, effectiveLimit,
                colors == null ? "" : string.Join(",", colors),
                types == null ? "" : string.Join(",", types),
                cmc == null ? "" : string.Join(",", cmc));
            return Results.Json(new CardsError("search-failed", ex.Message), statusCode: 500);
        }

        var dtos = new List<CardDto>(cards.Count);
        foreach (var c in cards.OrderBy(c => c.Name))
        {
            try
            {
                dtos.Add(ToDto(c));
            }
            catch (Exception ex)
            {
                logger.LogError(ex,
                    "ToDto failed for card '{Name}' (id={Id}). Skipping.",
                    c.Name, c.Id);
            }
        }

        return Results.Ok(dtos);
    }

    private static CardDto ToDto(CardEntity c)
    {
        // TypeLine format: "Creature — Human Wizard" (em-dash separator)
        // Split on " — " to get supertypes+types portion, then split on spaces.
        // Null TypeLine should be impossible (column is NOT NULL + default "") but be defensive
        // since legacy DBs have shown surprising values.
        var typeLine = c.TypeLine ?? "";
        var typePart = typeLine.Split(" — ")[0];
        var types = typePart
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .ToList();

        int? power = null;
        int? toughness = null;

        if (c.Power != null && int.TryParse(c.Power, out var p))
            power = p;

        if (c.Toughness != null && int.TryParse(c.Toughness, out var t))
            toughness = t;

        IReadOnlyList<string> colors;
        try
        {
            colors = System.Text.Json.JsonSerializer.Deserialize<List<string>>(c.Colors) ?? new List<string>();
        }
        catch
        {
            colors = new List<string>();
        }

        return new CardDto(
            Name: c.Name,
            ManaCost: c.ManaCost ?? "",
            Types: types,
            Power: power,
            Toughness: toughness,
            IsImplemented: c.IsImplemented,
            Cmc: c.Cmc,
            Colors: colors,
            OracleText: c.OracleText);
    }
}

/// <summary>Request body for <c>POST /cards/by-name</c>.</summary>
public sealed record CardsByNameRequest(IReadOnlyList<string> Names);
