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
        });
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
