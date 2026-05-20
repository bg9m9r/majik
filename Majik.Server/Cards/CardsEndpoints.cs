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

        return routes;
    }

    private static IResult Search(
        [FromQuery] string? q,
        [FromQuery] bool? implementedOnly,
        [FromQuery] int? limit,
        [FromServices] ICardRepository repo)
    {
        var effectiveLimit = limit ?? DefaultLimit;

        if (effectiveLimit < 1 || effectiveLimit > MaxLimit)
            return Results.BadRequest(new CardsError("invalid-limit", "1..200"));

        var cards = repo.Search(q, implementedOnly ?? false, effectiveLimit);

        var dtos = cards
            .OrderBy(c => c.Name)
            .Select(ToDto)
            .ToList();

        return Results.Ok(dtos);
    }

    private static CardDto ToDto(CardEntity c)
    {
        // TypeLine format: "Creature — Human Wizard" (em-dash separator)
        // Split on " — " to get supertypes+types portion, then split on spaces.
        var typePart = c.TypeLine.Split(" — ")[0];
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
