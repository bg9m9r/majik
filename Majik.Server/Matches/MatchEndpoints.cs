using System.Security.Claims;
using Majik.Core.Api;
using Majik.Core.Api.Commands;
using Majik.Core.Api.Dtos;
using Majik.Server.Composition;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Majik.Server.Matches;

public static class MatchEndpoints
{
    // Keep for backwards-compat references in tests; canonical names live in
    // RateLimitPolicies.
    public const string RateLimitPolicy = RateLimitPolicies.Expensive;

    public static IEndpointRouteBuilder MapMatchEndpoints(this IEndpointRouteBuilder routes)
    {
        // Group default: "expensive" bucket (60 req/min).
        // Per-priority-window routes override to "in-match" (600 req/min) so
        // gameplay traffic doesn't hit the tight cap mid-game.
        var group = routes.MapGroup("/matches")
            .RequireAuthorization(AuthRegistration.AsPlayerPolicy)
            .RequireRateLimiting(RateLimitPolicies.Expensive)
            .WithTags("Matches");

        // --- "expensive" routes (inherit group policy) ---
        group.MapPost("/", Create).WithName("CreateMatch");
        group.MapGet("/", List).WithName("ListMatches");

        // Selectable bot archetypes (key + spaced label) for the
        // create-match wizard's "Bot Archetype" dropdown. Static catalog
        // data — no Mongo dependency, so it works even when Mongo is down.
        group.MapGet("/archetypes", ListBotArchetypes)
            .WithName("ListBotArchetypes")
            .Produces<IReadOnlyList<BotArchetypeDto>>(StatusCodes.Status200OK);

        group.MapGet("/{id:guid}", Get).WithName("GetMatch");
        group.MapPost("/{id:guid}/join", Join).WithName("JoinMatch");
        group.MapDelete("/{id:guid}", Abandon).WithName("AbandonMatch");

        // --- "in-match" routes (per-priority-window, 600 req/min) ---
        group.MapPost("/{id:guid}/roll", Roll)
            .WithName("SubmitRoll")
            .RequireRateLimiting(RateLimitPolicies.InMatch);

        group.MapPost("/{id:guid}/play-draw", PlayDraw)
            .WithName("ChoosePlayOrDraw")
            .RequireRateLimiting(RateLimitPolicies.InMatch);

        group.MapPost("/{id:guid}/concede", Concede)
            .WithName("ConcedeMatch")
            .RequireRateLimiting(RateLimitPolicies.InMatch);

        group.MapPost("/{id:guid}/commands", SubmitCommand)
            .WithName("SubmitMatchCommand")
            .RequireRateLimiting(RateLimitPolicies.InMatch);

        // Slice 5a — per-viewer auto-pass preferences. Caller PUTs a
        // full AutoPassPrefs snapshot; the next priority window picks
        // it up. Party-only authz. 204 NoContent on success.
        group.MapPut("/{id:guid}/me/prefs", SetAutoPassPrefs)
            .WithName("SetAutoPassPrefs")
            .RequireRateLimiting(RateLimitPolicies.InMatch);

        // Annotate the response shape so ng-openapi-gen emits a typed
        // GameStateDto return on the frontend client. Minimal-API infers
        // void otherwise (the handler returns IResult, which the spec
        // can't introspect into GameStateDto).
        group.MapGet("/{id:guid}/state", GetState)
            .WithName("GetMatchState")
            .RequireRateLimiting(RateLimitPolicies.InMatch)
            .Produces<GameStateDto>(StatusCodes.Status200OK)
            .Produces<MatchError>(StatusCodes.Status404NotFound)
            .Produces<MatchError>(StatusCodes.Status403Forbidden)
            .Produces<MatchError>(StatusCodes.Status409Conflict);

        // Replay log download — MVP "share a finished game" capability.
        // Returns the in-memory capture of EventDto + BotDecision records
        // in arrival order. Access is gated to seated players via
        // MatchService.GetReplayAsync.
        group.MapGet("/{id:guid}/replay", GetReplay)
            .WithName("GetMatchReplay")
            .RequireRateLimiting(RateLimitPolicies.InMatch)
            .Produces<MatchReplayDto>(StatusCodes.Status200OK)
            .Produces<MatchError>(StatusCodes.Status404NotFound)
            .Produces<MatchError>(StatusCodes.Status403Forbidden);

        return routes;
    }

    private static IResult ListBotArchetypes() =>
        Results.Ok(Majik.Bot.Decks.BotDeckCatalog.Archetypes
            .Select(a => new BotArchetypeDto(a, Majik.Bot.Decks.BotDeckCatalog.Label(a)))
            .ToList());

    private static IResult MongoUnavailable() =>
        Results.Json(new MatchError("mongo-not-configured"), statusCode: StatusCodes.Status503ServiceUnavailable);

    private static IResult MapResult<T>(Result<T> r) where T : class =>
        r.IsSuccess ? Results.Ok(r.Value) : ErrorToResult(r.Error!);

    private static IResult ErrorToResult(MatchError err) => err.Error switch
    {
        "match-not-found" or "deck-not-found"
                                   => Results.NotFound(err),
        "invalid-request" or "invalid-choice" or "invalid-clock-minutes"
            or "invalid-command" or "deck-invalid"
                                   => Results.BadRequest(err),
        "match-not-open" or "not-rolling" or "cannot-concede"
            or "match-in-progress" or "game-not-started" or "self-join-forbidden"
            or "game-state-unavailable"
                                   => Results.Conflict(err),
        "not-roll-winner" or "not-a-player" or "forbidden" or "no-profile" or "private-match"
                                   => Results.Json(err, statusCode: StatusCodes.Status403Forbidden),
        _                          => Results.Json(err, statusCode: StatusCodes.Status500InternalServerError),
    };

    private static string? SubOf(ClaimsPrincipal user) => user.FindFirst("sub")?.Value;

    private static async Task<IResult> Create(
        CreateMatchRequest body, ClaimsPrincipal user,
        [FromServices] MatchService? svc, CancellationToken ct)
    {
        if (svc == null) return MongoUnavailable();
        var sub = SubOf(user); if (sub == null) return Results.Unauthorized();
        var r = await svc.CreateAsync(sub, body, ct);
        return r.IsSuccess
            ? Results.Created($"/matches/{r.Value!.Id}", r.Value)
            : ErrorToResult(r.Error!);
    }

    private static async Task<IResult> List(
        [FromQuery] string? visibility,
        [FromServices] MatchService? svc, CancellationToken ct)
    {
        if (svc == null) return MongoUnavailable();
        var matches = await svc.ListOpenPublicAsync(ct);
        return Results.Ok(matches);
    }

    private static async Task<IResult> Get(
        Guid id, ClaimsPrincipal user,
        [FromServices] MatchService? svc, CancellationToken ct)
    {
        if (svc == null) return MongoUnavailable();
        var sub = SubOf(user); if (sub == null) return Results.Unauthorized();
        var r = await svc.GetAsync(sub, id, ct);
        return MapResult(r);
    }

    private static async Task<IResult> Join(
        Guid id, JoinMatchRequest body, ClaimsPrincipal user,
        [FromServices] MatchService? svc, CancellationToken ct)
    {
        if (svc == null) return MongoUnavailable();
        var sub = SubOf(user); if (sub == null) return Results.Unauthorized();
        var r = await svc.JoinAsync(sub, id, body, ct);
        return MapResult(r);
    }

    private static async Task<IResult> PlayDraw(
        Guid id, PlayDrawRequest body, ClaimsPrincipal user,
        [FromServices] MatchService? svc, CancellationToken ct)
    {
        if (svc == null) return MongoUnavailable();
        var sub = SubOf(user); if (sub == null) return Results.Unauthorized();
        var r = await svc.PlayDrawAsync(sub, id, body, ct);
        return MapResult(r);
    }

    private static async Task<IResult> Roll(
        Guid id, ClaimsPrincipal user,
        [FromServices] MatchService? svc, CancellationToken ct)
    {
        if (svc == null) return MongoUnavailable();
        var sub = SubOf(user); if (sub == null) return Results.Unauthorized();
        var r = await svc.SubmitRollAsync(sub, id, ct);
        return MapResult(r);
    }

    private static async Task<IResult> Concede(
        Guid id, ClaimsPrincipal user,
        [FromServices] MatchService? svc, CancellationToken ct)
    {
        if (svc == null) return MongoUnavailable();
        var sub = SubOf(user); if (sub == null) return Results.Unauthorized();
        var r = await svc.ConcedeAsync(sub, id, ct);
        return MapResult(r);
    }

    private static async Task<IResult> Abandon(
        Guid id, ClaimsPrincipal user,
        [FromServices] MatchService? svc, CancellationToken ct)
    {
        if (svc == null) return MongoUnavailable();
        var sub = SubOf(user); if (sub == null) return Results.Unauthorized();
        var r = await svc.AbandonAsync(sub, id, ct);
        return r.IsSuccess ? Results.NoContent() : ErrorToResult(r.Error!);
    }

    private static async Task<IResult> SetAutoPassPrefs(
        Guid id, AutoPassPrefs body, ClaimsPrincipal user,
        [FromServices] MatchService? svc, CancellationToken ct)
    {
        if (svc == null) return MongoUnavailable();
        var sub = SubOf(user); if (sub == null) return Results.Unauthorized();
        var r = await svc.SetAutoPassPrefsAsync(sub, id, body, ct);
        return r.IsSuccess ? Results.NoContent() : ErrorToResult(r.Error!);
    }

    private static async Task<IResult> SubmitCommand(
        Guid id, GameCommand body, ClaimsPrincipal user,
        [FromServices] MatchService? svc, CancellationToken ct)
    {
        if (svc == null) return MongoUnavailable();
        var sub = SubOf(user); if (sub == null) return Results.Unauthorized();
        var r = await svc.SubmitCommandAsync(sub, id, body, ct);
        return r.IsSuccess ? Results.NoContent() : ErrorToResult(r.Error!);
    }

    private static async Task<IResult> GetState(
        Guid id, ClaimsPrincipal user,
        [FromServices] MatchService? svc, CancellationToken ct)
    {
        if (svc == null) return MongoUnavailable();
        var sub = SubOf(user); if (sub == null) return Results.Unauthorized();
        var r = await svc.GetGameStateAsync(sub, id, ct);
        return MapResult(r);
    }

    private static async Task<IResult> GetReplay(
        Guid id, ClaimsPrincipal user,
        [FromServices] MatchService? svc, CancellationToken ct)
    {
        if (svc == null) return MongoUnavailable();
        var sub = SubOf(user); if (sub == null) return Results.Unauthorized();
        var r = await svc.GetReplayAsync(sub, id, ct);
        return MapResult(r);
    }
}
