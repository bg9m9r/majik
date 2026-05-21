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
using Xunit;

// Alias so we share the same auth scheme as the other Decks endpoint tests.
using ProfileTestAuth = Majik.Server.Tests.Profiles.TestAuthHandler;

namespace Majik.Server.Tests.Decks;

/// <summary>
/// Integration tests for POST /decks/parse.
/// The endpoint requires auth but does NOT require Mongo — DeckTextParser
/// depends only on ICardRepository (SQLite), which we override with a fake.
/// </summary>
public class DeckParseEndpointTests : IClassFixture<TestMongoFixture>
{
    private readonly TestMongoFixture _fixture;

    public DeckParseEndpointTests(TestMongoFixture fixture) => _fixture = fixture;

    // -----------------------------------------------------------------------
    // Factory helpers
    // -----------------------------------------------------------------------

    /// <summary>
    /// Factory with TestAuth and a fake card repo seeded with "Forest".
    /// Mongo is wired up (required to start the app) but parse doesn't use it.
    /// </summary>
    private WebApplicationFactory<Program> Factory() =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Mongo:ConnectionString", _fixture.ConnectionString);
            builder.UseSetting("Mongo:Database", "parse-test-" + Guid.NewGuid().ToString("N"));
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

                // Override card repo: parse tests only need "Forest".
                services.RemoveAll<ICardRepository>();
                services.AddSingleton<ICardRepository>(BuildParseCardRepo());
            });
        });

    private static ICardRepository BuildParseCardRepo()
    {
        var repo = new FakeCardRepoForParseTests();
        repo.Add("Forest", "Basic Land — Forest");
        return repo;
    }

    /// <summary>Create an authenticated client; pass null sub for unauthenticated.</summary>
    private static HttpClient ClientFor(WebApplicationFactory<Program> factory, string? sub)
    {
        var client = factory.CreateClient();
        if (sub != null)
        {
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue(ProfileTestAuth.SchemeName, sub);
        }
        return client;
    }

    // -----------------------------------------------------------------------
    // Tests
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Parse_Authenticated_Returns200WithParsedEntries()
    {
        using var factory = Factory();
        var client = ClientFor(factory, "alice");

        var response = await client.PostAsJsonAsync("/decks/parse", new { text = "60 Forest" });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<ParseDeckResultDto>();
        body.Should().NotBeNull();
        body!.Mainboard.Should().HaveCount(1);
        body.Mainboard[0].Name.Should().Be("Forest");
        body.Mainboard[0].Count.Should().Be(60);
        body.Sideboard.Should().BeEmpty();
        body.Unknown.Should().BeEmpty();
    }

    [Fact]
    public async Task Parse_Unauthenticated_Returns401()
    {
        using var factory = Factory();
        var client = ClientFor(factory, sub: null);

        var response = await client.PostAsJsonAsync("/decks/parse", new { text = "60 Forest" });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Parse_EmptyText_Returns400EmptyText()
    {
        using var factory = Factory();
        var client = ClientFor(factory, "alice");

        var response = await client.PostAsJsonAsync("/decks/parse", new { text = "" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var raw = await response.Content.ReadAsStringAsync();
        raw.Should().Contain("empty-text");
    }

    [Fact]
    public async Task Parse_TooLarge_Returns400TooLarge()
    {
        using var factory = Factory();
        var client = ClientFor(factory, "alice");

        var text = new string('x', 100_001);
        var response = await client.PostAsJsonAsync("/decks/parse", new { text });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var raw = await response.Content.ReadAsStringAsync();
        raw.Should().Contain("too-large");
    }
}

// ---------------------------------------------------------------------------
// Fake card repository for parse endpoint integration tests
// ---------------------------------------------------------------------------

/// <summary>In-memory ICardRepository seeded with cards needed by parse tests.</summary>
internal sealed class FakeCardRepoForParseTests : ICardRepository
{
    private readonly Dictionary<string, CardEntity> _cards = new(StringComparer.OrdinalIgnoreCase);

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
    }

    public CardEntity? GetByName(string name) =>
        _cards.TryGetValue(name, out var c) ? c : null;

    public IReadOnlyList<CardEntity> GetByNames(IEnumerable<string> names) =>
        names.Select(n => GetByName(n)).OfType<CardEntity>().ToList();

    public bool IsImplemented(string name) => _cards.ContainsKey(name);

    public IReadOnlyList<CardEntity> Search(string? q, bool implementedOnly, int limit,
        IReadOnlyList<string>? colors = null, IReadOnlyList<string>? types = null, IReadOnlyList<int>? cmcBuckets = null)
        => throw new NotImplementedException();

    public void SetImplemented(string name, bool value) =>
        throw new NotImplementedException();
}
