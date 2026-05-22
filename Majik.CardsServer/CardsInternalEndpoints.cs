using Majik.Core.CardData;
using Majik.Core.CardData.Contracts;
using Microsoft.AspNetCore.Mvc;

namespace Majik.CardsServer;

/// <summary>
/// Internal HTTP surface for the cards service. Mirrors
/// <see cref="ICardRepository"/> 1-to-1 over JSON. Consumed by
/// <c>HttpCardRepository</c> in Majik.Server.
/// </summary>
public static class CardsInternalEndpoints
{
    private const int MaxByNameCount = 200;
    private const int MaxSearchLimit = 200;

    public static IEndpointRouteBuilder MapCardsInternalEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/internal/cards").WithTags("CardsInternal");

        group.MapGet("/by-name", (
            [FromQuery] string? name,
            [FromServices] ICardRepository repo) =>
        {
            if (string.IsNullOrWhiteSpace(name))
                return Results.BadRequest(new { error = "missing-name" });
            var entity = repo.GetByName(name);
            return entity is null
                ? Results.NotFound()
                : Results.Ok(CardEntityDto.From(entity));
        });

        group.MapPost("/by-names", (
            [FromBody] CardsByNamesRequest body,
            [FromServices] ICardRepository repo) =>
        {
            if (body?.Names == null || body.Names.Count == 0)
                return Results.Ok(Array.Empty<CardEntityDto>());
            if (body.Names.Count > MaxByNameCount)
                return Results.BadRequest(new { error = "too-many-names", max = MaxByNameCount });
            var rows = repo.GetByNames(body.Names);
            var dtos = new List<CardEntityDto>(rows.Count);
            foreach (var c in rows) dtos.Add(CardEntityDto.From(c));
            return Results.Ok((IReadOnlyList<CardEntityDto>)dtos);
        });

        group.MapGet("/search", (
            [FromQuery] string? q,
            [FromQuery] bool? implementedOnly,
            [FromQuery] int? limit,
            [FromQuery(Name = "colors")] string[]? colors,
            [FromQuery(Name = "types")] string[]? types,
            [FromQuery(Name = "cmc")] int[]? cmc,
            [FromServices] ICardRepository repo) =>
        {
            var effectiveLimit = limit ?? 50;
            if (effectiveLimit < 1 || effectiveLimit > MaxSearchLimit)
                return Results.BadRequest(new { error = "invalid-limit", range = "1..200" });

            var rows = repo.Search(
                q,
                implementedOnly ?? false,
                effectiveLimit,
                colors: colors,
                types: types,
                cmcBuckets: cmc);
            var dtos = new List<CardEntityDto>(rows.Count);
            foreach (var c in rows) dtos.Add(CardEntityDto.From(c));
            return Results.Ok((IReadOnlyList<CardEntityDto>)dtos);
        });

        group.MapGet("/is-implemented", (
            [FromQuery] string? name,
            [FromServices] ICardRepository repo) =>
        {
            if (string.IsNullOrWhiteSpace(name))
                return Results.BadRequest(new { error = "missing-name" });
            return Results.Ok(new IsImplementedResponse(repo.IsImplemented(name)));
        });

        group.MapPost("/set-implemented", (
            [FromBody] SetImplementedRequest body,
            [FromServices] ICardRepository repo) =>
        {
            if (body == null || string.IsNullOrWhiteSpace(body.Name))
                return Results.BadRequest(new { error = "missing-name" });
            try
            {
                repo.SetImplemented(body.Name, body.Value);
                return Results.NoContent();
            }
            catch (ArgumentException)
            {
                return Results.NotFound();
            }
        });

        return routes;
    }
}
