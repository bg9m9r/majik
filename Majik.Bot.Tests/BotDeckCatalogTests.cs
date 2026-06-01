using FluentAssertions;
using Majik.Bot.Decks;
using Xunit;

namespace Majik.Bot.Tests;

public class BotDeckCatalogTests
{
    [Fact]
    public void Catalog_ContainsAllArchetypes()
    {
        BotDeckCatalog.Archetypes.Should().BeEquivalentTo(new[]
        {
            "Burn", "Prowess", "BorosEnergy", "Yawg",
            "Affinity", "RubyStorm", "Belcher", "GoryoVengeance", "LivingEnd",
            "EldraziTron", "GrixisReanimator", "DimirMidrange", "EldraziRamp",
            "Neobrand", "EsperBlink", "SultaiMidrange", "MonoBlackMidrange",
            "AzoriusBlink", "AzoriusControl", "BorosLandDestruction", "Rhinos",
            "DomainZoo", "GruulBroodscale", "EldraziBroodscale",
        });
    }

    [Fact]
    public void Label_StripsBotPrefix_AndSpacesName()
    {
        BotDeckCatalog.Label("BorosEnergy").Should().Be("Boros Energy");
        BotDeckCatalog.Label("DomainZoo").Should().Be("Domain Zoo");
    }

    [Fact]
    public void Get_ReturnsDeckListForKnownArchetype()
    {
        var list = BotDeckCatalog.Get("Burn");
        list.Should().NotBeEmpty();
    }

    [Fact]
    public void Get_UnknownArchetype_Throws()
    {
        var act = () => BotDeckCatalog.Get("Mystery");
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void DisplayName_KnownArchetypes_ContainsBotPrefix()
    {
        BotDeckCatalog.DisplayName("Burn").Should().Contain("Bot").And.Contain("Burn");
    }

    // ── Sideboard contract (deferral #8) ───────────────────────────────

    [Fact]
    public void GetSideboard_ReturnsFifteenCards_ForFilledArchetype()
    {
        // RubyStorm has Wishes maindeck, so its sideboard is load-bearing.
        BotDeckCatalog.GetSideboard("RubyStorm").Should().HaveCount(15);
        BotDeckCatalog.GetSideboard("Burn").Should().HaveCount(15);
    }

    [Fact]
    public void GetSideboard_UnknownArchetype_Throws()
    {
        var act = () => BotDeckCatalog.GetSideboard("Mystery");
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void GetSideboard_EveryKnownArchetype_IsEitherEmptyOrExactlyFifteen()
    {
        foreach (var archetype in BotDeckCatalog.Archetypes)
        {
            var sb = BotDeckCatalog.GetSideboard(archetype);
            sb.Should().NotBeNull();
            (sb.Count == 0 || sb.Count == 15).Should().BeTrue(
                $"archetype '{archetype}' sideboard must be empty (not yet filled) " +
                $"or a legal 15 cards, but had {sb.Count}");
        }
    }

    [Fact]
    public void GetSideboard_EveryKnownArchetype_IsFilled()
    {
        // All ~24 archetypes ship a 15-card sideboard. If a future archetype
        // is added without one, GetSideboard still returns empty (no crash) —
        // this test then flags that it needs a list.
        var unfilled = BotDeckCatalog.Archetypes
            .Where(a => BotDeckCatalog.GetSideboard(a).Count == 0)
            .ToList();

        unfilled.Should().BeEmpty(
            "every bot archetype should ship a 15-card sideboard; " +
            "unfilled archetypes default to empty and need a list");
    }
}
