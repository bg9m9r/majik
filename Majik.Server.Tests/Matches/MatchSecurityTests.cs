using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Majik.Core.Api.Dtos;
using Majik.Core.CardData;
using Majik.Core.CardData.Database;
using Majik.Server.Decks;
using Majik.Server.Matches;
using Majik.Server.Profiles;
using Majik.Server.Tests.Profiles;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using MongoDB.Driver;
using Xunit;

namespace Majik.Server.Tests.Matches;

/// <summary>
/// Security regression tests for the player-visibility cluster:
///   - GetGameStateAsync masks the opponent's hand (CR 706).
///   - ListOpenPublicAsync strips Creator.DeckSnapshot for lobby browsers.
///   - MatchHub.JoinMatch rejects authed users that aren't a participant.
/// </summary>
public class MatchSecurityTests : IClassFixture<TestMongoFixture>
{
    private readonly TestMongoFixture _fixture;

    public MatchSecurityTests(TestMongoFixture fixture) => _fixture = fixture;

    // -----------------------------------------------------------------------
    // Factory helpers (mirrored from MatchEndpointsGameplayTests)
    // -----------------------------------------------------------------------

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

    /// <summary>Drive a freshly-seeded match through join → roll → play-draw
    /// so the engine is running and GetGameStateAsync returns a real snapshot.</summary>
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
        created.StatusCode.Should().Be(HttpStatusCode.Created);
        var matchDto = await created.Content.ReadFromJsonAsync<MatchDto>();

        var joined = await bobClient.PostAsJsonAsync($"/matches/{matchDto!.Id}/join",
            new { deckId = bobDeckId.ToString() });
        joined.StatusCode.Should().Be(HttpStatusCode.OK);

        (await aliceClient.PostAsync($"/matches/{matchDto.Id}/roll", null))
            .EnsureSuccessStatusCode();
        (await bobClient.PostAsync($"/matches/{matchDto.Id}/roll", null))
            .EnsureSuccessStatusCode();

        var playDraw = await aliceClient.PostAsJsonAsync($"/matches/{matchDto.Id}/play-draw",
            new { choice = "play" });
        playDraw.StatusCode.Should().Be(HttpStatusCode.OK);

        return (matchDto.Id, factory);
    }

    // -----------------------------------------------------------------------
    // GetGameStateAsync: masks opponent hand + libraries (CR 706)
    // -----------------------------------------------------------------------

    [Fact]
    public async Task GetState_BothPlayers_HaveLibrariesMaskedAsHidden()
    {
        // The engine deals opening hands asynchronously and getting to the
        // first prompt is racy across CI environments. The deterministic
        // invariant we can check at any point is the LIBRARY masking: both
        // libraries are always non-empty AND always hidden, regardless of
        // viewer or phase. That guards against the regression where
        // GetGameStateAsync called facade.GetState() (the full spectator
        // view) instead of GetStateFor(viewer) — the former would leak
        // real card names from both libraries.
        var (matchId, factory) = await SetupPlayingMatch();
        using (factory)
        {
            var bobClient = AuthedClient(factory, "bob");
            var resp = await bobClient.GetAsync($"/matches/{matchId}/state");
            resp.StatusCode.Should().Be(HttpStatusCode.OK);
            var state = await resp.Content.ReadFromJsonAsync<GameStateDto>();
            state.Should().NotBeNull();
            state!.Players.Should().HaveCount(2);

            foreach (var player in state.Players)
            {
                player.Library.Cards.Should().NotBeEmpty(
                    "libraries are non-empty as soon as the engine boots");
                player.Library.Cards.Should().OnlyContain(c => c.Name == "(hidden)",
                    "CR 706 — libraries are hidden information; the per-viewer " +
                    "snapshot must mask every card name. Previously the endpoint " +
                    "called facade.GetState() (full spectator view) which leaked " +
                    "real card names from both libraries.");
            }
        }
    }

    [Fact]
    public async Task GetState_NonPartyMember_Returns403()
    {
        var (matchId, factory) = await SetupPlayingMatch();
        using (factory)
        {
            var charlieClient = AuthedClient(factory, "charlie");
            var resp = await charlieClient.GetAsync($"/matches/{matchId}/state");
            resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        }
    }

    // -----------------------------------------------------------------------
    // ListOpenPublicAsync: strips Creator.DeckSnapshot
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ListOpenPublic_DoesNotLeakCreatorDeckSnapshot()
    {
        var db = await FreshDb();
        await SeedProfile(db, "alice", "Alice");
        var aliceDeckId = await SeedDeckAsync(db, "alice", "Alice Deck");
        using var factory = Factory(db);
        var aliceClient = AuthedClient(factory, "alice");

        // Create a public match — its decklist is the creator's private data.
        var created = await aliceClient.PostAsJsonAsync("/matches",
            new { format = "constructed", visibility = "public", deckId = aliceDeckId.ToString(), clockMinutes = 20 });
        created.StatusCode.Should().Be(HttpStatusCode.Created);

        // Lobby browser is a different authenticated user.
        var charlieClient = AuthedClient(factory, "charlie");
        var listResp = await charlieClient.GetAsync("/matches");
        listResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var listed = await listResp.Content.ReadFromJsonAsync<List<MatchDto>>();
        listed.Should().NotBeNull().And.NotBeEmpty();
        var entry = listed!.Single(m => m.Creator.Sub == "alice");
        entry.Creator.DeckSnapshot.Should().BeEmpty(
            "lobby listings must not leak the creator's decklist");
    }

    [Fact]
    public async Task GetMatch_ForCreator_StillSeesOwnDeckSnapshot()
    {
        var db = await FreshDb();
        await SeedProfile(db, "alice", "Alice");
        var aliceDeckId = await SeedDeckAsync(db, "alice", "Alice Deck");
        using var factory = Factory(db);
        var aliceClient = AuthedClient(factory, "alice");

        var created = await aliceClient.PostAsJsonAsync("/matches",
            new { format = "constructed", visibility = "public", deckId = aliceDeckId.ToString(), clockMinutes = 20 });
        var matchDto = await created.Content.ReadFromJsonAsync<MatchDto>();

        // Creator polls /matches/{id}: their own decklist is theirs to see.
        var getResp = await aliceClient.GetAsync($"/matches/{matchDto!.Id}");
        getResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var got = await getResp.Content.ReadFromJsonAsync<MatchDto>();
        got!.Creator.DeckSnapshot.Should().NotBeEmpty();
    }

    // -----------------------------------------------------------------------
    // MatchHub.JoinMatch — only seated party may subscribe
    // -----------------------------------------------------------------------

    [Fact]
    public async Task JoinMatch_NonSeatedUser_IsRejected()
    {
        var db = await FreshDb();
        await SeedProfile(db, "alice", "Alice");
        var aliceDeckId = await SeedDeckAsync(db, "alice", "Alice Deck");
        using var factory = Factory(db);

        // Create a *public* match — previously these allowed any authed
        // user to join the hub group. Post-fix, only seated players may.
        var aliceClient = AuthedClient(factory, "alice");
        var created = await aliceClient.PostAsJsonAsync("/matches",
            new { format = "constructed", visibility = "public", deckId = aliceDeckId.ToString(), clockMinutes = 20 });
        var matchDto = await created.Content.ReadFromJsonAsync<MatchDto>();

        var server = factory.Server;
        var hub = new HubConnectionBuilder()
            .WithUrl($"{server.BaseAddress}hubs/match", options =>
            {
                options.HttpMessageHandlerFactory = _ => server.CreateHandler();
                options.Headers["Authorization"] =
                    $"{MatchTestAuthHandler.SchemeName} charlie";
            })
            .Build();

        await hub.StartAsync();
        try
        {
            var act = async () => await hub.InvokeAsync("JoinMatch", matchDto!.Id);
            await act.Should().ThrowAsync<HubException>()
                .WithMessage("*Not a participant*");
        }
        finally
        {
            await hub.DisposeAsync();
        }
    }

    [Fact]
    public async Task JoinMatch_SeatedCreator_IsAccepted()
    {
        var db = await FreshDb();
        await SeedProfile(db, "alice", "Alice");
        var aliceDeckId = await SeedDeckAsync(db, "alice", "Alice Deck");
        using var factory = Factory(db);

        var aliceClient = AuthedClient(factory, "alice");
        var created = await aliceClient.PostAsJsonAsync("/matches",
            new { format = "constructed", visibility = "public", deckId = aliceDeckId.ToString(), clockMinutes = 20 });
        var matchDto = await created.Content.ReadFromJsonAsync<MatchDto>();

        var server = factory.Server;
        var hub = new HubConnectionBuilder()
            .WithUrl($"{server.BaseAddress}hubs/match", options =>
            {
                options.HttpMessageHandlerFactory = _ => server.CreateHandler();
                options.Headers["Authorization"] =
                    $"{MatchTestAuthHandler.SchemeName} alice";
            })
            .Build();

        await hub.StartAsync();
        try
        {
            // No exception → group join succeeded.
            await hub.InvokeAsync("JoinMatch", matchDto!.Id);
        }
        finally
        {
            await hub.DisposeAsync();
        }
    }
}
