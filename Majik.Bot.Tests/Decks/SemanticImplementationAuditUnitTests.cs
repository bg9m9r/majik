using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.Cards;
using Xunit;

namespace Majik.Bot.Tests.Decks;

/// <summary>
/// Focused unit tests proving the Layer-A parity comparison
/// (<see cref="SemanticImplementationAuditTests.CompareCardToEntity"/>) flags a
/// deliberately-wrong card and does NOT flag a correct one. This is the TDD
/// proof that the pool-wide report would actually catch the Asmor-class bug.
/// </summary>
public class SemanticImplementationAuditUnitTests
{
    private static CardEntity BearEntity() => new()
    {
        Name = "Grizzly Bears",
        TypeLine = "Creature — Bear",
        ManaCost = "{1}{G}",
        Power = "2",
        Toughness = "2",
        Colors = "[\"G\"]",
    };

    [Fact]
    public void Correct_creature_produces_no_mismatch()
    {
        var entity = BearEntity();
        var card = new Creature("Grizzly Bears", "{1}{G}", 2, 2,
            supertypes: null, subtypes: new[] { Majik.Core.Cards.Types.CardSubtype.Bear });

        var mismatches = SemanticImplementationAuditTests
            .CompareCardToEntity(card, entity).ToList();

        mismatches.Should().BeEmpty();
    }

    [Fact]
    public void Wrong_power_toughness_is_flagged()
    {
        var entity = BearEntity();
        // Pre-errata-style wrong stat line: built 4/4 vs printed 2/2.
        var card = new Creature("Grizzly Bears", "{1}{G}", 4, 4,
            supertypes: null, subtypes: new[] { Majik.Core.Cards.Types.CardSubtype.Bear });

        var mismatches = SemanticImplementationAuditTests
            .CompareCardToEntity(card, entity).ToList();

        mismatches.Should().Contain(m => m.Field == "BasePower" && m.Expected == "2" && m.Actual == "4");
        mismatches.Should().Contain(m => m.Field == "BaseToughness" && m.Expected == "2" && m.Actual == "4");
    }

    [Fact]
    public void DeliberatelyWrongShape_WouldBeFlagged()
    {
        // The Asmor class: the factory builds a fully-fictional shape — wrong
        // type (Instant instead of Creature) and wrong mana value — vs the seed.
        var entity = BearEntity();
        var fictional = new Instant("Grizzly Bears", "{5}");

        var mismatches = SemanticImplementationAuditTests
            .CompareCardToEntity(fictional, entity).ToList();

        mismatches.Should().Contain(m => m.Field == "CardTypes");
        mismatches.Should().Contain(m => m.Field == "ManaValue" && m.Expected == "2" && m.Actual == "5");
    }
}
