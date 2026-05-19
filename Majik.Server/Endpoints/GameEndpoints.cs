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
             .WithName("CreateGame");

        group.MapGet("/{id:guid}", GetGame)
             .WithName("GetGame");

        group.MapGet("/", ListGames)
             .WithName("ListGames");

        group.MapDelete("/{id:guid}", DeleteGame)
             .WithName("DeleteGame");

        return routes;
    }

    private static IResult CreateGame(CreateGameRequest body, GameRegistry registry)
    {
        if (string.IsNullOrWhiteSpace(body.AliceName) || string.IsNullOrWhiteSpace(body.BobName))
        {
            return Results.BadRequest(new { error = "Both player names required" });
        }

        var facade = registry.Create(body.AliceName, body.BobName);
        var response = ToCreateResponse(facade);

        return Results.Created($"/games/{facade.GameId}", response);
    }

    private static IResult GetGame(Guid id, GameRegistry registry)
    {
        var facade = registry.Get(id);
        return facade == null
            ? Results.NotFound(new { error = $"Game {id} not found" })
            : Results.Ok(ToCreateResponse(facade));
    }

    private static IResult ListGames(GameRegistry registry)
        => Results.Ok(new { count = registry.Count });

    private static IResult DeleteGame(Guid id, GameRegistry registry)
        => registry.Remove(id)
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
