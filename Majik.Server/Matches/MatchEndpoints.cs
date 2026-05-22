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
    public const string RateLimitPolicy = "authed-60-per-min";

    public static IEndpointRouteBuilder MapMatchEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/matches")
            .RequireAuthorization(AuthRegistration.AsPlayerPolicy)
            .RequireRateLimiting(RateLimitPolicy)
            .WithTags("Matches");

        group.MapPost("/", Create).WithName("CreateMatch");
        group.MapGet("/", List).WithName("ListMatches");
        group.MapGet("/{id:guid}", Get).WithName("GetMatch");
        group.MapPost("/{id:guid}/join", Join).WithName("JoinMatch");
        group.MapPost("/{id:guid}/roll", Roll).WithName("SubmitRoll");
        group.MapPost("/{id:guid}/play-draw", PlayDraw).WithName("ChoosePlayOrDraw");
        group.MapPost("/{id:guid}/concede", Concede).WithName("ConcedeMatch");
        group.MapDelete("/{id:guid}", Abandon).WithName("AbandonMatch");

        group.MapPost("/{id:guid}/commands", SubmitCommand).WithName("SubmitMatchCommand");
        group.MapGet("/{id:guid}/state", GetState).WithName("GetMatchState");
        return routes;
    }

    private static IResult MongoUnavailable() =>
        Results.Json(new MatchError("mongo-not-configured"), statusCode: StatusCodes.Status503ServiceUnavailable);

    private static IResult MapResult<T>(Result<T> r) where T : class =>
        r.IsSuccess ? Results.Ok(r.Value) : ErrorToResult(r.Error!);

    private static IResult ErrorToResult(MatchError err) => err.Error switch
    {
        "match-not-found"          => Results.NotFound(err),
        "invalid-request" or "invalid-choice" or "invalid-clock-minutes"
                                   => Results.BadRequest(err),
        "match-not-open" or "not-rolling" or "cannot-concede"
            or "match-in-progress" or "game-not-started" or "self-join-forbidden"
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
}
