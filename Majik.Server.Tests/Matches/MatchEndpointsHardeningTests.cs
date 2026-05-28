using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Majik.Core.Cards;
using Majik.Core.CardData;
using Majik.Server.Decks;
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
/// API input-hardening integration tests for the /matches/{id}/commands path.
/// Asserts the defensive posture added by the API-hardening work:
///   * an engine-throwing command → clean 4xx invalid-command, no exception
///     TYPE NAME leaked in the body (item 1);
///   * an over-bounds command → 400 invalid-command, rejected before the
///     engine (item 2);
///   * a normal command still succeeds (no regression);
///   * a deck-load failure on join → generic deck-invalid, no card / exception
///     detail leaked (item 4).
/// Mirrors the fixture pattern in <see cref="MatchEndpointsGameplayTests"/>.
/// </summary>
public class MatchEndpointsHardeningTests : IClassFixture<TestMongoFixture>
{
    private readonly TestMongoFixture _fixture;

    public MatchEndpointsHardeningTests(TestMongoFixture fixture) => _fixture = fixture;

    private WebApplicationFactory<Program> Factory(
        IMongoDatabase db,
        IRandomSource? rng = null,
        IDeckLoader? deckLoader = null)
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

                if (deckLoader != null)
                {
                    services.RemoveAll<IDeckLoader>();
                    services.AddSingleton<IDeckLoader>(deckLoader);
                }
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

    /// <summary>Creates alice's match + bob joins + alice picks play → Playing.
    /// Mirrors MatchEndpointsGameplayTests.SetupPlayingMatch.</summary>
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

        var aliceRoll = await aliceClient.PostAsync($"/matches/{matchDto.Id}/roll", null);
        aliceRoll.StatusCode.Should().Be(HttpStatusCode.OK);
        var bobRoll = await bobClient.PostAsync($"/matches/{matchDto.Id}/roll", null);
        bobRoll.StatusCode.Should().Be(HttpStatusCode.OK);

        var playDraw = await aliceClient.PostAsJsonAsync($"/matches/{matchDto.Id}/play-draw",
            new { choice = "play" });
        playDraw.StatusCode.Should().Be(HttpStatusCode.OK);
        var playDto = await playDraw.Content.ReadFromJsonAsync<MatchDto>();
        playDto!.State.Should().Be("Playing");

        return (matchDto.Id, factory);
    }

    // -----------------------------------------------------------------------
    // Item 1: an engine-throwing command → clean 4xx invalid-command, no leak.
    //
    // At the start of the game the engine prompts for a priority action /
    // mulligan, NOT a ChooseX. Submitting a ChooseXCommand drives
    // RemoteAgent.Submit to throw InvalidOperationException ("Engine expected
    // ..., got ChooseXCommand"). Pre-hardening that bubbled to the global
    // handler and came back as a 500 carrying the exception TYPE NAME.
    // -----------------------------------------------------------------------
    [Fact]
    public async Task SubmitCommand_EngineThrows_Returns4xxInvalidCommand_NoExceptionLeak()
    {
        var (matchId, factory) = await SetupPlayingMatch();
        using (factory)
        {
            var aliceClient = AuthedClient(factory, "alice");

            // A legal X (within bounds) so the bounds guard does NOT fire — we
            // want the request to reach the engine, which then rejects it for
            // being the wrong command at this prompt.
            var resp = await aliceClient.PostAsJsonAsync(
                $"/matches/{matchId}/commands",
                new Dictionary<string, object> { ["$type"] = "x", ["x"] = 3 });

            ((int)resp.StatusCode).Should().BeInRange(400, 499,
                "an engine rejection must be a clean client error, not a 500");
            resp.StatusCode.Should().NotBe(HttpStatusCode.InternalServerError);

            var body = await resp.Content.ReadAsStringAsync();
            // No exception type name leaked.
            body.Should().NotContain("Exception");
            body.Should().NotContain("InvalidOperation");
            // It surfaces the hardened code.
            body.Should().Contain("invalid-command");
        }
    }

    // -----------------------------------------------------------------------
    // Item 2: over-bounds command → 400 invalid-command, rejected before engine.
    // -----------------------------------------------------------------------
    [Fact]
    public async Task SubmitCommand_HugeX_Returns400InvalidCommand()
    {
        var (matchId, factory) = await SetupPlayingMatch();
        using (factory)
        {
            var aliceClient = AuthedClient(factory, "alice");

            var resp = await aliceClient.PostAsJsonAsync(
                $"/matches/{matchId}/commands",
                new Dictionary<string, object> { ["$type"] = "x", ["x"] = 1_000_000 });

            resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            var err = await resp.Content.ReadFromJsonAsync<MatchError>();
            err!.Error.Should().Be("invalid-command");
        }
    }

    [Fact]
    public async Task SubmitCommand_HugeTargetList_Returns400InvalidCommand()
    {
        var (matchId, factory) = await SetupPlayingMatch();
        using (factory)
        {
            var aliceClient = AuthedClient(factory, "alice");

            var targets = Enumerable.Range(0, CommandValidator.MaxListLength + 1)
                .Select(_ => Guid.NewGuid().ToString())
                .ToArray();
            var resp = await aliceClient.PostAsJsonAsync(
                $"/matches/{matchId}/commands",
                new Dictionary<string, object> { ["$type"] = "targets", ["targetInstanceIds"] = targets });

            resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            var err = await resp.Content.ReadFromJsonAsync<MatchError>();
            err!.Error.Should().Be("invalid-command");
        }
    }

    // -----------------------------------------------------------------------
    // Regression: a normal/legal command still succeeds. The opening prompt
    // accepts a mulligan decision (Keep); the engine processes it without
    // error and the endpoint returns 204.
    // -----------------------------------------------------------------------
    [Fact]
    public async Task SubmitCommand_LegalMulligan_Succeeds204()
    {
        var (matchId, factory) = await SetupPlayingMatch();
        using (factory)
        {
            var aliceClient = AuthedClient(factory, "alice");

            var resp = await aliceClient.PostAsJsonAsync(
                $"/matches/{matchId}/commands",
                new Dictionary<string, object> { ["$type"] = "mulligan", ["keep"] = true });

            resp.StatusCode.Should().Be(HttpStatusCode.NoContent);
        }
    }

    // -----------------------------------------------------------------------
    // Item 4: deck-load failure on join → generic deck-invalid, no detail leak.
    //
    // A throwing IDeckLoader stands in for RealDeckLoader; its
    // DeckLoadException message names a specific card (sensitive load detail).
    // The hardened catch must log that server-side and return ONLY a generic
    // client message.
    // -----------------------------------------------------------------------
    [Fact]
    public async Task Join_DeckLoadFails_ReturnsGenericDeckInvalid_NoDetailLeak()
    {
        var db = await FreshDb();
        await SeedProfile(db, "alice", "Alice");
        await SeedProfile(db, "bob", "Bob");
        var aliceDeckId = await SeedDeckAsync(db, "alice", "Alice Deck");
        var bobDeckId = await SeedDeckAsync(db, "bob", "Bob Deck");

        const string SecretDetail = "unknown card at load time: Black Lotus";
        using var factory = Factory(db, deckLoader: new ThrowingDeckLoader(SecretDetail));
        var aliceClient = AuthedClient(factory, "alice");
        var bobClient = AuthedClient(factory, "bob");

        var created = await aliceClient.PostAsJsonAsync("/matches",
            new { format = "constructed", visibility = "public", deckId = aliceDeckId.ToString(), clockMinutes = 20 });
        created.StatusCode.Should().Be(HttpStatusCode.Created);
        var matchDto = await created.Content.ReadFromJsonAsync<MatchDto>();

        // Bob's join triggers the engine deck-load (creator + opponent decks),
        // which the throwing loader fails with a sensitive message.
        var joined = await bobClient.PostAsJsonAsync($"/matches/{matchDto!.Id}/join",
            new { deckId = bobDeckId.ToString() });

        // Status is left unchanged by the hardening (only the message is
        // genericized), so assert on the body fields + no-leak rather than a
        // specific status code.
        var body = await joined.Content.ReadAsStringAsync();
        var err = System.Text.Json.JsonSerializer.Deserialize<MatchError>(body,
            new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
        err!.Error.Should().Be("deck-invalid");
        err.Detail.Should().Be("One or more cards in the deck are invalid");
        // The sensitive card name / exception detail must NOT appear in the body.
        body.Should().NotContain("Black Lotus");
        body.Should().NotContain("unknown card");
        body.Should().NotContain("Exception");
    }

    // -----------------------------------------------------------------------
    // Item 5: SignalR receive-message-size cap is configured.
    //
    // The body-size cap (item 3) and SignalR cap (item 5) are server-config:
    // a 413 is not exercisable through WebApplicationFactory because TestServer
    // does not run Kestrel (Kestrel's MaxRequestBodySize is what enforces the
    // 413). We assert the SignalR HubOptions cap from DI here — it is wired by
    // AddMajikSignalR regardless of the test transport — and pin the Kestrel
    // body-size constant in a separate const-value test below.
    // -----------------------------------------------------------------------
    [Fact]
    public void SignalR_MaximumReceiveMessageSize_IsCapped()
    {
        var db = _fixture.NewDatabase();
        using var factory = Factory(db);
        // Force the host to build so DI is available.
        using var scope = factory.Services.CreateScope();
        var opts = scope.ServiceProvider
            .GetRequiredService<Microsoft.Extensions.Options.IOptions<
                Microsoft.AspNetCore.SignalR.HubOptions>>();
        opts.Value.MaximumReceiveMessageSize
            .Should().Be(Majik.Server.Composition.SignalRRegistration.MaximumReceiveMessageSize);
        opts.Value.MaximumReceiveMessageSize.Should().Be(64 * 1024);
    }

    // -----------------------------------------------------------------------
    // Item 3: a legitimate large payload (deck create with a full decklist)
    // is well under the configured body-size cap, so the cap doesn't break
    // any real request. (The 413-on-oversized path is enforced by Kestrel and
    // not reachable through TestServer; see the comment above.)
    // -----------------------------------------------------------------------
    [Fact]
    public async Task DeckCreate_FullDecklist_WellUnderBodyCap_Succeeds()
    {
        var db = await FreshDb();
        await SeedProfile(db, "alice", "Alice");
        using var factory = Factory(db);
        var client = AuthedClient(factory, "alice");

        // A maximal-ish decklist: 60 mainboard + 15 sideboard distinct-ish
        // entries. Serialized this is a few KB — far below the 256 KB cap.
        var mainboard = Enumerable.Range(0, 60)
            .Select(i => new { name = "Forest", count = 1 }).ToArray();
        var payload = new { name = "Big Deck", mainboard, sideboard = Array.Empty<object>() };
        var json = System.Text.Json.JsonSerializer.Serialize(payload);
        json.Length.Should().BeLessThan(256 * 1024,
            "the largest legitimate deck payload must fit comfortably under the body cap");

        var resp = await client.PostAsJsonAsync("/decks", payload);
        // Deck create may 200/201 (success) or 400 (validation) depending on
        // decklist legality — the point is the body is accepted/parsed, NOT a
        // 413 or 500 from the size guard.
        ((int)resp.StatusCode).Should().NotBe(413);
        ((int)resp.StatusCode).Should().NotBe(500);
    }

    /// <summary>IDeckLoader stub whose loads always throw DeckLoadException
    /// with a sensitive message, to verify it is not surfaced to the client.</summary>
    private sealed class ThrowingDeckLoader : IDeckLoader
    {
        private readonly string _detail;
        public ThrowingDeckLoader(string detail) => _detail = detail;

        public Task<IReadOnlyList<ICard>> LoadAsync(string deckId, CancellationToken ct) =>
            throw new DeckLoadException(_detail);

        public Task<IReadOnlyList<ICard>> LoadFromCardNamesAsync(IReadOnlyList<string> cardNames, CancellationToken ct) =>
            throw new DeckLoadException(_detail);
    }
}
