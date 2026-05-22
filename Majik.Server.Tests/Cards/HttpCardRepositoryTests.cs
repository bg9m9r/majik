using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.CardData.Database;
using Majik.Server.Cards;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Majik.Server.Tests.Cards;

/// <summary>
/// Hosts the real Majik.CardsServer in-process against an in-memory SQLite
/// DB, points HttpCardRepository at it via the TestServer's HttpClient, and
/// asserts that the entity round-trip preserves every field used by callers
/// downstream of <see cref="ICardRepository"/>.
/// </summary>
public class HttpCardRepositoryTests : IDisposable
{
    private readonly SqliteConnection _conn;
    private readonly CardDbContext _db;
    private readonly WebApplicationFactory<Majik.CardsServer.CardsServerEntryPoint> _factory;
    private readonly HttpCardRepository _repo;

    public HttpCardRepositoryTests()
    {
        _conn = new SqliteConnection("Data Source=:memory:");
        _conn.Open();
        var opts = new DbContextOptionsBuilder<CardDbContext>().UseSqlite(_conn).Options;
        _db = new CardDbContext(opts);
        _db.Database.EnsureCreated();
        Seed();

        _factory = new WebApplicationFactory<Majik.CardsServer.CardsServerEntryPoint>().WithWebHostBuilder(b =>
        {
            // Disable the X-Internal-Token check for tests (empty = disabled).
            b.UseSetting("Cards:InternalToken", "");
            b.ConfigureServices(services =>
            {
                services.RemoveAll<ICardRepository>();
                services.AddSingleton<ICardRepository>(new DbCardRepository(_db));
            });
        });

        _repo = new HttpCardRepository(_factory.CreateClient());
    }

    private void Seed()
    {
        _db.Cards.AddRange(
            Card("Bear Cub", implemented: true, cmc: 2, manaCost: "{1}{G}",
                 typeLine: "Creature — Bear", colors: "[\"G\"]",
                 oracleText: "A small bear.", power: "2", toughness: "2"),
            Card("Lightning Bolt", implemented: false, cmc: 1, manaCost: "{R}",
                 typeLine: "Instant", colors: "[\"R\"]",
                 oracleText: "Lightning Bolt deals 3 damage to any target."),
            Card("Hill Giant", implemented: false, cmc: 4, manaCost: "{3}{R}",
                 typeLine: "Creature — Giant", colors: "[\"R\"]",
                 oracleText: "", power: "3", toughness: "3"),
            Card("Jace, the Mind Sculptor", implemented: false, cmc: 4, manaCost: "{2}{U}{U}",
                 typeLine: "Legendary Planeswalker — Jace", colors: "[\"U\"]",
                 loyalty: 3));
        _db.SaveChanges();
    }

    private static CardEntity Card(
        string name,
        bool implemented,
        int? cmc = null,
        string? manaCost = null,
        string typeLine = "",
        string colors = "[]",
        string? oracleText = null,
        string? power = null,
        string? toughness = null,
        int? loyalty = null) =>
        new()
        {
            Name = name,
            ScryfallId = Guid.NewGuid().ToString(),
            ManaCost = manaCost,
            Cmc = cmc,
            TypeLine = typeLine,
            OracleText = oracleText,
            Power = power,
            Toughness = toughness,
            Loyalty = loyalty,
            Colors = colors,
            IsImplemented = implemented,
        };

    [Fact]
    public void GetByName_KnownCard_RoundTripsAllGameplayFields()
    {
        var e = _repo.GetByName("Bear Cub");
        e.Should().NotBeNull();
        e!.Name.Should().Be("Bear Cub");
        e.ManaCost.Should().Be("{1}{G}");
        e.Cmc.Should().Be(2);
        e.TypeLine.Should().Be("Creature — Bear");
        e.OracleText.Should().Be("A small bear.");
        e.Power.Should().Be("2");
        e.Toughness.Should().Be("2");
        e.Colors.Should().Be("[\"G\"]");
        e.IsImplemented.Should().BeTrue();
    }

    [Fact]
    public void GetByName_Planeswalker_PreservesLoyalty()
    {
        var e = _repo.GetByName("Jace, the Mind Sculptor");
        e.Should().NotBeNull();
        e!.Loyalty.Should().Be(3);
    }

    [Fact]
    public void GetByName_Unknown_ReturnsNull()
    {
        _repo.GetByName("Nope, Not A Card").Should().BeNull();
    }

    [Fact]
    public void GetByNames_Bulk_DedupesAndPreservesEachEntity()
    {
        var rows = _repo.GetByNames(new[] { "Bear Cub", "Lightning Bolt", "Lightning Bolt", "Unknown" });
        rows.Select(r => r.Name).Should().BeEquivalentTo(new[] { "Bear Cub", "Lightning Bolt" });
        rows.Single(r => r.Name == "Lightning Bolt").OracleText
            .Should().Be("Lightning Bolt deals 3 damage to any target.");
    }

    [Fact]
    public void Search_PrefixQuery_HitsMatchingCard()
    {
        var rows = _repo.Search(q: "Bear", implementedOnly: false, limit: 10);
        rows.Select(r => r.Name).Should().Contain("Bear Cub");
    }

    [Fact]
    public void Search_ImplementedOnly_FiltersUnimplemented()
    {
        var rows = _repo.Search(q: null, implementedOnly: true, limit: 50);
        rows.Should().OnlyContain(r => r.IsImplemented);
    }

    [Fact]
    public void IsImplemented_KnownImplemented_True()
    {
        _repo.IsImplemented("Bear Cub").Should().BeTrue();
    }

    [Fact]
    public void IsImplemented_KnownUnimplemented_False()
    {
        _repo.IsImplemented("Lightning Bolt").Should().BeFalse();
    }

    [Fact]
    public void SetImplemented_FlipsFlag()
    {
        _repo.SetImplemented("Lightning Bolt", true);
        _repo.IsImplemented("Lightning Bolt").Should().BeTrue();
    }

    [Fact]
    public void SetImplemented_UnknownName_ThrowsArgumentException()
    {
        var ex = Record.Exception(() => _repo.SetImplemented("Definitely Not A Card", true));
        ex.Should().BeOfType<ArgumentException>();
    }

    public void Dispose()
    {
        _factory.Dispose();
        _db.Dispose();
        _conn.Dispose();
    }
}
