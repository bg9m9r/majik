using System.Security.Claims;
using Majik.Server.Auth;
using Majik.Server.Composition;

namespace Majik.Server.Endpoints;

/// <summary>
/// Identity → player-slot binding. After a game is created via
/// /games, each authenticated principal claims one of the two player
/// slots before submitting commands as that player. The server uses
/// <see cref="GameSeating"/> to enforce that the calling `sub` owns the
/// slot it tries to act on.
/// </summary>
public static class SeatEndpoints
{
    public sealed record ClaimSeatRequest(Guid PlayerId);
    public sealed record ClaimSeatResponse(Guid GameId, Guid PlayerId, string Sub);

    public static IEndpointRouteBuilder MapSeatEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/games/{id:guid}/seat")
            .RequireAuthorization(AuthRegistration.AsPlayerPolicy)
            .WithTags("Seating");

        group.MapPost("/", ClaimSeat)
             .WithName("ClaimSeat")
             .Produces<ClaimSeatResponse>(StatusCodes.Status200OK)
             .Produces(StatusCodes.Status400BadRequest)
             .Produces(StatusCodes.Status404NotFound)
             .Produces(StatusCodes.Status409Conflict);

        group.MapGet("/", GetMySeats)
             .WithName("GetMySeats")
             .Produces(StatusCodes.Status200OK);

        return routes;
    }

    private static IResult ClaimSeat(
        Guid id,
        ClaimSeatRequest body,
        ClaimsPrincipal user,
        ServerGameFactory factory,
        GameSeating seating)
    {
        var sub = user.FindFirst("sub")?.Value;
        if (sub == null) return Results.Unauthorized();

        var facade = factory.Get(id);
        if (facade == null) return Results.NotFound(new { error = $"Game {id} not found" });

        var validSlot = facade.GetState().Players.Any(p => p.Id == body.PlayerId);
        if (!validSlot) return Results.BadRequest(new { error = $"Player {body.PlayerId} is not in this game" });

        var result = seating.TryClaim(id, body.PlayerId, sub);
        return result switch
        {
            GameSeating.ClaimResult.Claimed => Results.Ok(new ClaimSeatResponse(id, body.PlayerId, sub)),
            GameSeating.ClaimResult.AlreadyOurs => Results.Ok(new ClaimSeatResponse(id, body.PlayerId, sub)),
            GameSeating.ClaimResult.Conflict => Results.Conflict(new { error = "Slot already claimed by another player" }),
            _ => Results.StatusCode(500),
        };
    }

    private static IResult GetMySeats(
        Guid id,
        ClaimsPrincipal user,
        GameSeating seating)
    {
        var sub = user.FindFirst("sub")?.Value;
        if (sub == null) return Results.Unauthorized();

        return Results.Ok(new { gameId = id, playerIds = seating.SlotsForSub(id, sub) });
    }
}
