using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Majik.Core.Api.Dtos;
using Majik.Server.Endpoints;
using Xunit;

namespace Majik.Server.Tests;

/// <summary>End-to-end coverage of CR 706 visibility filtering on the
/// /games/{id}/state endpoint. The state response is scoped to the
/// caller's first claimed seat: opponent hand cards arrive as
/// (hidden) placeholders.</summary>
public class VisibilityTests : IClassFixture<TestAppFactory>
{
    private readonly TestAppFactory _factory;
    public VisibilityTests(TestAppFactory factory) { _factory = factory; }

    private HttpClient ClientAs(string sub)
    {
        var c = _factory.CreateClient();
        c.DefaultRequestHeaders.Add("X-Test-Sub", sub);
        return c;
    }

    [Fact]
    public async Task GetState_CallerWithoutSeat_Returns403()
    {
        var creator = ClientAs("creator");
        var create = await creator.PostAsJsonAsync(
            "/games",
            new GameEndpoints.CreateGameRequest("Alice", "Bob"));
        var game = (await create.Content.ReadFromJsonAsync<GameEndpoints.CreateGameResponse>())!;

        var spectator = ClientAs("uninvolved");
        var resp = await spectator.GetAsync($"/games/{game.GameId}/state");

        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task GetState_ReturnsSnapshot_ForClaimingCaller()
    {
        var alice = ClientAs("alice-sub");
        var create = await alice.PostAsJsonAsync(
            "/games",
            new GameEndpoints.CreateGameRequest("Alice", "Bob"));
        var game = (await create.Content.ReadFromJsonAsync<GameEndpoints.CreateGameResponse>())!;

        await alice.PostAsJsonAsync(
            $"/games/{game.GameId}/seat",
            new SeatEndpoints.ClaimSeatRequest(game.Players[0].Id));

        var resp = await alice.GetAsync($"/games/{game.GameId}/state");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var state = await resp.Content.ReadFromJsonAsync<GameStateDto>();
        state.Should().NotBeNull();
        state!.GameId.Should().Be(game.GameId);
        state.Players.Should().HaveCount(2);
    }
}
