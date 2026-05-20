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
            NewCard("Lightning Bolt", implemented: false));
        _db.SaveChanges();
    }

    private static CardEntity NewCard(string name, bool implemented) =>
        new()
        {
            Name = name,
            ScryfallId = Guid.NewGuid().ToString(),
            ManaCost = "{1}{G}",
            TypeLine = "Creature — Bear",
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
    public async Task GetCards_WithQuery_FiltersSubstring()
    {
        using var f = Factory();
        var resp = await Authed(f, "stub-alice").GetAsync("/cards?q=bear");
        var body = await resp.Content.ReadFromJsonAsync<IReadOnlyList<CardDto>>();
        body!.Select(c => c.Name)
             .Should().BeEquivalentTo(new[] { "Bear Cub", "Grizzly Bears" });
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

    public void Dispose()
    {
        _db.Dispose();
        _conn.Dispose();
    }
}
