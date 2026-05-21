using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.CardData.Database;
using Majik.Server.Cards;
using Microsoft.AspNetCore.Authentication;
using ProfileTestAuth = Majik.Server.Tests.Profiles.TestAuthHandler;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Majik.Server.Tests.Cards;

public class CardsEndpointsTests : IDisposable
{
    private readonly SqliteConnection _conn;
    private readonly CardDbContext _db;

    public CardsEndpointsTests()
    {
        _conn = new SqliteConnection("Data Source=:memory:");
        _conn.Open();
        var opts = new DbContextOptionsBuilder<CardDbContext>().UseSqlite(_conn).Options;
        _db = new CardDbContext(opts);
        _db.Database.EnsureCreated();
        Seed();
    }

    private void Seed()
    {
        _db.Cards.AddRange(
            NewCard("Bear Cub", implemented: true),
            NewCard("Grizzly Bears", implemented: true),
            NewCard("Hill Giant", implemented: false),
            NewCard("Lightning Bolt", implemented: false,
                manaCost: "{R}", typeLine: "Instant", cmc: 1, colors: new[] { "R" }));
        _db.SaveChanges();
    }

    private static CardEntity NewCard(
        string name,
        bool implemented,
        string? manaCost = "{1}{G}",
        string typeLine = "Creature — Bear",
        int? cmc = null,
        string[]? colors = null) =>
        new()
        {
            Name = name,
            ScryfallId = Guid.NewGuid().ToString(),
            ManaCost = manaCost,
            TypeLine = typeLine,
            Cmc = cmc,
            Colors = colors is { Length: > 0 }
                ? System.Text.Json.JsonSerializer.Serialize(colors)
                : "[]",
            Set = "TST",
            CollectorNumber = "1",
            IsImplemented = implemented,
        };

    private WebApplicationFactory<Program> Factory() =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            // Disable external dependencies so the test server starts cleanly.
            builder.UseSetting("Mongo:ConnectionString", "");
            builder.UseSetting("Auth:Authority", "");

            builder.ConfigureTestServices(services =>
            {
                // Override card data with in-memory SQLite seeded in the ctor.
                services.RemoveAll<ICardRepository>();
                services.RemoveAll<CardDbContext>();
                services.AddSingleton(_db);
                services.AddSingleton<ICardRepository>(new DbCardRepository(_db));

                // Replace JWT auth with a test handler that only authenticates
                // when the Authorization header is present (returns NoResult otherwise,
                // allowing the challenge middleware to return 401).
                services.PostConfigure<AuthenticationOptions>(opts =>
                {
                    opts.DefaultAuthenticateScheme = ProfileTestAuth.SchemeName;
                    opts.DefaultChallengeScheme = ProfileTestAuth.SchemeName;
                });
                services.AddAuthentication(ProfileTestAuth.SchemeName)
                    .AddScheme<AuthenticationSchemeOptions, ProfileTestAuth>(
                        ProfileTestAuth.SchemeName, _ => { });
            });
        });

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

    [Fact]
    public async Task GetCards_Unauth_Returns401()
    {
        using var f = Factory();
        var resp = await Authed(f, null).GetAsync("/cards");
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetCards_NoQuery_ReturnsAllSorted()
    {
        using var f = Factory();
        var resp = await Authed(f, "stub-alice").GetAsync("/cards");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<IReadOnlyList<CardDto>>();
        body!.Select(c => c.Name)
             .Should().ContainInOrder("Bear Cub", "Grizzly Bears", "Hill Giant", "Lightning Bolt");
    }

    [Fact]
    public async Task GetCards_WithQuery_FiltersByNamePrefix()
    {
        // Prefix-only LIKE so the IX_Cards_Name_NoCase index applies. Matches
        // "Bear Cub" but not "Grizzly Bears" — see DbCardRepository.Search.
        using var f = Factory();
        var resp = await Authed(f, "stub-alice").GetAsync("/cards?q=bear");
        var body = await resp.Content.ReadFromJsonAsync<IReadOnlyList<CardDto>>();
        body!.Select(c => c.Name)
             .Should().BeEquivalentTo(new[] { "Bear Cub" });
    }

    [Fact]
    public async Task GetCards_ImplementedOnly_ExcludesUnflagged()
    {
        using var f = Factory();
        var resp = await Authed(f, "stub-alice").GetAsync("/cards?implementedOnly=true");
        var body = await resp.Content.ReadFromJsonAsync<IReadOnlyList<CardDto>>();
        body!.Select(c => c.Name)
             .Should().BeEquivalentTo(new[] { "Bear Cub", "Grizzly Bears" });
    }

    [Fact]
    public async Task GetCards_LimitOver200_Returns400()
    {
        using var f = Factory();
        var resp = await Authed(f, "stub-alice").GetAsync("/cards?limit=300");
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await resp.Content.ReadFromJsonAsync<CardsError>();
        body!.Error.Should().Be("invalid-limit");
    }

    [Fact]
    public async Task GetCards_LimitZero_Returns400()
    {
        using var f = Factory();
        var resp = await Authed(f, "stub-alice").GetAsync("/cards?limit=0");
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task GetCards_LimitHonored()
    {
        using var f = Factory();
        var resp = await Authed(f, "stub-alice").GetAsync("/cards?limit=2");
        var body = await resp.Content.ReadFromJsonAsync<IReadOnlyList<CardDto>>();
        body!.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetCards_ColorsFilter_ReturnsOnlyMatchingColor()
    {
        using var f = Factory();
        var resp = await Authed(f, "stub-alice").GetAsync("/cards?colors=R");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<IReadOnlyList<CardDto>>();
        // Only Lightning Bolt is seeded with color R
        body!.Select(c => c.Name).Should().ContainSingle().Which.Should().Be("Lightning Bolt");
    }

    [Fact]
    public async Task GetCards_MultipleColorsFilter_ReturnsUnion()
    {
        using var f = Factory();
        // R matches Lightning Bolt; G matches nothing seeded with explicit G colors
        // Both in query → should return Lightning Bolt (union semantics)
        var resp = await Authed(f, "stub-alice").GetAsync("/cards?colors=R&colors=G");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<IReadOnlyList<CardDto>>();
        body!.Select(c => c.Name).Should().Contain("Lightning Bolt");
    }

    [Fact]
    public async Task GetCards_TypesFilter_ReturnsOnlyMatchingType()
    {
        using var f = Factory();
        // Instant matches only Lightning Bolt in the seeded data
        var resp = await Authed(f, "stub-alice").GetAsync("/cards?types=Instant");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<IReadOnlyList<CardDto>>();
        body!.Select(c => c.Name).Should().ContainSingle().Which.Should().Be("Lightning Bolt");
    }

    [Fact]
    public async Task GetCards_CmcFilter_ReturnsOnlyMatchingCmc()
    {
        using var f = Factory();
        // cmc=1 should match Lightning Bolt (seeded with cmc 1)
        var resp = await Authed(f, "stub-alice").GetAsync("/cards?cmc=1");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<IReadOnlyList<CardDto>>();
        body!.Select(c => c.Name).Should().ContainSingle().Which.Should().Be("Lightning Bolt");
    }

    // ---- POST /cards/by-name ----

    [Fact]
    public async Task PostByName_Unauth_Returns401()
    {
        using var f = Factory();
        var resp = await Authed(f, null).PostAsJsonAsync("/cards/by-name", new { names = new[] { "Bear Cub" } });
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task PostByName_EmptyList_ReturnsEmptyArray()
    {
        using var f = Factory();
        var resp = await Authed(f, "stub-alice").PostAsJsonAsync("/cards/by-name", new { names = Array.Empty<string>() });
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<IReadOnlyList<CardDto>>();
        body.Should().BeEmpty();
    }

    [Fact]
    public async Task PostByName_NullBody_ReturnsEmptyArray()
    {
        using var f = Factory();
        // Send an explicit null names list
        var resp = await Authed(f, "stub-alice").PostAsJsonAsync("/cards/by-name", new { names = (string[]?)null });
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<IReadOnlyList<CardDto>>();
        body.Should().BeEmpty();
    }

    [Fact]
    public async Task PostByName_TooManyNames_Returns400()
    {
        using var f = Factory();
        var names = Enumerable.Range(0, 201).Select(i => $"Card {i}").ToArray();
        var resp = await Authed(f, "stub-alice").PostAsJsonAsync("/cards/by-name", new { names });
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await resp.Content.ReadFromJsonAsync<CardsError>();
        body!.Error.Should().Be("too-many-names");
    }

    [Fact]
    public async Task PostByName_ExactNames_ReturnsMatchingCardsOnly()
    {
        using var f = Factory();
        var resp = await Authed(f, "stub-alice").PostAsJsonAsync("/cards/by-name",
            new { names = new[] { "Bear Cub", "Lightning Bolt" } });
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<IReadOnlyList<CardDto>>();
        body!.Select(c => c.Name).Should()
            .BeEquivalentTo(new[] { "Bear Cub", "Lightning Bolt" });
    }

    [Fact]
    public async Task PostByName_ResultsSortedByName()
    {
        using var f = Factory();
        // Post in reverse order; response should be alphabetical
        var resp = await Authed(f, "stub-alice").PostAsJsonAsync("/cards/by-name",
            new { names = new[] { "Lightning Bolt", "Bear Cub" } });
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<IReadOnlyList<CardDto>>();
        body!.Select(c => c.Name).Should().ContainInOrder("Bear Cub", "Lightning Bolt");
    }

    [Fact]
    public async Task PostByName_UnknownNamesOmitted()
    {
        using var f = Factory();
        var resp = await Authed(f, "stub-alice").PostAsJsonAsync("/cards/by-name",
            new { names = new[] { "Bear Cub", "Nonexistent Card XYZ" } });
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<IReadOnlyList<CardDto>>();
        body!.Select(c => c.Name).Should().ContainSingle().Which.Should().Be("Bear Cub");
    }

    [Fact]
    public async Task PostByName_SubstringDoesNotMatch()
    {
        using var f = Factory();
        // "Bear" is a substring of "Bear Cub" — should NOT match exact-name semantics
        var resp = await Authed(f, "stub-alice").PostAsJsonAsync("/cards/by-name",
            new { names = new[] { "Bear" } });
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<IReadOnlyList<CardDto>>();
        body.Should().BeEmpty();
    }

    [Fact]
    public async Task PostByName_DuplicateNamesDeduplicatedInResponse()
    {
        using var f = Factory();
        // Sending same name twice should return it only once
        var resp = await Authed(f, "stub-alice").PostAsJsonAsync("/cards/by-name",
            new { names = new[] { "Bear Cub", "Bear Cub" } });
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<IReadOnlyList<CardDto>>();
        body!.Select(c => c.Name).Should().ContainSingle().Which.Should().Be("Bear Cub");
    }

    public void Dispose()
    {
        _db.Dispose();
        _conn.Dispose();
    }
}
