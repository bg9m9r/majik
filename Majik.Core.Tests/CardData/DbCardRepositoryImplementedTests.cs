using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.CardData.Database;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Majik.Core.Tests.CardData;

public class DbCardRepositoryImplementedTests : IDisposable
{
    private readonly SqliteConnection _conn;
    private readonly CardDbContext _db;
    private readonly DbCardRepository _repo;

    public DbCardRepositoryImplementedTests()
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
    public void IsImplemented_DefaultsToFalse()
    {
        _db.Cards.Add(NewCard("Lightning Bolt", isImplemented: false));
        _db.SaveChanges();

        _repo.IsImplemented("Lightning Bolt").Should().BeFalse();
    }

    [Fact]
    public void SetImplemented_PersistsTrue()
    {
        _db.Cards.Add(NewCard("Lightning Bolt", isImplemented: false));
        _db.SaveChanges();

        _repo.SetImplemented("Lightning Bolt", true);

        _repo.IsImplemented("Lightning Bolt").Should().BeTrue();
    }

    [Fact]
    public void SetImplemented_TogglesBackToFalse()
    {
        _db.Cards.Add(NewCard("Lightning Bolt", isImplemented: true));
        _db.SaveChanges();

        _repo.SetImplemented("Lightning Bolt", false);

        _repo.IsImplemented("Lightning Bolt").Should().BeFalse();
    }

    [Fact]
    public void SetImplemented_UnknownCard_ThrowsArgumentException()
    {
        var act = () => _repo.SetImplemented("Nonexistent Card", true);

        act.Should().Throw<ArgumentException>()
            .WithParameterName("name");
    }

    [Fact]
    public void IsImplemented_UnknownCard_ReturnsFalse()
    {
        _repo.IsImplemented("Totally Unknown Card").Should().BeFalse();
    }
}
