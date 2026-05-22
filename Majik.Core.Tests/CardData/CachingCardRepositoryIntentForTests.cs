using FluentAssertions;
using Majik.Core.Cards;
using Majik.Core.CardData;
using Majik.Core.CardData.Database;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace Majik.Core.Tests.CardData;

public class CachingCardRepositoryIntentForTests
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
    public void DbCardRepository_IntentFor_ReturnsPersistedFlags()
    {
        using var db = NewDb();
        db.CompiledSpellTemplates.Add(new CompiledSpellTemplateEntity
        {
            CardName = "Lightning Bolt",
            TemplateName = "DamageAnyTarget",
            Priority = 50,
            CompiledAt = 0,
            Intent = (ulong)(BotIntent.Burn | BotIntent.Reach),
        });
        db.SaveChanges();

        var repo = new DbCardRepository(db);
        repo.IntentFor("Lightning Bolt").Should().Be(BotIntent.Burn | BotIntent.Reach);
    }

    [Fact]
    public void DbCardRepository_IntentFor_UnknownCard_ReturnsNone()
    {
        using var db = NewDb();
        var repo = new DbCardRepository(db);
        repo.IntentFor("Made-Up Card").Should().Be(BotIntent.None);
    }

    [Fact]
    public void DbCardRepository_IntentFor_NullOrWhitespace_ReturnsNone()
    {
        using var db = NewDb();
        var repo = new DbCardRepository(db);
        repo.IntentFor("").Should().Be(BotIntent.None);
        repo.IntentFor("   ").Should().Be(BotIntent.None);
    }

    [Fact]
    public void Caching_HitsInnerOnce()
    {
        var inner = new Mock<ICardRepository>();
        inner.Setup(r => r.IntentFor("X")).Returns(BotIntent.Burn);

        var caching = new CachingCardRepository(inner.Object);
        caching.IntentFor("X");
        caching.IntentFor("X");
        caching.IntentFor("X");

        inner.Verify(r => r.IntentFor("X"), Times.Once);
    }

    [Fact]
    public void Caching_DistinctNamesCachedSeparately()
    {
        var inner = new Mock<ICardRepository>();
        inner.Setup(r => r.IntentFor("Bolt")).Returns(BotIntent.Burn);
        inner.Setup(r => r.IntentFor("Doom Blade")).Returns(BotIntent.Removal);

        var caching = new CachingCardRepository(inner.Object);
        caching.IntentFor("Bolt").Should().Be(BotIntent.Burn);
        caching.IntentFor("Doom Blade").Should().Be(BotIntent.Removal);
        caching.IntentFor("Bolt").Should().Be(BotIntent.Burn);

        inner.Verify(r => r.IntentFor("Bolt"), Times.Once);
        inner.Verify(r => r.IntentFor("Doom Blade"), Times.Once);
    }
}
