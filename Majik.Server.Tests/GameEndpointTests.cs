using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Majik.Server.Endpoints;
using Xunit;

namespace Majik.Server.Tests;

/// <summary>End-to-end coverage of the /games REST surface. Auth comes
/// for free via <see cref="TestAppFactory"/> — every request is signed
/// in by <see cref="TestAuthHandler"/>.</summary>
public class GameEndpointTests : IClassFixture<TestAppFactory>
{
    private readonly TestAppFactory _factory;

    public GameEndpointTests(TestAppFactory factory) { _factory = factory; }

    [Fact]
    public async Task Healthz_AnonymousAllowed_Returns200()
    {
        // /healthz is AllowAnonymous; using the unauthed (default)
        // client should still succeed.
        using var unauthedClient = new HttpClient { BaseAddress = _factory.Server.BaseAddress };
        unauthedClient.BaseAddress = _factory.ClientOptions.BaseAddress;

        var client = _factory.CreateClient();
        var response = await client.GetAsync("/healthz");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task CreateGame_ReturnsCreatedWithBothSlots()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/games",
            new GameEndpoints.CreateGameRequest("Alice", "Bob"));

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await response.Content.ReadFromJsonAsync<GameEndpoints.CreateGameResponse>();
        body.Should().NotBeNull();
        body!.GameId.Should().NotBe(Guid.Empty);
        body.Players.Should().HaveCount(2);
        body.Players[0].Name.Should().Be("Alice");
        body.Players[1].Name.Should().Be("Bob");
    }

    [Fact]
    public async Task CreateGame_RejectsEmptyName()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/games",
            new GameEndpoints.CreateGameRequest("", "Bob"));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task GetGame_ReturnsCreatedShape_AfterCreate()
    {
        var client = _factory.CreateClient();

        var created = await client.PostAsJsonAsync(
            "/games",
            new GameEndpoints.CreateGameRequest("Alice", "Bob"));
        var createdBody = await created.Content.ReadFromJsonAsync<GameEndpoints.CreateGameResponse>();

        var fetched = await client.GetAsync($"/games/{createdBody!.GameId}");
        fetched.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await fetched.Content.ReadFromJsonAsync<GameEndpoints.CreateGameResponse>();
        body!.GameId.Should().Be(createdBody.GameId);
        body.Players.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetGame_Returns404_WhenNotFound()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync($"/games/{Guid.NewGuid()}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task DeleteGame_RemovesIt_AndSecondDeleteIs404()
    {
        var client = _factory.CreateClient();
        var created = await client.PostAsJsonAsync(
            "/games",
            new GameEndpoints.CreateGameRequest("Alice", "Bob"));
        var body = await created.Content.ReadFromJsonAsync<GameEndpoints.CreateGameResponse>();

        var firstDelete = await client.DeleteAsync($"/games/{body!.GameId}");
        firstDelete.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var secondDelete = await client.DeleteAsync($"/games/{body.GameId}");
        secondDelete.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
