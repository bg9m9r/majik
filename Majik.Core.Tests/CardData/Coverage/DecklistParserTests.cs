using FluentAssertions;
using Majik.Core.CardData.Coverage;
using Xunit;

namespace Majik.Core.Tests.CardData.Coverage;

/// <summary>
/// Smoke tests for <see cref="DecklistParser"/>. Uses paste-format
/// fragments that mirror the lists the user reviews coverage against —
/// Izzet Prowess (Modern) and Yawgmoth Cauldron (Modern).
/// </summary>
public class DecklistParserTests
{
    [Fact]
    public void Parse_BasicMTGOFormat()
    {
        var deck = """
            4 Lightning Bolt
            3 Counterspell
            2x Brainstorm
            """;
        var parsed = DecklistParser.Parse(deck);
        parsed.Should().BeEquivalentTo(new Dictionary<string, int>
        {
            ["Lightning Bolt"] = 4,
            ["Counterspell"] = 3,
            ["Brainstorm"] = 2,
        });
    }

    [Fact]
    public void Parse_Ignores_Headers_And_Comments()
    {
        var deck = """
            # Mainboard
            Deck
            4 Lightning Bolt
            // sideboard line
            Sideboard
            2 Pyroblast
            """;
        var parsed = DecklistParser.Parse(deck);
        parsed.Should().BeEquivalentTo(new Dictionary<string, int>
        {
            ["Lightning Bolt"] = 4,
            ["Pyroblast"] = 2,
        });
    }

    [Fact]
    public void Parse_Sums_Duplicate_Entries_Across_Sections()
    {
        var deck = """
            3 Lightning Bolt
            Sideboard
            1 Lightning Bolt
            """;
        var parsed = DecklistParser.Parse(deck);
        parsed["Lightning Bolt"].Should().Be(4);
    }

    [Fact]
    public void Parse_Strips_Set_Printing_Tail()
    {
        var deck = """
            4 Lightning Bolt (LEA) 161
            1 Counterspell [7ED]
            """;
        var parsed = DecklistParser.Parse(deck);
        parsed.Should().ContainKeys("Lightning Bolt", "Counterspell");
    }

    [Fact]
    public void Parse_IzzetProwess_Excerpt()
    {
        var deck = """
            4 Monastery Swiftspear
            4 Lightning Bolt
            4 Stormchaser's Talent
            4 Slickshot Show-Off
            """;
        var parsed = DecklistParser.Parse(deck);
        parsed.Values.Sum().Should().Be(16);
        parsed.Should().HaveCount(4);
    }

    [Fact]
    public void Parse_YawgmothCauldron_Excerpt()
    {
        var deck = """
            4 Yawgmoth, Thran Physician
            4 Agatha's Soul Cauldron
            3 Grist, the Hunger Tide
            """;
        var parsed = DecklistParser.Parse(deck);
        parsed["Yawgmoth, Thran Physician"].Should().Be(4);
        parsed["Agatha's Soul Cauldron"].Should().Be(4);
        parsed["Grist, the Hunger Tide"].Should().Be(3);
    }

    [Fact]
    public void Parse_Empty_Returns_Empty()
    {
        DecklistParser.Parse("").Should().BeEmpty();
        DecklistParser.Parse("\n\n# nothing here\n").Should().BeEmpty();
    }
}
