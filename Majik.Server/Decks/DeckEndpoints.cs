using System.Security.Claims;
using Majik.Server.Composition;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Majik.Server.Decks;

public static class DeckEndpoints
{
    public const string RateLimitPolicy = "authed-60-per-min";

    public static IEndpointRouteBuilder MapDeckEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/decks")
            .RequireAuthorization(AuthRegistration.AsPlayerPolicy)
            .RequireRateLimiting(RateLimitPolicy)
            .WithTags("Decks");

        group.MapGet("/", List).WithName("ListDecks")
             .Produces<IReadOnlyList<DeckDto>>(StatusCodes.Status200OK);
        group.MapGet("/{id:guid}", Get).WithName("GetDeck")
             .Produces<DeckDto>(StatusCodes.Status200OK)
             .Produces<DeckError>(StatusCodes.Status404NotFound);
        group.MapPost("/", Create).WithName("CreateDeck")
             .Produces<DeckDto>(StatusCodes.Status201Created)
             .Produces<DeckError>(StatusCodes.Status400BadRequest)
             .Produces<DeckError>(StatusCodes.Status409Conflict);
        group.MapPut("/{id:guid}", Update).WithName("UpdateDeck")
             .Produces<DeckDto>(StatusCodes.Status200OK)
             .Produces<DeckError>(StatusCodes.Status400BadRequest)
             .Produces<DeckError>(StatusCodes.Status404NotFound)
             .Produces<DeckError>(StatusCodes.Status409Conflict);
        group.MapDelete("/{id:guid}", Delete).WithName("DeleteDeck")
             .Produces(StatusCodes.Status204NoContent)
             .Produces<DeckError>(StatusCodes.Status404NotFound);
        group.MapPost("/parse", Parse).WithName("ParseDeck")
             .Produces<ParseDeckResultDto>(StatusCodes.Status200OK)
             .Produces(StatusCodes.Status400BadRequest)
             .Produces(StatusCodes.Status503ServiceUnavailable);
        return routes;
    }

    private static IResult MongoUnavailable() =>
        Results.Json(new DeckError("mongo-not-configured"), statusCode: StatusCodes.Status503ServiceUnavailable);

    private static IResult MapResult(DeckResult r, int successCode)
    {
        if (r.IsSuccess) return successCode switch
        {
            StatusCodes.Status201Created => Results.Created($"/decks/{r.Value!.Id}", r.Value),
            StatusCodes.Status204NoContent => Results.NoContent(),
            _ => Results.Ok(r.Value),
        };
        return r.Error!.Error switch
        {
            "deck-not-found" => Results.NotFound(r.Error),
            "invalid-deck" => Results.BadRequest(r.Error),
            "name-taken" or "deck-cap-reached" or "concurrent-edit" =>
                Results.Conflict(r.Error),
            _ => Results.Json(r.Error, statusCode: StatusCodes.Status500InternalServerError),
        };
    }

    private static string? SubOf(ClaimsPrincipal user) => user.FindFirst("sub")?.Value;

    private static async Task<IResult> List(
        ClaimsPrincipal user, [FromServices] DeckService? svc, CancellationToken ct)
    {
        if (svc == null) return MongoUnavailable();
        var sub = SubOf(user); if (sub == null) return Results.Unauthorized();
        return Results.Ok(await svc.ListAsync(sub, ct));
    }

    private static async Task<IResult> Get(
        Guid id, ClaimsPrincipal user, [FromServices] DeckService? svc, CancellationToken ct)
    {
        if (svc == null) return MongoUnavailable();
        var sub = SubOf(user); if (sub == null) return Results.Unauthorized();
        return MapResult(await svc.GetAsync(sub, id, ct), StatusCodes.Status200OK);
    }

    private static async Task<IResult> Create(
        CreateDeckRequest body, ClaimsPrincipal user, [FromServices] DeckService? svc, CancellationToken ct)
    {
        if (svc == null) return MongoUnavailable();
        var sub = SubOf(user); if (sub == null) return Results.Unauthorized();
        return MapResult(await svc.CreateAsync(sub, body, ct), StatusCodes.Status201Created);
    }

    private static async Task<IResult> Update(
        Guid id, UpdateDeckRequest body, ClaimsPrincipal user, [FromServices] DeckService? svc, CancellationToken ct)
    {
        if (svc == null) return MongoUnavailable();
        var sub = SubOf(user); if (sub == null) return Results.Unauthorized();
        return MapResult(await svc.UpdateAsync(sub, id, body, ct), StatusCodes.Status200OK);
    }

    private static async Task<IResult> Delete(
        Guid id, ClaimsPrincipal user, [FromServices] DeckService? svc, CancellationToken ct)
    {
        if (svc == null) return MongoUnavailable();
        var sub = SubOf(user); if (sub == null) return Results.Unauthorized();
        return MapResult(await svc.DeleteAsync(sub, id, ct), StatusCodes.Status204NoContent);
    }

    private static async Task<IResult> Parse(
        ParseDeckRequest body, ClaimsPrincipal user,
        [FromServices] DeckTextParser? parser,
        CancellationToken ct)
    {
        if (parser is null) return Results.Json(new DeckError("mongo-not-configured"), statusCode: StatusCodes.Status503ServiceUnavailable);
        var sub = SubOf(user); if (sub == null) return Results.Unauthorized();
        if (body is null || string.IsNullOrWhiteSpace(body.Text))
            return Results.BadRequest(new DeckError("empty-text"));
        if (body.Text.Length > 100_000)
            return Results.Json(new DeckError("too-large"),
                statusCode: StatusCodes.Status413PayloadTooLarge);
        // Pathological inputs (e.g. 100k newlines) can blow up parser
        // memory without tripping the byte cap. Cheap defensive cap.
        const int MaxLines = 5000;
        var lineCount = 1;
        for (int i = 0; i < body.Text.Length; i++)
        {
            if (body.Text[i] == '\n')
            {
                lineCount++;
                if (lineCount > MaxLines)
                    return Results.Json(new DeckError("too-many-lines"),
                        statusCode: StatusCodes.Status413PayloadTooLarge);
            }
        }

        var result = await parser.ParseAsync(body.Text, ct);
        var dto = new ParseDeckResultDto(
            Mainboard: result.Mainboard.Select(e => new DeckCardEntryDto(e.Name, e.Count)).ToList(),
            Sideboard: result.Sideboard.Select(e => new DeckCardEntryDto(e.Name, e.Count)).ToList(),
            Unknown: result.Unknown,
            Warnings: result.Warnings);
        return Results.Ok(dto);
    }
}
