using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.CardData.Database;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Majik.Core.Tests.CardData;

public class DbCardRepositorySearchTests : IDisposable
{
    private readonly SqliteConnection _conn;
    private readonly CardDbContext _db;
    private readonly DbCardRepository _repo;

    public DbCardRepositorySearchTests()
    {
        (_db, _conn) = NewDb();
        _repo = new DbCardRepository(_db);
    }

    public void Dispose()
    {
        _db.Dispose();
        _conn.Dispose();
    }

    private static (CardDbContext db, SqliteConnection conn) NewDb()
    {
        var conn = new SqliteConnection("Data Source=:memory:");
        conn.Open();
        var opts = new DbContextOptionsBuilder<CardDbContext>().UseSqlite(conn).Options;
        var db = new CardDbContext(opts);
        db.Database.EnsureCreated();
        return (db, conn);
    }

    private static CardEntity NewCard(string name, bool isImplemented = false) => new CardEntity
    {
        Name = name,
        ScryfallId = Guid.NewGuid().ToString(),
        ManaCost = "{1}",
        TypeLine = "Instant",
        Set = "m21",
        CollectorNumber = "1",
        IsImplemented = isImplemented,
        ImportedAt = DateTime.UtcNow,
    };

    [Fact]
    public void Search_NoFilter_ReturnsSortedByName()
    {
        _db.Cards.AddRange(
            NewCard("Zap"),
            NewCard("Aardvark"),
            NewCard("Mana Leak"));
        _db.SaveChanges();

        var results = _repo.Search(null, false, 100);

        results.Select(c => c.Name).Should().ContainInOrder("Aardvark", "Mana Leak", "Zap");
    }

    [Fact]
    public void Search_QFilter_ReturnsSubstringMatches()
    {
        _db.Cards.AddRange(
            NewCard("Lightning Bolt"),
            NewCard("Lightning Strike"),
            NewCard("Grizzly Bears"));
        _db.SaveChanges();

        var results = _repo.Search("Lightning", false, 100);

        results.Should().HaveCount(2);
        results.Select(c => c.Name).Should().Contain("Lightning Bolt").And.Contain("Lightning Strike");
    }

    [Fact]
    public void Search_ImplementedOnlyTrue_ExcludesUnimplemented()
    {
        _db.Cards.AddRange(
            NewCard("Implemented Card", isImplemented: true),
            NewCard("Not Implemented Card", isImplemented: false));
        _db.SaveChanges();

        var results = _repo.Search(null, true, 100);

        results.Should().HaveCount(1);
        results[0].Name.Should().Be("Implemented Card");
    }

    [Fact]
    public void Search_LimitHonored_ReturnsAtMostLimitRows()
    {
        _db.Cards.AddRange(
            NewCard("Alpha"),
            NewCard("Beta"),
            NewCard("Gamma"),
            NewCard("Delta"),
            NewCard("Epsilon"));
        _db.SaveChanges();

        var results = _repo.Search(null, false, 3);

        results.Should().HaveCount(3);
    }
}
