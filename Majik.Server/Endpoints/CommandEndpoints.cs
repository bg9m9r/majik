using System.Security.Claims;
using Majik.Core.Api.Commands;
using Majik.Server.Auth;
using Majik.Server.Composition;

namespace Majik.Server.Endpoints;

/// <summary>
/// Per-game command + state endpoints. Commands are the post-auth path
/// for the client to drive the engine: every IPlayerAgent decision
/// surfaces here as a polymorphic <see cref="GameCommand"/>.
///
/// Serialization: <see cref="GameCommand"/> carries a JsonPolymorphic
/// discriminator (`$type`), so a JSON body like
/// `{"$type":"pass","playerId":"..."}` is auto-resolved to the right
/// concrete command record.
/// </summary>
public static class CommandEndpoints
{
    public static IEndpointRouteBuilder MapCommandEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/games/{id:guid}")
            .RequireAuthorization(AuthRegistration.AsPlayerPolicy)
            .WithTags("GamePlay");

        group.MapPost("/commands", SubmitCommand)
             .WithName("SubmitCommand");

        group.MapPost("/start", StartGame)
             .WithName("StartGame");

        group.MapGet("/state", GetState)
             .WithName("GetGameState");

        return routes;
    }

    private static async Task<IResult> SubmitCommand(
        Guid id,
        GameCommand command,
        ClaimsPrincipal user,
        ServerGameFactory factory,
        GameSeating seating,
        CancellationToken ct)
    {
        var facade = factory.Get(id);
        if (facade == null) return Results.NotFound(new { error = $"Game {id} not found" });

        var sub = user.FindFirst("sub")?.Value;
        if (sub == null) return Results.Unauthorized();

        if (!seating.OwnsSlot(id, command.PlayerId, sub))
        {
            return Results.Forbid();
        }

        try
        {
            await facade.SubmitAsync(command, ct);
            return Results.NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
        catch (ArgumentException ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
    }

    private static async Task<IResult> StartGame(
        Guid id,
        ServerGameFactory factory,
        CancellationToken ct)
    {
        var facade = factory.Get(id);
        if (facade == null) return Results.NotFound(new { error = $"Game {id} not found" });

        await facade.StartAsync(ct);
        return Results.Ok(facade.GetState());
    }

    private static IResult GetState(Guid id, ServerGameFactory factory)
    {
        var facade = factory.Get(id);
        return facade == null
            ? Results.NotFound(new { error = $"Game {id} not found" })
            : Results.Ok(facade.GetState());
    }
}
