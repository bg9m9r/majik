using System.Linq;
using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="TineShrikeFactory"/>
/// (Phyrexia: All Will Be One, {3}{W}).
///
/// Tine Shrike — Creature — Phyrexian Bird 2/1. Oracle text (verified against
/// Scryfall 2026-06-23):
///   "Flying
///    Infect (This creature deals damage to creatures in the form of -1/-1
///    counters and to players in the form of poison counters.)"
///
/// The card is a near-vanilla body — two intrinsic evergreen keywords and no
/// triggered / activated logic. These tests cover its UNIQUE surface: the
/// Flying (CR 702.9) + Infect (CR 702.90) keyword markers, plus a single
/// Identity assert for the non-vanilla stats (mana cost / P-T / subtypes /
/// white colour). NamedCardFactory dispatch + well-formedness are asserted for
/// every implemented card by CardFactoryContractTests — not re-tested here.
/// </summary>
[Trait("Color", "W")]
public class TineShrikeFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void TineShrike_Identity_PhyrexianBird_2_1_White3W()
    {
        var c = TineShrikeFactory.Create(_alice);

        c.Name.Should().Be("Tine Shrike");
        c.ManaCost.Should().Be("{3}{W}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Phyrexian).Should().BeTrue();
        c.HasSubtype(CardSubtype.Bird).Should().BeTrue();
        c.BasePower.Should().Be(2);
        c.BaseToughness.Should().Be(1);
        CardColors.GetColors(c).Should().BeEquivalentTo(new[] { ManaColor.White },
            "the {3}{W} cost has a single white pip (CR 105.2a)");
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void TineShrike_HasFlyingAndInfectKeywordMarkers()
    {
        var c = TineShrikeFactory.Create(_alice);

        var keywords = c.Abilities.OfType<KeywordAbility>().Select(k => k.Keyword).ToList();
        keywords.Should().Contain("Flying",
            "Flying is wired as a KeywordAbility marker (CR 702.9)");
        keywords.Should().Contain("Infect",
            "Infect is wired as a KeywordAbility marker (CR 702.90)");
    }
}
