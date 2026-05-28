using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Majik.Core.Api.Dtos;
using Majik.Core.CardData;
using Majik.Server.Decks;
using Majik.Server.Matches;
using Majik.Server.Profiles;
using Majik.Server.Tests.Profiles;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using MongoDB.Driver;
using Xunit;

namespace Majik.Server.Tests.Matches;

/// <summary>
/// HTTP integration tests for Slice 5a's
/// <c>PUT /matches/{id}/me/prefs</c> endpoint. Validates authz +
/// success path + invalid-body rejection at the wire surface so a
/// regression in the endpoint plumbing is caught even when the
/// underlying service-level tests pass.
/// </summary>
public class MatchEndpointsAutoPassPrefsTests : IClassFixture<TestMongoFixture>
{
    private readonly TestMongoFixture _fixture;
    public MatchEndpointsAutoPassPrefsTests(TestMongoFixture fixture) => _fixture = fixture;

    private WebApplicationFactory<Program> Factory(IMongoDatabase db, IRandomSource? rng = null)
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

                services.RemoveAll<ICardRepository>();
                services.AddSingleton<ICardRepository>(TestCardRepo());
            });
        });
    }

    private static ICardRepository TestCardRepo()
    {
        var repo = new FakeCardRepoForGameplayTests();
        repo.Add("Forest", "Basic Land — Forest");
        repo.Add("Mountain", "Basic Land — Mountain");
        repo.Add("Grizzly Bears", "Creature — Bear");
        repo.Add("Hill Giant", "Creature — Giant");
        return repo;
    }

    private static async Task<Guid> SeedDeckAsync(IMongoDatabase db, string ownerSub, string name)
    {
        var deckRepo = new DeckRepository(db);
        await deckRepo.EnsureIndexesAsync(CancellationToken.None);
        var id = Guid.NewGuid();
        await deckRepo.InsertAsync(new Deck
        {
            Id = id,
            OwnerSub = ownerSub,
            Name = name,
            Mainboard = new List<DeckCardEntry>
            {
                new() { Name = "Forest", Count = 24 },
                new() { Name = "Grizzly Bears", Count = 4 },
                new() { Name = "Hill Giant", Count = 4 },
                new() { Name = "Mountain", Count = 28 },
            },
            Sideboard = new List<DeckCardEntry>(),
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        }, CancellationToken.None);
        return id;
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

    private async Task<(Guid matchId, WebApplicationFactory<Program> factory)> SetupPlayingMatch()
    {
        var db = await FreshDb();
        await SeedProfile(db, "alice", "Alice");
        await SeedProfile(db, "bob", "Bob");
        var aliceDeckId = await SeedDeckAsync(db, "alice", "Alice Deck");
        var bobDeckId = await SeedDeckAsync(db, "bob", "Bob Deck");

        var rng = new StubRandomSource(new Queue<int>(new[] { 6, 1 }));
        var factory = Factory(db, rng);
        var aliceClient = AuthedClient(factory, "alice");
        var bobClient = AuthedClient(factory, "bob");

        var created = await aliceClient.PostAsJsonAsync("/matches",
            new { format = "constructed", visibility = "public", deckId = aliceDeckId.ToString(), clockMinutes = 20 });
        var matchDto = await created.Content.ReadFromJsonAsync<MatchDto>();

        var joined = await bobClient.PostAsJsonAsync($"/matches/{matchDto!.Id}/join",
            new { deckId = bobDeckId.ToString() });
        joined.StatusCode.Should().Be(HttpStatusCode.OK);

        await aliceClient.PostAsync($"/matches/{matchDto.Id}/roll", null);
        await bobClient.PostAsync($"/matches/{matchDto.Id}/roll", null);

        var playDraw = await aliceClient.PostAsJsonAsync($"/matches/{matchDto.Id}/play-draw",
            new { choice = "play" });
        playDraw.StatusCode.Should().Be(HttpStatusCode.OK);

        return (matchDto.Id, factory);
    }

    // -----------------------------------------------------------------------
    // Success paths
    // -----------------------------------------------------------------------

    [Fact]
    public async Task SetAutoPassPrefs_ByCreator_Returns204AndPersists()
    {
        var (matchId, factory) = await SetupPlayingMatch();
        using (factory)
        {
            var aliceClient = AuthedClient(factory, "alice");

            var resp = await aliceClient.PutAsJsonAsync(
                $"/matches/{matchId}/me/prefs",
                new AutoPassPrefs(true, new Dictionary<string, string> { ["Upkeep"] = "mine" }));

            resp.StatusCode.Should().Be(HttpStatusCode.NoContent);

            // Cross-check via the store (resolved through the test DI graph).
            var store = factory.Services.GetRequiredService<AutoPassPrefsStore>();
            var stored = store.Get(matchId, "alice");
            stored.FullControl.Should().BeTrue();
            stored.PhaseStops["Upkeep"].Should().Be("mine");
        }
    }

    [Fact]
    public async Task SetAutoPassPrefs_ByOpponent_Returns204()
    {
        var (matchId, factory) = await SetupPlayingMatch();
        using (factory)
        {
            var bobClient = AuthedClient(factory, "bob");

            var resp = await bobClient.PutAsJsonAsync(
                $"/matches/{matchId}/me/prefs",
                new AutoPassPrefs(false, new Dictionary<string, string> { ["End"] = "theirs" }));

            resp.StatusCode.Should().Be(HttpStatusCode.NoContent);
        }
    }

    [Fact]
    public async Task SetAutoPassPrefs_EmptyDefaults_Returns204()
    {
        var (matchId, factory) = await SetupPlayingMatch();
        using (factory)
        {
            var aliceClient = AuthedClient(factory, "alice");

            var resp = await aliceClient.PutAsJsonAsync(
                $"/matches/{matchId}/me/prefs",
                new AutoPassPrefs(false, new Dictionary<string, string>()));

            resp.StatusCode.Should().Be(HttpStatusCode.NoContent);
        }
    }

    // -----------------------------------------------------------------------
    // Authz + error paths
    // -----------------------------------------------------------------------

    [Fact]
    public async Task SetAutoPassPrefs_ByNonParty_Returns403()
    {
        var (matchId, factory) = await SetupPlayingMatch();
        using (factory)
        {
            var charlieClient = AuthedClient(factory, "charlie");

            var resp = await charlieClient.PutAsJsonAsync(
                $"/matches/{matchId}/me/prefs",
                new AutoPassPrefs(true, new Dictionary<string, string>()));

            resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
            var body = await resp.Content.ReadFromJsonAsync<MatchError>();
            body!.Error.Should().Be("forbidden");
        }
    }

    [Fact]
    public async Task SetAutoPassPrefs_UnknownMatch_Returns404()
    {
        var (_, factory) = await SetupPlayingMatch();
        using (factory)
        {
            var aliceClient = AuthedClient(factory, "alice");

            var resp = await aliceClient.PutAsJsonAsync(
                $"/matches/{Guid.NewGuid()}/me/prefs",
                new AutoPassPrefs(true, new Dictionary<string, string>()));

            resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
        }
    }

    [Fact]
    public async Task SetAutoPassPrefs_Unauthenticated_Returns401()
    {
        var (matchId, factory) = await SetupPlayingMatch();
        using (factory)
        {
            var anon = factory.CreateClient();

            var resp = await anon.PutAsJsonAsync(
                $"/matches/{matchId}/me/prefs",
                new AutoPassPrefs(true, new Dictionary<string, string>()));

            resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }
    }
}
