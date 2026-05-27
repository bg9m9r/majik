using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Majik.Bot.Decks;
using Majik.Core.Api;
using Majik.Core.Api.Dtos;
using Majik.Core.CardData;
using Majik.Server.Composition;
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

namespace Majik.Server.Tests.Matches;

/// <summary>
/// Deterministic in-process bot-match runner used by the Robustness Slice
/// safety-net invariant tests. Stands up a real <see cref="Program"/> host
/// (EphemeralMongo, stub auth, seeded RNG, synchronous bot scheduler —
/// mirroring <see cref="MatchEndpointsBotTests"/> /
/// <see cref="MatchEndpointsGameplayTests"/>) and exposes the live
/// <see cref="GameFacade"/> together with every SignalR publish captured
/// off the host's <see cref="IMatchHubPublisher"/> (the
/// <see cref="CapturePublisher"/> fake from
/// <see cref="MatchServiceClockTests"/>, hoisted to the host scope so both
/// the bridge and MatchService funnel through it).
///
/// The bot wins the opening roll (rolls 6 vs the human's 1) so the
/// synchronous scheduler auto-chooses "play" and the match lands in
/// Playing inside the roll request — no polling needed. The human drives
/// the rest of the round via <see cref="AdvanceByPassAsync"/>, POSTing
/// PassPriorityCommands through the same /commands endpoint the gameplay
/// tests use.
/// </summary>
public sealed class LayerAgreementHarness : IDisposable
{
    public const string CreatorSubValue = "alice";

    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _creatorClient;

    public CapturePublisher Published { get; }
    public MatchFacadeBridge Bridge { get; }
    public Guid MatchId { get; }
    public Guid GameId { get; }
    public GameFacade Facade { get; }
    public string CreatorSub { get; }
    public string BotSub { get; }

    private LayerAgreementHarness(
        WebApplicationFactory<Program> factory,
        HttpClient creatorClient,
        CapturePublisher published,
        MatchFacadeBridge bridge,
        Guid matchId,
        Guid gameId,
        GameFacade facade,
        string creatorSub,
        string botSub)
    {
        _factory = factory;
        _creatorClient = creatorClient;
        Published = published;
        Bridge = bridge;
        MatchId = matchId;
        GameId = gameId;
        Facade = facade;
        CreatorSub = creatorSub;
        BotSub = botSub;
    }

    // -----------------------------------------------------------------------
    // Captured-publish hub fake. Mirrors CapturePublisher in
    // MatchServiceClockTests but lives at the host scope so EVERY publish
    // (MatchService clock updates + MatchFacadeBridge event/prompt fan-out)
    // is recorded in one place. PublishPerRecipient is captured as a Group
    // entry per recipient so prompt-routing tests can read playerId off the
    // payloads.
    // -----------------------------------------------------------------------

    public sealed class CapturePublisher : IMatchHubPublisher
    {
        private readonly object _gate = new();
        public List<(Guid matchId, string @event, object payload)> Published { get; } = new();

        public void Publish(Guid matchId, string @event, object payload)
        {
            lock (_gate) Published.Add((matchId, @event, payload));
        }

        public void PublishPerRecipient(
            Guid matchId,
            string @event,
            IReadOnlyList<string> recipientSubs,
            Func<string, object> payloadFor)
        {
            foreach (var sub in recipientSubs)
            {
                if (string.IsNullOrEmpty(sub)) continue;
                lock (_gate) Published.Add((matchId, @event, payloadFor(sub)));
            }
        }

        public IReadOnlyList<(Guid matchId, string @event, object payload)> Snapshot()
        {
            lock (_gate) return Published.ToList();
        }
    }

    // -----------------------------------------------------------------------
    // Card repo — union of bot archetype cards + the human deck cards.
    // Mirrors MatchEndpointsBotTests.BotTestCardRepo.
    // -----------------------------------------------------------------------

    private static ICardRepository BotTestCardRepo()
    {
        var repo = new FakeCardRepoForMatchTests();
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
                repo.Add(card, "Card");
            }
        }
        return repo;
    }

    // -----------------------------------------------------------------------
    // Startup
    // -----------------------------------------------------------------------

    /// <summary>
    /// Stand up a host, create a vs-Bot match, drive it to Playing, and
    /// return a harness wrapping the live facade + captured publishes.
    /// The bot wins the roll (6 vs 1) so the synchronous scheduler
    /// auto-plays — <paramref name="rngSeed"/> currently only documents
    /// intent (the dice outcome is fixed so the run is fully reproducible
    /// regardless of seed); it is threaded through so future variants can
    /// randomise the opening without changing call sites.
    /// </summary>
    public static async Task<LayerAgreementHarness> StartBotMatchAsync(
        TestMongoFixture fixture, int rngSeed)
    {
        const string creatorSub = CreatorSubValue;

        var db = fixture.NewDatabase();
        await new MatchRepository(db).EnsureIndexesAsync(CancellationToken.None);
        await new UserProfileRepository(db).EnsureIndexesAsync(CancellationToken.None);

        // Seed the creator profile + deck.
        var profiles = new UserProfileRepository(db);
        await profiles.UpsertAsync(new UserProfile
        {
            Sub = creatorSub,
            Handle = "alice",
            HandleDisplay = "Alice",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        }, CancellationToken.None);

        var deckRepo = new DeckRepository(db);
        await deckRepo.EnsureIndexesAsync(CancellationToken.None);
        var aliceDeckId = Guid.NewGuid();
        await deckRepo.InsertAsync(new Deck
        {
            Id = aliceDeckId,
            OwnerSub = creatorSub,
            Name = "Alice Deck",
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

        var published = new CapturePublisher();
        // Bot rolls first (6), human second (1) → bot wins → synchronous
        // scheduler auto-plays → Playing. Seed isn't consumed by the stub
        // queue, keeping the run deterministic.
        var rng = new StubRandomSource(new Queue<int>(new[] { 6, 1 }));

        var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Mongo:ConnectionString", fixture.ConnectionString);
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

                services.RemoveAll<IRandomSource>();
                services.AddSingleton<IRandomSource>(rng);

                // Capture every SignalR publish (clock updates from
                // MatchService AND event/prompt fan-out from the bridge).
                services.RemoveAll<IMatchHubPublisher>();
                services.AddSingleton<IMatchHubPublisher>(published);

                // Bot roll + play/draw land inside the request that
                // triggered them — no polling for state transitions.
                services.RemoveAll<IBotMatchScheduler>();
                services.AddSingleton<IBotMatchScheduler>(sp =>
                    new SynchronousBotMatchScheduler(sp));
            });
        });

        var creatorClient = factory.CreateClient();
        creatorClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue(MatchTestAuthHandler.SchemeName, creatorSub);

        var createResp = await creatorClient.PostAsJsonAsync("/matches", new
        {
            format = "constructed",
            visibility = "invite",
            deckId = aliceDeckId.ToString(),
            clockMinutes = 20,
            botOpponent = new { archetype = "Burn" },
        });
        if (createResp.StatusCode != HttpStatusCode.Created)
        {
            var body = await createResp.Content.ReadAsStringAsync();
            factory.Dispose();
            throw new InvalidOperationException(
                $"Bot-match create failed: {(int)createResp.StatusCode} {body}");
        }
        var created = (await createResp.Content.ReadFromJsonAsync<MatchDto>())!;

        // Human rolls → bot wins → scheduler auto-plays → Playing.
        var rollResp = await creatorClient.PostAsync($"/matches/{created.Id}/roll", null);
        if (rollResp.StatusCode != HttpStatusCode.OK)
        {
            var body = await rollResp.Content.ReadAsStringAsync();
            factory.Dispose();
            throw new InvalidOperationException(
                $"Roll failed: {(int)rollResp.StatusCode} {body}");
        }

        var matchRepo = new MatchRepository(db);
        var fresh = (await matchRepo.GetByIdAsync(created.Id, CancellationToken.None))!;
        var gameId = fresh.GameId
            ?? throw new InvalidOperationException("Match has no GameId after roll.");
        var botSub = fresh.Opponent?.Sub
            ?? throw new InvalidOperationException("Match has no bot opponent after roll.");

        var gameFactory = factory.Services.GetRequiredService<ServerGameFactory>();
        var facade = gameFactory.Get(gameId)
            ?? throw new InvalidOperationException(
                $"ServerGameFactory has no facade for GameId={gameId}.");
        var bridge = factory.Services.GetRequiredService<MatchFacadeBridge>();

        var harness = new LayerAgreementHarness(
            factory, creatorClient, published, bridge, created.Id, gameId, facade, creatorSub, botSub);

        // Clear the opening-hand mulligan gate so subsequent
        // AdvanceByPassAsync calls actually walk the turn through phases.
        // The bot seat keeps on its own (in-process agent); the human seat
        // keeps via a MulliganCommand on the /commands endpoint. CR 103.4.
        await harness.KeepOpeningHandAsync();

        return harness;
    }

    // -----------------------------------------------------------------------
    // Drive
    // -----------------------------------------------------------------------

    /// <summary>Keep the creator's opening hand (MulliganCommand keep=true)
    /// so the engine leaves the CR 103.4 mulligan gate and starts turn 1.
    /// The bot seat keeps on its own. Idempotent / tolerant: a non-success
    /// just means the engine wasn't awaiting a mulligan from this seat.</summary>
    public async Task KeepOpeningHandAsync()
    {
        var resp = await _creatorClient.PostAsJsonAsync(
            $"/matches/{MatchId}/commands",
            new Dictionary<string, object> { ["$type"] = "mulligan", ["keep"] = true });
        _ = resp;
    }

    /// <summary>POST a PassPriorityCommand for <paramref name="sub"/> via the
    /// /commands endpoint (the same path the gameplay tests use). The
    /// polymorphic $type discriminator selects PassPriorityCommand and the
    /// caller's seat-derived PlayerId is stamped server-side.</summary>
    public async Task AdvanceByPassAsync(string sub)
    {
        var client = sub == CreatorSub ? _creatorClient : ClientFor(sub);
        var resp = await client.PostAsJsonAsync(
            $"/matches/{MatchId}/commands",
            new Dictionary<string, object> { ["$type"] = "pass" });
        // No-content on accept; a non-success here is tolerated (the engine
        // may have moved past the point where this seat holds priority).
        _ = resp;
    }

    private HttpClient ClientFor(string sub)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue(MatchTestAuthHandler.SchemeName, sub);
        return client;
    }

    /// <summary>The holder sub on the most recent captured
    /// <c>match.clock-update</c>, or null if none captured yet. Read off the
    /// anonymous payload via reflection.</summary>
    public string? LatestClockHolderSub()
    {
        foreach (var entry in Published.Snapshot().AsEnumerable().Reverse())
        {
            if (entry.@event != "match.clock-update") continue;
            return PayloadReflection.GetString(entry.payload, "holder");
        }
        return null;
    }

    /// <summary>GET /matches/{MatchId}/state as <paramref name="sub"/> and
    /// deserialise the response body as <see cref="GameStateDto"/>. Mirrors
    /// how <see cref="MatchEndpointsGameplayTests"/> GETs state with the
    /// seated auth header, but scoped to this harness's MatchId.</summary>
    public async Task<GameStateDto?> GetStateAsync(string sub)
    {
        var client = sub == CreatorSub ? _creatorClient : ClientFor(sub);
        var resp = await client.GetAsync($"/matches/{MatchId}/state");
        if (!resp.IsSuccessStatusCode) return null;
        return await resp.Content.ReadFromJsonAsync<GameStateDto>();
    }

    public void Dispose()
    {
        _creatorClient.Dispose();
        _factory.Dispose();
    }
}

/// <summary>Reads string-valued members off the anonymous payload objects
/// the publisher captures. The wire payloads are anonymous types, so tests
/// can't see their compile-time shape — reflection is the only seam.</summary>
internal static class PayloadReflection
{
    public static string? GetString(object payload, string member)
    {
        var value = GetMember(payload, member);
        return value?.ToString();
    }

    public static object? GetMember(object payload, string member)
    {
        var prop = payload.GetType().GetProperty(member);
        return prop?.GetValue(payload);
    }
}
