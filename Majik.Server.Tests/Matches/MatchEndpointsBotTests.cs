using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Majik.Bot.Decks;
using Majik.Core.Api.Dtos;
using Majik.Core.CardData;
using Majik.Core.CardData.Database;
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
/// Integration tests for the vs-Bot branch of POST /matches. Mirrors the
/// fixture pattern used by <see cref="MatchEndpointsLifecycleTests"/>:
/// real EphemeralMongo, stub auth, fake card repo seeded with the cards
/// our bot archetypes use.
/// </summary>
public class MatchEndpointsBotTests : IClassFixture<TestMongoFixture>
{
    private readonly TestMongoFixture _fixture;

    public MatchEndpointsBotTests(TestMongoFixture fixture) => _fixture = fixture;

    // -----------------------------------------------------------------------
    // Factory helpers — mirror MatchEndpointsLifecycleTests
    // -----------------------------------------------------------------------

    private WebApplicationFactory<Program> Factory(IMongoDatabase db) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
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

                services.RemoveAll<ICardRepository>();
                services.AddSingleton<ICardRepository>(BotTestCardRepo());
            });
        });

    /// <summary>Card repo pre-loaded with every card that appears in any
    /// archetype shipped by <see cref="BotDeckCatalog"/>, plus the basic-deck
    /// cards used to seed the human creator's deck. The bot-archetype union
    /// is derived from <see cref="BotDeckCatalog"/> at fixture build so deck
    /// list updates don't silently regress this test — adding a card to a
    /// deck file automatically seeds it here.</summary>
    private static ICardRepository BotTestCardRepo()
    {
        var repo = new FakeCardRepoForMatchTests();
        // Human-side deck cards (reused from existing match-endpoint tests).
        repo.Add("Forest", "Basic Land — Forest");
        repo.Add("Mountain", "Basic Land — Mountain");
        repo.Add("Grizzly Bears", "Creature — Bear");
        repo.Add("Hill Giant", "Creature — Giant");
        foreach (var archetype in BotDeckCatalog.Archetypes)
        {
            foreach (var card in BotDeckCatalog.Get(archetype)
                .Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (repo.GetByName(card) != null) continue;
                // Type-line is opaque to RealDeckLoader's lookup path: it
                // only checks GetByName presence. A placeholder keeps the
                // fake repo from synthesizing wrong gameplay shape; real
                // card data lives in cards.db for production.
                repo.Add(card, "Card");
            }
        }
        return repo;
    }

    private static async Task<Guid> SeedDeckAsync(IMongoDatabase db, string ownerSub, string name, CancellationToken ct = default)
    {
        var deckRepo = new DeckRepository(db);
        await deckRepo.EnsureIndexesAsync(ct);
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
        }, ct);
        return id;
    }

    private static HttpClient Authed(WebApplicationFactory<Program> factory, string sub)
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

    // -----------------------------------------------------------------------
    // Happy path: bot opponent is synthesized in one POST
    // -----------------------------------------------------------------------

    [Fact]
    public async Task CreateMatch_WithBotOpponent_SkipsRollAndPopulatesBotSeat()
    {
        var db = await FreshDb();
        await SeedProfile(db, "alice", "Alice");
        var aliceDeckId = await SeedDeckAsync(db, "alice", "Alice Deck");
        using var factory = Factory(db);
        var client = Authed(factory, "alice");

        var resp = await client.PostAsJsonAsync("/matches", new
        {
            format = "constructed",
            visibility = "invite",
            deckId = aliceDeckId.ToString(),
            clockMinutes = 20,
            botOpponent = new { archetype = "Burn" },
        });

        resp.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await resp.Content.ReadFromJsonAsync<MatchDto>();
        body.Should().NotBeNull();
        body!.Opponent.Should().NotBeNull();
        body.Opponent!.Sub.Should().StartWith("bot:");
        body.Opponent.DeckId.Should().Be("bot:Burn");
        // vs-Bot skips Rolling and lands directly in Playing.
        body.State.Should().Be("Playing");
        body.Roll.Should().BeNull();
        // Invite is forced — bot matches must not surface in the public lobby.
        body.Visibility.Should().Be("Invite");
        body.GameId.Should().NotBeNull();
    }

    // -----------------------------------------------------------------------
    // GetState after bot match create returns 200 (regression guard)
    //
    // PR #168 switched GetGameStateAsync to facade.GetStateFor(viewer) for
    // CR 706 masking, and the new null branches return 409 "game-not-started".
    // The bot-match end-to-end flow tripped one of those branches in prod
    // (portal rendered "No game state.") even though the match document was
    // already in state=Playing with a non-null GameId. This test pins the
    // contract that immediately after POST /matches with a bot opponent,
    // GET /matches/{id}/state returns 200 with a populated GameStateDto.
    // -----------------------------------------------------------------------

    [Fact]
    public async Task GetState_AfterBotMatchCreate_Returns200WithPopulatedDto()
    {
        var db = await FreshDb();
        await SeedProfile(db, "alice", "Alice");
        var aliceDeckId = await SeedDeckAsync(db, "alice", "Alice Deck");
        using var factory = Factory(db);
        var client = Authed(factory, "alice");

        var createResp = await client.PostAsJsonAsync("/matches", new
        {
            format = "constructed",
            visibility = "invite",
            deckId = aliceDeckId.ToString(),
            clockMinutes = 20,
            botOpponent = new { archetype = "Burn" },
        });
        createResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var match = await createResp.Content.ReadFromJsonAsync<MatchDto>();
        match.Should().NotBeNull();
        match!.GameId.Should().NotBeNull();

        // GET /state must succeed — this is the call the portal makes
        // immediately after match creation. Failing here is what produced
        // the "No game state." regression.
        var stateResp = await client.GetAsync($"/matches/{match.Id}/state");
        stateResp.StatusCode.Should().Be(HttpStatusCode.OK,
            "bot match landed in Playing with a live facade — /state must " +
            "return the snapshot, not 409 game-not-started");

        var state = await stateResp.Content.ReadFromJsonAsync<GameStateDto>();
        state.Should().NotBeNull();
        state!.Players.Should().HaveCount(2);
    }

    // -----------------------------------------------------------------------
    // Validation: unknown archetype → 400
    // -----------------------------------------------------------------------

    [Fact]
    public async Task CreateMatch_WithUnknownArchetype_Returns400()
    {
        var db = await FreshDb();
        await SeedProfile(db, "alice", "Alice");
        var aliceDeckId = await SeedDeckAsync(db, "alice", "Alice Deck");
        using var factory = Factory(db);
        var client = Authed(factory, "alice");

        var resp = await client.PostAsJsonAsync("/matches", new
        {
            format = "constructed",
            visibility = "invite",
            deckId = aliceDeckId.ToString(),
            clockMinutes = 20,
            botOpponent = new { archetype = "NotAnArchetype" },
        });

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var err = await resp.Content.ReadFromJsonAsync<MatchError>();
        err!.Error.Should().Be("invalid-request");
    }

    // -----------------------------------------------------------------------
    // Lobby leak guard: a bot match must NOT appear in the public list
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ListMatches_ExcludesBotMatches()
    {
        var db = await FreshDb();
        await SeedProfile(db, "alice", "Alice");
        var aliceDeckId = await SeedDeckAsync(db, "alice", "Alice Deck");
        using var factory = Factory(db);
        var client = Authed(factory, "alice");

        var created = await client.PostAsJsonAsync("/matches", new
        {
            format = "constructed",
            visibility = "invite",
            deckId = aliceDeckId.ToString(),
            clockMinutes = 20,
            botOpponent = new { archetype = "Burn" },
        });
        created.StatusCode.Should().Be(HttpStatusCode.Created);

        var listResp = await client.GetAsync("/matches?visibility=public");
        listResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var list = await listResp.Content.ReadFromJsonAsync<MatchDto[]>();
        list.Should().NotBeNull();
        list!.Should().BeEmpty("vs-Bot matches are Invite-scoped and never enter the lobby");
    }
}
