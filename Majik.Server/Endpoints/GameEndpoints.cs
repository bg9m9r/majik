using Majik.Core.Api;
using Majik.Server.Composition;

namespace Majik.Server.Endpoints;

/// <summary>
/// Per-game REST endpoints. Auth: AsPlayer policy on every route —
/// caller must be authenticated. Per-player-slot authorization (only
/// Alice can act as Alice) lands in a later slice when player slots
/// learn their owning principal.
/// </summary>
public static class GameEndpoints
{
    public sealed record CreateGameRequest(string AliceName, string BobName);

    public sealed record CreateGameResponse(
        Guid GameId,
        IReadOnlyList<PlayerSlotResponse> Players);

    public sealed record PlayerSlotResponse(Guid Id, string Name);

    public static IEndpointRouteBuilder MapGameEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/games")
            .RequireAuthorization(AuthRegistration.AsPlayerPolicy)
            .WithTags("Games");

        group.MapPost("/", CreateGame)
             .WithName("CreateGame")
             .Produces<CreateGameResponse>(StatusCodes.Status201Created)
             .Produces(StatusCodes.Status400BadRequest)
             .Produces(StatusCodes.Status401Unauthorized);

        group.MapGet("/{id:guid}", GetGame)
             .WithName("GetGame")
             .Produces<CreateGameResponse>(StatusCodes.Status200OK)
             .Produces(StatusCodes.Status401Unauthorized)
             .Produces(StatusCodes.Status404NotFound);

        group.MapGet("/", ListGames)
             .WithName("ListGames")
             .Produces(StatusCodes.Status200OK);

        group.MapDelete("/{id:guid}", DeleteGame)
             .WithName("DeleteGame")
             .Produces(StatusCodes.Status204NoContent)
             .Produces(StatusCodes.Status404NotFound);

        return routes;
    }

    private static IResult CreateGame(CreateGameRequest body, ServerGameFactory factory)
    {
        if (string.IsNullOrWhiteSpace(body.AliceName) || string.IsNullOrWhiteSpace(body.BobName))
        {
            return Results.BadRequest(new { error = "Both player names required" });
        }

        var facade = factory.Create(body.AliceName, body.BobName);
        var response = ToCreateResponse(facade);

        return Results.Created($"/games/{facade.GameId}", response);
    }

    private static IResult GetGame(Guid id, ServerGameFactory factory)
    {
        var facade = factory.Get(id);
        return facade == null
            ? Results.NotFound(new { error = $"Game {id} not found" })
            : Results.Ok(ToCreateResponse(facade));
    }

    private static IResult ListGames(ServerGameFactory factory)
        => Results.Ok(new { count = factory.Count });

    private static IResult DeleteGame(Guid id, ServerGameFactory factory)
        => factory.Delete(id)
            ? Results.NoContent()
            : Results.NotFound(new { error = $"Game {id} not found" });

    private static CreateGameResponse ToCreateResponse(GameFacade facade)
    {
        var state = facade.GetState();
        var players = state.Players
            .Select(p => new PlayerSlotResponse(p.Id, p.Name))
            .ToList();
        return new CreateGameResponse(facade.GameId, players);
    }
}
