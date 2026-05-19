using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Majik.Server.Endpoints;
using Xunit;

namespace Majik.Server.Tests;

/// <summary>End-to-end coverage of identity → player-slot binding.
/// Uses X-Test-Sub on the request to scope different principals.</summary>
public class SeatingTests : IClassFixture<TestAppFactory>
{
    private readonly TestAppFactory _factory;

    public SeatingTests(TestAppFactory factory) { _factory = factory; }

    private HttpClient ClientAs(string sub)
    {
        var c = _factory.CreateClient();
        c.DefaultRequestHeaders.Add("X-Test-Sub", sub);
        return c;
    }

    private async Task<GameEndpoints.CreateGameResponse> CreateGameAs(string sub)
    {
        var client = ClientAs(sub);
        var resp = await client.PostAsJsonAsync(
            "/games",
            new GameEndpoints.CreateGameRequest("Alice", "Bob"));
        resp.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await resp.Content.ReadFromJsonAsync<GameEndpoints.CreateGameResponse>())!;
    }

    [Fact]
    public async Task ClaimSeat_FirstTime_Succeeds()
    {
        var game = await CreateGameAs("alice-sub");
        var alice = ClientAs("alice-sub");

        var resp = await alice.PostAsJsonAsync(
            $"/games/{game.GameId}/seat",
            new SeatEndpoints.ClaimSeatRequest(game.Players[0].Id));

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task ClaimSeat_DifferentSub_Conflicts()
    {
        var game = await CreateGameAs("alice-sub");

        var alice = ClientAs("alice-sub");
        (await alice.PostAsJsonAsync(
            $"/games/{game.GameId}/seat",
            new SeatEndpoints.ClaimSeatRequest(game.Players[0].Id)))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var mallory = ClientAs("mallory-sub");
        var resp = await mallory.PostAsJsonAsync(
            $"/games/{game.GameId}/seat",
            new SeatEndpoints.ClaimSeatRequest(game.Players[0].Id));

        resp.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task ClaimSeat_SameSubReclaim_Idempotent()
    {
        var game = await CreateGameAs("alice-sub");
        var alice = ClientAs("alice-sub");

        var first = await alice.PostAsJsonAsync(
            $"/games/{game.GameId}/seat",
            new SeatEndpoints.ClaimSeatRequest(game.Players[0].Id));
        var second = await alice.PostAsJsonAsync(
            $"/games/{game.GameId}/seat",
            new SeatEndpoints.ClaimSeatRequest(game.Players[0].Id));

        first.StatusCode.Should().Be(HttpStatusCode.OK);
        second.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task ClaimSeat_UnknownPlayer_Returns400()
    {
        var game = await CreateGameAs("alice-sub");
        var alice = ClientAs("alice-sub");

        var resp = await alice.PostAsJsonAsync(
            $"/games/{game.GameId}/seat",
            new SeatEndpoints.ClaimSeatRequest(Guid.NewGuid()));

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task ClaimSeat_UnknownGame_Returns404()
    {
        var alice = ClientAs("alice-sub");

        var resp = await alice.PostAsJsonAsync(
            $"/games/{Guid.NewGuid()}/seat",
            new SeatEndpoints.ClaimSeatRequest(Guid.NewGuid()));

        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetMySeats_ReflectsClaimedSlots()
    {
        var game = await CreateGameAs("alice-sub");
        var alice = ClientAs("alice-sub");
        await alice.PostAsJsonAsync(
            $"/games/{game.GameId}/seat",
            new SeatEndpoints.ClaimSeatRequest(game.Players[0].Id));

        var resp = await alice.GetAsync($"/games/{game.GameId}/seat");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await resp.Content.ReadFromJsonAsync<MySeatsResponse>();
        body!.PlayerIds.Should().ContainSingle().Which.Should().Be(game.Players[0].Id);
    }

    private sealed record MySeatsResponse(Guid GameId, IReadOnlyList<Guid> PlayerIds);
}
