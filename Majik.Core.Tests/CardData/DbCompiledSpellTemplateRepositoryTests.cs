using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.CardData.Database;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Majik.Core.Tests.CardData;

public class DbCompiledSpellTemplateRepositoryTests
{
    private static CardDbContext NewDb()
    {
        var options = new DbContextOptionsBuilder<CardDbContext>()
            .UseSqlite("DataSource=:memory:")
            .Options;
        var db = new CardDbContext(options);
        db.Database.OpenConnection();
        db.Database.EnsureCreated();
        return db;
    }

    [Fact]
    public void Lookup_ReturnsRow_ForExistingCardName()
    {
        using var db = NewDb();
        db.CompiledSpellTemplates.Add(new CompiledSpellTemplateEntity
        {
            CardName = "Lightning Bolt",
            TemplateName = "DamageAnyTarget",
            Priority = 50,
            ParamsJson = "{\"n\":\"3\"}",
            CompiledAt = 1700000000,
        });
        db.SaveChanges();

        var repo = new DbCompiledSpellTemplateRepository(db);
        var hit = repo.Lookup("Lightning Bolt");

        hit.Should().NotBeNull();
        hit!.TemplateName.Should().Be("DamageAnyTarget");
        hit.ParamsJson.Should().Be("{\"n\":\"3\"}");
    }

    [Fact]
    public void Lookup_ReturnsNull_ForMissingCardName()
    {
        using var db = NewDb();
        var repo = new DbCompiledSpellTemplateRepository(db);

        repo.Lookup("Nonexistent Card").Should().BeNull();
    }

    [Fact]
    public void Lookup_NullOrWhitespace_ReturnsNull()
    {
        using var db = NewDb();
        var repo = new DbCompiledSpellTemplateRepository(db);

        repo.Lookup("").Should().BeNull();
        repo.Lookup("   ").Should().BeNull();
    }
}
