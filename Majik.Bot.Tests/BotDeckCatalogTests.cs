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
}
