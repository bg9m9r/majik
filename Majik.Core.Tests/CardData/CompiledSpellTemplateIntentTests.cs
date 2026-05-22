using FluentAssertions;
using Majik.Core.Cards;
using Majik.Core.CardData.Database;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Majik.Core.Tests.CardData;

public class CompiledSpellTemplateIntentTests
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
    public void Intent_RoundTrips_ThroughDb()
    {
        using var db = NewDb();
        db.CompiledSpellTemplates.Add(new CompiledSpellTemplateEntity
        {
            CardName = "Lightning Bolt",
            TemplateName = "DamageAnyTarget",
            Priority = 50,
            ParamsJson = "{\"n\":\"3\"}",
            CompiledAt = 1700000000,
            Intent = (ulong)(BotIntent.Burn | BotIntent.Reach),
        });
        db.SaveChanges();

        var row = db.CompiledSpellTemplates.Single(r => r.CardName == "Lightning Bolt");
        ((BotIntent)row.Intent).Should().Be(BotIntent.Burn | BotIntent.Reach);
    }

    [Fact]
    public void Intent_DefaultsToNone_WhenUnset()
    {
        using var db = NewDb();
        db.CompiledSpellTemplates.Add(new CompiledSpellTemplateEntity
        {
            CardName = "Stub",
            TemplateName = "StubTemplate",
            Priority = 10,
            CompiledAt = 0,
        });
        db.SaveChanges();

        var row = db.CompiledSpellTemplates.Single(r => r.CardName == "Stub");
        ((BotIntent)row.Intent).Should().Be(BotIntent.None);
    }
}
