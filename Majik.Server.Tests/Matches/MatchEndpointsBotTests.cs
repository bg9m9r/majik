using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Majik.Bot.Decks;
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

    private WebApplicationFactory<Program> Factory(IMongoDatabase db, IRandomSource? rng = null) =>
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

                if (rng != null)
                {
                    services.RemoveAll<IRandomSource>();
                    services.AddSingleton<IRandomSource>(rng);
                }

                // Swap the production bot scheduler (real wall-clock delays)
                // for a synchronous one so the bot's roll + play/draw land
                // inside the same request that triggered them. Without this,
                // tests would have to poll for state transitions.
                services.RemoveAll<IBotMatchScheduler>();
                services.AddSingleton<IBotMatchScheduler>(sp =>
                    new SynchronousBotMatchScheduler(sp));
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
    public async Task CreateMatch_WithBotOpponent_EntersRollingWithBotSeatPopulated()
    {
        var db = await FreshDb();
        await SeedProfile(db, "alice", "Alice");
        var aliceDeckId = await SeedDeckAsync(db, "alice", "Alice Deck");
        // Deterministic RNG: bot rolls 6 (first call, fired by scheduler
        // synchronously during POST /matches), alice would roll 1 if she
        // posts /roll. We only assert the bot's slot here.
        var rng = new StubRandomSource(new Queue<int>(new[] { 6, 1 }));
        using var factory = Factory(db, rng);
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
        // Bot matches now flow through Rolling like human matches — the
        // SynchronousBotMatchScheduler has already submitted the bot's
        // dice roll inline, but alice hasn't rolled yet so the match is
        // sitting in Rolling with the bot's slot filled and no winner.
        body.State.Should().Be("Rolling");
        body.Roll.Should().NotBeNull();
        body.Roll!.OpponentRoll.Should().Be(6, "bot's roll was submitted synchronously by the scheduler");
        body.Roll.CreatorRoll.Should().BeNull("alice hasn't rolled yet");
        body.Roll.WinnerSub.Should().BeNull();
        // Invite is forced — bot matches must not surface in the public lobby.
        body.Visibility.Should().Be("Invite");
        body.GameId.Should().NotBeNull();
    }

    [Fact]
    public async Task BotMatch_BotWinsRoll_AutoChoosesPlayAndEntersPlaying()
    {
        var db = await FreshDb();
        await SeedProfile(db, "alice", "Alice");
        var aliceDeckId = await SeedDeckAsync(db, "alice", "Alice Deck");
        // RNG order matches SubmitRollAsync call order:
        //   1. Bot scheduler fires SubmitRollAsync(bot) first → bot rolls 6.
        //   2. Alice POSTs /roll → she rolls 1.
        // Bot wins, scheduler synchronously calls PlayDraw("play") →
        // match transitions to Playing.
        var rng = new StubRandomSource(new Queue<int>(new[] { 6, 1 }));
        using var factory = Factory(db, rng);
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
        var match = await created.Content.ReadFromJsonAsync<MatchDto>();
        match!.State.Should().Be("Rolling");

        // Alice rolls — at this point both slots are filled. Bot wins
        // (6 > 1), so the scheduler immediately calls PlayDraw("play")
        // on the bot's behalf, transitioning the match into Playing
        // before this call returns.
        var rollResp = await client.PostAsync($"/matches/{match.Id}/roll", null);
        rollResp.StatusCode.Should().Be(HttpStatusCode.OK);

        var afterResp = await client.GetAsync($"/matches/{match.Id}");
        afterResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var after = await afterResp.Content.ReadFromJsonAsync<MatchDto>();
        after!.State.Should().Be("Playing",
            "bot won the roll and the scheduler auto-chose play, " +
            "transitioning the match to Playing inline");
        after.FirstChoice.Should().Be("play");
        after.Roll!.WinnerSub.Should().StartWith("bot:");
    }

    [Fact]
    public async Task BotMatch_HumanWinsRoll_MatchSitsInRollingUntilHumanChooses()
    {
        var db = await FreshDb();
        await SeedProfile(db, "alice", "Alice");
        var aliceDeckId = await SeedDeckAsync(db, "alice", "Alice Deck");
        // Bot rolls 1, alice rolls 6 → alice wins. Scheduler does NOT
        // schedule PlayDraw; match must stay in Rolling until alice
        // explicitly posts /play-draw.
        var rng = new StubRandomSource(new Queue<int>(new[] { 1, 6 }));
        using var factory = Factory(db, rng);
        var client = Authed(factory, "alice");

        var created = await client.PostAsJsonAsync("/matches", new
        {
            format = "constructed",
            visibility = "invite",
            deckId = aliceDeckId.ToString(),
            clockMinutes = 20,
            botOpponent = new { archetype = "Burn" },
        });
        var match = (await created.Content.ReadFromJsonAsync<MatchDto>())!;

        var rollResp = await client.PostAsync($"/matches/{match.Id}/roll", null);
        rollResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var afterRoll = await rollResp.Content.ReadFromJsonAsync<MatchDto>();
        afterRoll!.Roll!.WinnerSub.Should().Be("alice");
        afterRoll.State.Should().Be("Rolling",
            "human won the roll — match should wait for the human's play/draw choice");

        // Human posts /play-draw → match transitions to Playing.
        var pd = await client.PostAsJsonAsync($"/matches/{match.Id}/play-draw",
            new { choice = "play" });
        pd.StatusCode.Should().Be(HttpStatusCode.OK);
        var afterPd = await pd.Content.ReadFromJsonAsync<MatchDto>();
        afterPd!.State.Should().Be("Playing");
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
    public async Task GetState_AfterBotMatchEntersPlaying_Returns200WithPopulatedDto()
    {
        var db = await FreshDb();
        await SeedProfile(db, "alice", "Alice");
        var aliceDeckId = await SeedDeckAsync(db, "alice", "Alice Deck");
        // Bot wins the roll → scheduler auto-chooses play → Playing.
        var rng = new StubRandomSource(new Queue<int>(new[] { 6, 1 }));
        using var factory = Factory(db, rng);
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

        // Alice rolls → bot wins → scheduler triggers PlayDraw → Playing.
        await client.PostAsync($"/matches/{match.Id}/roll", null);

        // GET /state must succeed — this is the call the portal makes
        // once the match is Playing. Failing here is what produced the
        // "No game state." regression.
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
    // PlayerId stamping: portal posts a MulliganCommand WITHOUT a PlayerId
    // (the generated OpenAPI client marks the field optional and the portal's
    // command builders don't set it). MatchService must stamp the caller's
    // seat-derived PlayerId before handing the command to the facade, or
    // GameFacade.SubmitAsync throws "Unknown player {Guid.Empty}" and the
    // commands endpoint surfaces a 500 / failure.
    // -----------------------------------------------------------------------

    [Fact]
    public async Task SubmitCommand_MulliganWithoutPlayerId_StampsAndAdvances()
    {
        var db = await FreshDb();
        await SeedProfile(db, "alice", "Alice");
        var aliceDeckId = await SeedDeckAsync(db, "alice", "Alice Deck");
        // Bot wins the roll → match auto-advances to Playing.
        var rng = new StubRandomSource(new Queue<int>(new[] { 6, 1 }));
        using var factory = Factory(db, rng);
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
        match!.GameId.Should().NotBeNull();

        // Alice rolls → bot wins → scheduler triggers PlayDraw → Playing.
        await client.PostAsync($"/matches/{match.Id}/roll", null);

        // POST a MulliganCommand with NO PlayerId — exactly what the portal
        // sends. The wire payload only has the $type discriminator and the
        // Keep flag; PlayerId is omitted entirely.
        var resp = await client.PostAsJsonAsync(
            $"/matches/{match.Id}/commands",
            new System.Collections.Generic.Dictionary<string, object>
            {
                ["$type"] = "mulligan",
                ["keep"] = true,
            });

        resp.StatusCode.Should().Be(HttpStatusCode.NoContent,
            "MatchService must stamp PlayerId from the caller's seat before " +
            "dispatching to the facade — otherwise the engine sees Guid.Empty " +
            "and throws on player routing");

        // Match should still be Playing (or have progressed); the game state
        // endpoint must keep returning 200 to confirm the engine did not
        // crash out of the facade.
        var stateResp = await client.GetAsync($"/matches/{match.Id}/state");
        stateResp.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // -----------------------------------------------------------------------
    // Recently-added cards smoke test
    //
    // PR added catch(Exception) in CreateBotMatchAsync so a card-factory
    // binder NRE no longer escapes to the global UseExceptionHandler and
    // returns opaque `{"error":"internal"}`. This test exercises the
    // bot-match create path with a user deck containing a sampling of the
    // recently-added cards (Phantasmal Image, Living End, Wrenn and
    // Realmbreaker, Engineered Explosives, Cabal Ritual). If a binder /
    // factory crashes, the response should now be a 500 with
    // type/message in the body (visible in test output), NOT a hidden
    // engine fault.
    //
    // This test deliberately does NOT fix any binder bug it uncovers —
    // its purpose is to make a crash *visible*. If the test reveals a
    // real card-factory bug, it should be triaged in a follow-up PR.
    // -----------------------------------------------------------------------

    /// <summary>Card repo that mirrors BotTestCardRepo and additionally
    /// seeds the recently-added cards we want the bot-match smoke test to
    /// exercise.</summary>
    private static ICardRepository BotTestCardRepoWithRecentCards()
    {
        var repo = (FakeCardRepoForMatchTests)BotTestCardRepo();
        // Use realistic type lines so RealDeckLoader instantiates the
        // correct typed ICard shell; binders inspect entity.TypeLine /
        // ManaCost when binding abilities.
        repo.Add("Phantasmal Image", "Creature — Illusion");
        repo.Add("Living End", "Sorcery");
        repo.Add("Wrenn and Realmbreaker", "Legendary Planeswalker — Wrenn");
        repo.Add("Engineered Explosives", "Artifact");
        repo.Add("Cabal Ritual", "Instant");
        return repo;
    }

    private WebApplicationFactory<Program> FactoryWithRecentCards(IMongoDatabase db) =>
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
                services.AddSingleton<ICardRepository>(BotTestCardRepoWithRecentCards());

                services.RemoveAll<IBotMatchScheduler>();
                services.AddSingleton<IBotMatchScheduler>(sp =>
                    new SynchronousBotMatchScheduler(sp));
            });
        });

    private static async Task<Guid> SeedDeckWithRecentCardsAsync(
        IMongoDatabase db, string ownerSub, string name, CancellationToken ct = default)
    {
        var deckRepo = new DeckRepository(db);
        await deckRepo.EnsureIndexesAsync(ct);
        var id = Guid.NewGuid();
        await deckRepo.InsertAsync(new Deck
        {
            Id = id,
            OwnerSub = ownerSub,
            Name = name,
            // Sprinkle the recently-added cards through a 60-card deck. Counts
            // chosen to stay within deck-validation rules (≤4 of any non-basic);
            // basics fill the rest.
            Mainboard = new List<DeckCardEntry>
            {
                new() { Name = "Forest", Count = 18 },
                new() { Name = "Mountain", Count = 18 },
                new() { Name = "Grizzly Bears", Count = 4 },
                new() { Name = "Hill Giant", Count = 4 },
                new() { Name = "Phantasmal Image", Count = 4 },
                new() { Name = "Living End", Count = 2 },
                new() { Name = "Wrenn and Realmbreaker", Count = 2 },
                new() { Name = "Engineered Explosives", Count = 4 },
                new() { Name = "Cabal Ritual", Count = 4 },
            },
            Sideboard = new List<DeckCardEntry>(),
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        }, ct);
        return id;
    }

    [Fact]
    public async Task CreateMatch_WithBotOpponentAndRecentCardsInUserDeck_DoesNotReturnOpaqueInternal()
    {
        var db = await FreshDb();
        await SeedProfile(db, "alice", "Alice");
        var aliceDeckId = await SeedDeckWithRecentCardsAsync(db, "alice", "Alice Recent Cards");
        using var factory = FactoryWithRecentCards(db);
        var client = Authed(factory, "alice");

        var resp = await client.PostAsJsonAsync("/matches", new
        {
            format = "constructed",
            visibility = "invite",
            deckId = aliceDeckId.ToString(),
            clockMinutes = 20,
            botOpponent = new { archetype = "Burn" },
        });

        // If the call succeeded, the binder pipeline is happy for these
        // cards — match should be in Rolling with a live facade (the
        // engine is started later by PlayDrawAsync; the create path now
        // only goes Open → Joined → Starting → Rolling).
        if (resp.StatusCode == HttpStatusCode.Created)
        {
            var body = await resp.Content.ReadFromJsonAsync<MatchDto>();
            body.Should().NotBeNull();
            body!.State.Should().Be("Rolling");
            body.GameId.Should().NotBeNull();

            // Verify the match landed in Mongo.
            var matchRepo = new MatchRepository(db);
            var stored = await matchRepo.GetByIdAsync(body.Id, CancellationToken.None);
            stored.Should().NotBeNull();
            stored!.GameId.Should().NotBeNull();
            return;
        }

        // Otherwise the response MUST surface a structured MatchError with
        // a non-empty Detail (exception type + message), NOT a generic
        // 500 body from the global UseExceptionHandler. This is the
        // regression-prevention assertion for this PR.
        var errBody = await resp.Content.ReadAsStringAsync();
        resp.StatusCode.Should().Be(HttpStatusCode.InternalServerError,
            $"non-success must be 500 with surfaced detail. Body was: {errBody}");
        var err = System.Text.Json.JsonSerializer.Deserialize<MatchError>(errBody,
            new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        err.Should().NotBeNull($"500 must carry a MatchError JSON body. Raw: {errBody}");
        err!.Error.Should().Be("internal");
        err.Detail.Should().NotBeNullOrWhiteSpace(
            "the broadened catch(Exception) in CreateBotMatchAsync must surface " +
            "the exception type + message so the user can see WHICH card factory " +
            $"crashed — not the opaque global-handler body. Raw: {errBody}");
        // Detail should look like "<TypeName>: <message>" — at minimum it
        // must contain a colon and a non-whitespace type name. Failing here
        // means the catch fell through to the global handler.
        err.Detail!.Should().Contain(":",
            $"Detail should be `<ExceptionType>: <message>`. Raw: {errBody}");
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
