using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.CardData.Database;
using Majik.Server.Decks;
using Majik.Server.Tests.Profiles;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using MongoDB.Driver;
using Xunit;

// Alias the auth handler from the Profiles sub-project so we share the same scheme.
using ProfileTestAuth = Majik.Server.Tests.Profiles.TestAuthHandler;

namespace Majik.Server.Tests.Decks;

/// <summary>
/// Integration tests for the /decks CRUD endpoints using a real
/// EphemeralMongo instance and the shared TestAuthHandler from sub-project #1.
/// </summary>
public class DeckEndpointsTests : IClassFixture<TestMongoFixture>
{
    private readonly TestMongoFixture _fixture;

    public DeckEndpointsTests(TestMongoFixture fixture) => _fixture = fixture;

    // -----------------------------------------------------------------------
    // Factory helpers
    // -----------------------------------------------------------------------

    private WebApplicationFactory<Program> Factory(IMongoDatabase db)
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
                    opts.DefaultAuthenticateScheme = ProfileTestAuth.SchemeName;
                    opts.DefaultChallengeScheme = ProfileTestAuth.SchemeName;
                });
                services.AddAuthentication(ProfileTestAuth.SchemeName)
                    .AddScheme<AuthenticationSchemeOptions, ProfileTestAuth>(
                        ProfileTestAuth.SchemeName, _ => { });

                // Override card repo with fake that knows the test cards.
                services.RemoveAll<ICardRepository>();
                services.AddSingleton<ICardRepository>(BuildTestCardRepo());
            });
        });
    }

    /// <summary>Separate factory with empty connection string — triggers 503 on every endpoint.</summary>
    private static WebApplicationFactory<Program> NoMongoFactory() =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Mongo:ConnectionString", "");
            builder.UseSetting("Mongo:Database", "");
            builder.ConfigureServices(services =>
            {
                services.RemoveAll(typeof(IHostedService));
                services.PostConfigure<AuthenticationOptions>(opts =>
                {
                    opts.DefaultAuthenticateScheme = ProfileTestAuth.SchemeName;
                    opts.DefaultChallengeScheme = ProfileTestAuth.SchemeName;
                });
                services.AddAuthentication(ProfileTestAuth.SchemeName)
                    .AddScheme<AuthenticationSchemeOptions, ProfileTestAuth>(
                        ProfileTestAuth.SchemeName, _ => { });

                services.RemoveAll<ICardRepository>();
                services.AddSingleton<ICardRepository>(BuildTestCardRepo());
            });
        });

    private static ICardRepository BuildTestCardRepo()
    {
        var repo = new FakeCardRepoForDeckTests();
        repo.Add("Forest", "Basic Land — Forest");
        repo.Add("Mountain", "Basic Land — Mountain");
        repo.Add("Grizzly Bears", "Creature — Bear");
        repo.Add("Hill Giant", "Creature — Giant");
        return repo;
    }

    /// <summary>Create an authenticated HTTP client. Pass null sub for unauthenticated.</summary>
    private HttpClient Authed(WebApplicationFactory<Program> f, string? sub)
    {
        var c = f.CreateClient();
        if (sub != null)
        {
            c.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue(ProfileTestAuth.SchemeName, sub);
        }
        return c;
    }

    private async Task<IMongoDatabase> FreshDb()
    {
        var db = _fixture.NewDatabase();
        await new DeckRepository(db).EnsureIndexesAsync(CancellationToken.None);
        return db;
    }

    // -----------------------------------------------------------------------
    // Valid deck body helper — 60-card deck from the seeded card pool
    // -----------------------------------------------------------------------

    /// <summary>
    /// 60-card legal deck: 52 basic lands + 4 Grizzly Bears + 4 Hill Giant.
    /// Basic lands have no copy limit; non-basics are exactly at the 4-of cap.
    /// </summary>
    private static CreateDeckRequest ValidDeckBody(string name = "My Deck") =>
        new(
            Name: name,
            Mainboard: new[]
            {
                new DeckCardEntryDto("Forest", 26),
                new DeckCardEntryDto("Mountain", 26),
                new DeckCardEntryDto("Grizzly Bears", 4),
                new DeckCardEntryDto("Hill Giant", 4),
            },
            Sideboard: Array.Empty<DeckCardEntryDto>());

    // -----------------------------------------------------------------------
    // Test 1: GET /decks — 401 unauthenticated
    // -----------------------------------------------------------------------

    [Fact]
    public async Task GetDecks_Unauth_Returns401()
    {
        var db = await FreshDb();
        using var factory = Factory(db);
        var resp = await Authed(factory, null).GetAsync("/decks");
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // -----------------------------------------------------------------------
    // Test 2: GET /decks — empty list when no decks for caller
    // -----------------------------------------------------------------------

    [Fact]
    public async Task GetDecks_NoMatchesForCaller_ReturnsEmptyArray()
    {
        var db = await FreshDb();
        using var factory = Factory(db);
        var resp = await Authed(factory, "alice").GetAsync("/decks");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<DeckDto[]>();
        body.Should().NotBeNull();
        body!.Should().BeEmpty();
    }

    // -----------------------------------------------------------------------
    // Test 3: POST /decks — valid → 201 with DeckDto
    // -----------------------------------------------------------------------

    [Fact]
    public async Task PostDecks_Valid_Returns201()
    {
        var db = await FreshDb();
        using var factory = Factory(db);
        var resp = await Authed(factory, "alice").PostAsJsonAsync("/decks", ValidDeckBody("Alpha Deck"));
        resp.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await resp.Content.ReadFromJsonAsync<DeckDto>();
        body.Should().NotBeNull();
        body!.Name.Should().Be("Alpha Deck");
        body.OwnerSub.Should().Be("alice");
        body.Id.Should().NotBe(Guid.Empty);
        body.Mainboard.Sum(e => e.Count).Should().Be(60);
        resp.Headers.Location.Should().NotBeNull();
        resp.Headers.Location!.ToString().Should().Contain(body.Id.ToString());
    }

    // -----------------------------------------------------------------------
    // Test 4: POST /decks — undersized mainboard → 400 with validation
    // -----------------------------------------------------------------------

    [Fact]
    public async Task PostDecks_Invalid_Returns400WithValidation()
    {
        var db = await FreshDb();
        using var factory = Factory(db);

        var req = new CreateDeckRequest(
            Name: "Too Small",
            Mainboard: new[] { new DeckCardEntryDto("Forest", 10) },
            Sideboard: Array.Empty<DeckCardEntryDto>());

        var resp = await Authed(factory, "alice").PostAsJsonAsync("/decks", req);
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await resp.Content.ReadFromJsonAsync<DeckError>();
        body.Should().NotBeNull();
        body!.Error.Should().Be("invalid-deck");
        body.Validation.Should().NotBeNullOrEmpty();
    }

    // -----------------------------------------------------------------------
    // Test 5: POST /decks — name collision → 409 name-taken
    // -----------------------------------------------------------------------

    [Fact]
    public async Task PostDecks_NameCollision_Returns409()
    {
        var db = await FreshDb();
        using var factory = Factory(db);
        var client = Authed(factory, "alice");

        var first = await client.PostAsJsonAsync("/decks", ValidDeckBody("Clash Deck"));
        first.StatusCode.Should().Be(HttpStatusCode.Created);

        var second = await client.PostAsJsonAsync("/decks", ValidDeckBody("Clash Deck"));
        second.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var body = await second.Content.ReadFromJsonAsync<DeckError>();
        body!.Error.Should().Be("name-taken");
    }

    // -----------------------------------------------------------------------
    // Test 6: POST /decks — cap reached → 409 deck-cap-reached
    //
    // Optimization: insert first 25 directly via DeckRepository.InsertAsync
    // to bypass validation overhead, then hit the endpoint for the 26th.
    // -----------------------------------------------------------------------

    [Fact]
    public async Task PostDecks_CapReached_Returns409()
    {
        var db = await FreshDb();
        var deckRepo = new DeckRepository(db);

        // Directly insert 25 decks to reach the cap without HTTP round-trips.
        var tasks = Enumerable.Range(1, 25).Select(i => deckRepo.InsertAsync(new Deck
        {
            Id = Guid.NewGuid(),
            OwnerSub = "alice",
            Name = $"Cap Deck {i}",
            Mainboard = new List<DeckCardEntry>
            {
                new() { Name = "Forest", Count = 60 },
            },
            Sideboard = new List<DeckCardEntry>(),
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        }, CancellationToken.None));
        await Task.WhenAll(tasks);

        using var factory = Factory(db);
        var resp = await Authed(factory, "alice").PostAsJsonAsync("/decks", ValidDeckBody("Overflow Deck"));
        resp.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var body = await resp.Content.ReadFromJsonAsync<DeckError>();
        body!.Error.Should().Be("deck-cap-reached");
    }

    // -----------------------------------------------------------------------
    // Test 7: GET /decks/{id} — other owner → 404 deck-not-found
    // -----------------------------------------------------------------------

    [Fact]
    public async Task GetDecksById_OtherOwner_Returns404()
    {
        var db = await FreshDb();
        using var factory = Factory(db);

        // Alice creates a deck.
        var created = await Authed(factory, "alice").PostAsJsonAsync("/decks", ValidDeckBody("Alice Private"));
        created.StatusCode.Should().Be(HttpStatusCode.Created);
        var dto = await created.Content.ReadFromJsonAsync<DeckDto>();

        // Bob tries to fetch it.
        var resp = await Authed(factory, "bob").GetAsync($"/decks/{dto!.Id}");
        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var body = await resp.Content.ReadFromJsonAsync<DeckError>();
        body!.Error.Should().Be("deck-not-found");
    }

    // -----------------------------------------------------------------------
    // Test 8: PUT /decks/{id} — owner match → 200 with updated DTO
    // -----------------------------------------------------------------------

    [Fact]
    public async Task PutDecks_OwnerMatch_Updates()
    {
        var db = await FreshDb();
        using var factory = Factory(db);
        var client = Authed(factory, "alice");

        var created = await client.PostAsJsonAsync("/decks", ValidDeckBody("Before Update"));
        created.StatusCode.Should().Be(HttpStatusCode.Created);
        var dto = await created.Content.ReadFromJsonAsync<DeckDto>();

        var updateReq = new UpdateDeckRequest(
            Name: "After Update",
            Mainboard: new[]
            {
                new DeckCardEntryDto("Forest", 52),
                new DeckCardEntryDto("Grizzly Bears", 4),
                new DeckCardEntryDto("Hill Giant", 4),
            },
            Sideboard: Array.Empty<DeckCardEntryDto>());

        var resp = await client.PutAsJsonAsync($"/decks/{dto!.Id}", updateReq);
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var updated = await resp.Content.ReadFromJsonAsync<DeckDto>();
        updated.Should().NotBeNull();
        updated!.Name.Should().Be("After Update");
        updated.Id.Should().Be(dto.Id);
    }

    // -----------------------------------------------------------------------
    // Test 9: PUT /decks/{id} — other owner → 404
    // -----------------------------------------------------------------------

    [Fact]
    public async Task PutDecks_OtherOwner_Returns404()
    {
        var db = await FreshDb();
        using var factory = Factory(db);

        var created = await Authed(factory, "alice").PostAsJsonAsync("/decks", ValidDeckBody("Alice Deck"));
        created.StatusCode.Should().Be(HttpStatusCode.Created);
        var dto = await created.Content.ReadFromJsonAsync<DeckDto>();

        var updateReq = new UpdateDeckRequest(
            Name: "Hijacked",
            Mainboard: new[] { new DeckCardEntryDto("Forest", 60) },
            Sideboard: Array.Empty<DeckCardEntryDto>());

        var resp = await Authed(factory, "bob").PutAsJsonAsync($"/decks/{dto!.Id}", updateReq);
        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var body = await resp.Content.ReadFromJsonAsync<DeckError>();
        body!.Error.Should().Be("deck-not-found");
    }

    // -----------------------------------------------------------------------
    // Test 10: DELETE /decks/{id} — owner match → 204
    // -----------------------------------------------------------------------

    [Fact]
    public async Task DeleteDecks_OwnerMatch_Returns204()
    {
        var db = await FreshDb();
        using var factory = Factory(db);
        var client = Authed(factory, "alice");

        var created = await client.PostAsJsonAsync("/decks", ValidDeckBody("Delete Me"));
        created.StatusCode.Should().Be(HttpStatusCode.Created);
        var dto = await created.Content.ReadFromJsonAsync<DeckDto>();

        var resp = await client.DeleteAsync($"/decks/{dto!.Id}");
        resp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Verify gone.
        var gone = await client.GetAsync($"/decks/{dto.Id}");
        gone.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // -----------------------------------------------------------------------
    // Test 11: DELETE /decks/{id} — other owner → 404
    // -----------------------------------------------------------------------

    [Fact]
    public async Task DeleteDecks_OtherOwner_Returns404()
    {
        var db = await FreshDb();
        using var factory = Factory(db);

        var created = await Authed(factory, "alice").PostAsJsonAsync("/decks", ValidDeckBody("Alice Deck"));
        created.StatusCode.Should().Be(HttpStatusCode.Created);
        var dto = await created.Content.ReadFromJsonAsync<DeckDto>();

        var resp = await Authed(factory, "bob").DeleteAsync($"/decks/{dto!.Id}");
        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var body = await resp.Content.ReadFromJsonAsync<DeckError>();
        body!.Error.Should().Be("deck-not-found");
    }

    // -----------------------------------------------------------------------
    // Test 12: 503 when Mongo is unconfigured
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Endpoints_503WhenMongoUnconfigured()
    {
        using var factory = NoMongoFactory();
        var client = Authed(factory, "alice");

        var resp = await client.GetAsync("/decks");
        resp.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        var body = await resp.Content.ReadFromJsonAsync<DeckError>();
        body!.Error.Should().Be("mongo-not-configured");
    }
}

// ---------------------------------------------------------------------------
// Fake card repository for deck endpoint integration tests
// ---------------------------------------------------------------------------

/// <summary>In-memory ICardRepository that knows the four standard test cards:
/// Forest, Mountain, Grizzly Bears, Hill Giant — all implemented.</summary>
internal sealed class FakeCardRepoForDeckTests : ICardRepository
{
    private readonly Dictionary<string, CardEntity> _cards = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _implemented = new(StringComparer.OrdinalIgnoreCase);

    public void Add(string name, string typeLine)
    {
        _cards[name] = new CardEntity
        {
            Name = name,
            ScryfallId = Guid.NewGuid().ToString(),
            ManaCost = "",
            TypeLine = typeLine,
            Set = "TST",
            CollectorNumber = "1",
            IsImplemented = true,
        };
        _implemented.Add(name);
    }

    public CardEntity? GetByName(string name) =>
        _cards.TryGetValue(name, out var c) ? c : null;

    public IReadOnlyList<CardEntity> GetByNames(IEnumerable<string> names) =>
        names.Select(n => GetByName(n)).OfType<CardEntity>().ToList();

    public bool IsImplemented(string name) => _implemented.Contains(name);

    public IReadOnlyList<CardEntity> Search(string? q, bool implementedOnly, int limit,
        IReadOnlyList<string>? colors = null, IReadOnlyList<string>? types = null, IReadOnlyList<int>? cmcBuckets = null)
        => throw new NotImplementedException();

    public void SetImplemented(string name, bool value) =>
        throw new NotImplementedException();
}
