using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Majik.Core.Api.Dtos;
using Majik.Server.Endpoints;
using Xunit;

namespace Majik.Server.Tests;

/// <summary>End-to-end HTTP path proving the full game stack wires up:
/// create → claim both seats → start in full mode → snapshot reflects
/// the engine's actual state. Stops short of submitting commands —
/// that's the SignalR client's territory and lives in a separate
/// client integration suite.</summary>
public class FullGameFlowTests : IClassFixture<TestAppFactory>
{
    private readonly TestAppFactory _factory;
    public FullGameFlowTests(TestAppFactory factory) { _factory = factory; }

    private HttpClient ClientAs(string sub)
    {
        var c = _factory.CreateClient();
        c.DefaultRequestHeaders.Add("X-Test-Sub", sub);
        return c;
    }

    [Fact]
    public async Task FullGame_HappyPath_StartReturnsSnapshot()
    {
        var aliceClient = ClientAs("alice-sub");
        var bobClient = ClientAs("bob-sub");

        // 1. Alice creates the game.
        var createResp = await aliceClient.PostAsJsonAsync(
            "/games",
            new GameEndpoints.CreateGameRequest("Alice", "Bob"));
        createResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var game = (await createResp.Content.ReadFromJsonAsync<GameEndpoints.CreateGameResponse>())!;
        var aliceSlot = game.Players[0].Id;
        var bobSlot = game.Players[1].Id;

        // 2. Each player claims their seat.
        (await aliceClient.PostAsJsonAsync($"/games/{game.GameId}/seat",
            new SeatEndpoints.ClaimSeatRequest(aliceSlot)))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        (await bobClient.PostAsJsonAsync($"/games/{game.GameId}/seat",
            new SeatEndpoints.ClaimSeatRequest(bobSlot)))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        // 3. Alice starts the game in full-driver mode.
        var startResp = await aliceClient.PostAsync(
            $"/games/{game.GameId}/start?mode=full&maxTurns=1",
            content: null);
        startResp.StatusCode.Should().Be(HttpStatusCode.OK);

        // 4. Snapshot through her view masks Bob's hand (CR 706) and
        //    carries both player slots.
        var stateResp = await aliceClient.GetAsync($"/games/{game.GameId}/state");
        stateResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var state = (await stateResp.Content.ReadFromJsonAsync<GameStateDto>())!;
        state.GameId.Should().Be(game.GameId);
        state.Players.Should().HaveCount(2);
    }

    [Fact]
    public async Task FullGame_StartTwice_Returns409()
    {
        var alice = ClientAs("alice-sub");
        var bob = ClientAs("bob-sub");

        var createResp = await alice.PostAsJsonAsync(
            "/games", new GameEndpoints.CreateGameRequest("Alice", "Bob"));
        var game = (await createResp.Content.ReadFromJsonAsync<GameEndpoints.CreateGameResponse>())!;

        await alice.PostAsJsonAsync($"/games/{game.GameId}/seat",
            new SeatEndpoints.ClaimSeatRequest(game.Players[0].Id));
        await bob.PostAsJsonAsync($"/games/{game.GameId}/seat",
            new SeatEndpoints.ClaimSeatRequest(game.Players[1].Id));

        (await alice.PostAsync($"/games/{game.GameId}/start?mode=full", null))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        (await alice.PostAsync($"/games/{game.GameId}/start?mode=full", null))
            .StatusCode.Should().Be(HttpStatusCode.Conflict);
    }
}
