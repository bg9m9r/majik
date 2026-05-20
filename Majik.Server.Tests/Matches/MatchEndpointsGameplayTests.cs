using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Majik.Server.Matches;
using Majik.Server.Profiles;
using Majik.Server.Tests.Profiles;
using Microsoft.AspNetCore.Authentication;
using MongoDB.Driver;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Majik.Server.Tests.Matches;

/// <summary>
/// Integration tests for the /matches/{id}/commands and /matches/{id}/state
/// endpoints. Requires matches in Playing state.
/// </summary>
public class MatchEndpointsGameplayTests : IClassFixture<TestMongoFixture>
{
    private readonly TestMongoFixture _fixture;

    public MatchEndpointsGameplayTests(TestMongoFixture fixture) => _fixture = fixture;

    // -----------------------------------------------------------------------
    // Factory helpers
    // -----------------------------------------------------------------------

    private WebApplicationFactory<Program> Factory(
        IMongoDatabase db,
        IRandomSource? rng = null)
    {
        return new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Mongo:ConnectionString", _fixture.ConnectionString);
            builder.UseSetting("Mongo:Database", db.DatabaseNamespace.DatabaseName);
            builder.ConfigureServices(services =>
            {
                services.RemoveAll(typeof(IHostedService));
                services.PostConfigure<AuthenticationOptions>(opts =>
                {
                    opts.DefaultAuthenticateScheme = MatchTestAuthHandler.SchemeName;
                    opts.DefaultChallengeScheme = MatchTestAuthHandler.SchemeName;
                });
                services.AddAuthentication(MatchTestAuthHandler.SchemeName)
                    .AddScheme<AuthenticationSchemeOptions, MatchTestAuthHandler>(
                        MatchTestAuthHandler.SchemeName, _ => { });

                if (rng != null)
                {
                    services.RemoveAll<IRandomSource>();
                    services.AddSingleton<IRandomSource>(rng);
                }
            });
        });
    }

    private static HttpClient AuthedClient(WebApplicationFactory<Program> factory, string sub)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue(MatchTestAuthHandler.SchemeName, sub);
        return client;
    }

    private async Task<IMongoDatabase> FreshDb()
    {
        var db = _fixture.NewDatabase();
        await new MatchRepository(db).EnsureIndexesAsync(CancellationToken.None);
        await new UserProfileRepository(db).EnsureIndexesAsync(CancellationToken.None);
        return db;
    }

    private async Task SeedProfile(IMongoDatabase db, string sub, string handle)
    {
        var repo = new UserProfileRepository(db);
        await repo.UpsertAsync(new UserProfile
        {
            Sub = sub,
            Handle = handle.ToLowerInvariant(),
            HandleDisplay = handle,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        }, CancellationToken.None);
    }

    /// <summary>
    /// Helper: creates a match (alice) + joins (bob) + alice chooses play/draw,
    /// returning the match in Playing state. Stub RNG ensures alice wins roll (6 vs 1).
    /// </summary>
    private async Task<(Guid matchId, WebApplicationFactory<Program> factory)> SetupPlayingMatch()
    {
        var db = await FreshDb();
        await SeedProfile(db, "alice", "Alice");
        await SeedProfile(db, "bob", "Bob");

        // alice wins the roll: creator=6, opponent=1
        var rng = new StubRandomSource(new Queue<int>(new[] { 6, 1 }));
        var factory = Factory(db, rng);
        var aliceClient = AuthedClient(factory, "alice");
        var bobClient = AuthedClient(factory, "bob");

        // Create
        var created = await aliceClient.PostAsJsonAsync("/matches",
            new { format = "constructed", visibility = "public", deckId = "deck-a", clockMinutes = 20 });
        created.StatusCode.Should().Be(HttpStatusCode.Created);
        var matchDto = await created.Content.ReadFromJsonAsync<MatchDto>();

        // Bob joins → dice roll happens
        var joined = await bobClient.PostAsJsonAsync($"/matches/{matchDto!.Id}/join",
            new { deckId = "deck-b" });
        joined.StatusCode.Should().Be(HttpStatusCode.OK);
        var joinedDto = await joined.Content.ReadFromJsonAsync<MatchDto>();
        joinedDto!.State.Should().Be("Rolling");
        joinedDto.Roll!.WinnerSub.Should().Be("alice");

        // Alice (winner) chooses play → Playing
        var playDraw = await aliceClient.PostAsJsonAsync($"/matches/{matchDto.Id}/play-draw",
            new { choice = "play" });
        playDraw.StatusCode.Should().Be(HttpStatusCode.OK);
        var playDto = await playDraw.Content.ReadFromJsonAsync<MatchDto>();
        playDto!.State.Should().Be("Playing");

        return (matchDto.Id, factory);
    }

    // -----------------------------------------------------------------------
    // POST /matches/{id}/commands — before Playing → 409
    // -----------------------------------------------------------------------

    [Fact]
    public async Task SubmitCommand_BeforePlaying_Returns409()
    {
        var db = await FreshDb();
        await SeedProfile(db, "alice", "Alice");
        using var factory = Factory(db);
        var client = AuthedClient(factory, "alice");

        // Create match (state=Open, not Playing)
        var created = await client.PostAsJsonAsync("/matches",
            new { format = "constructed", visibility = "public", deckId = "deck-a", clockMinutes = 20 });
        var matchDto = await created.Content.ReadFromJsonAsync<MatchDto>();

        // Use the polymorphic $type discriminator for PassPriorityCommand
        var resp = await client.PostAsJsonAsync(
            $"/matches/{matchDto!.Id}/commands",
            new System.Collections.Generic.Dictionary<string, object> { ["$type"] = "pass" });

        // match-not-open → 409 Conflict
        resp.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    // -----------------------------------------------------------------------
    // POST /matches/{id}/commands — by non-party → 403
    // -----------------------------------------------------------------------

    [Fact]
    public async Task SubmitCommand_ByNonParty_Returns403()
    {
        var (matchId, factory) = await SetupPlayingMatch();
        using (factory)
        {
            var charlieClient = AuthedClient(factory, "charlie");

            // Use the polymorphic $type discriminator for PassPriorityCommand
            var resp = await charlieClient.PostAsJsonAsync(
                $"/matches/{matchId}/commands",
                new System.Collections.Generic.Dictionary<string, object> { ["$type"] = "pass" });

            resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
            var body = await resp.Content.ReadFromJsonAsync<MatchError>();
            body!.Error.Should().Be("forbidden");
        }
    }

    // -----------------------------------------------------------------------
    // GET /matches/{id}/state — during Playing → 200 GameStateDto
    // -----------------------------------------------------------------------

    [Fact]
    public async Task GetState_DuringPlaying_Returns200GameStateDto()
    {
        // The ServerGameFactory is registered in the test DI pipeline.
        // After join + play-draw, the engine is actually running and returns
        // a real GameStateDto.
        var (matchId, factory) = await SetupPlayingMatch();
        using (factory)
        {
            var aliceClient = AuthedClient(factory, "alice");

            var resp = await aliceClient.GetAsync($"/matches/{matchId}/state");

            resp.StatusCode.Should().Be(HttpStatusCode.OK);
            // Response body is a non-null GameStateDto JSON object
            var body = await resp.Content.ReadAsStringAsync();
            body.Should().NotBeNullOrEmpty();
        }
    }
}
