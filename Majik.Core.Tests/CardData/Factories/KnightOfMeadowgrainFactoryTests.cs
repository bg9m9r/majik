using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="KnightOfMeadowgrainFactory"/>.
///
/// Card: Knight of Meadowgrain — {W}{W} Creature — Kithkin Knight 2/2.
///   "First strike
///    Lifelink"
/// </summary>
[Trait("Color", "W")]
public class KnightOfMeadowgrainFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void KnightOfMeadowgrain_Identity()
    {
        var c = KnightOfMeadowgrainFactory.Create(_alice);

        c.Name.Should().Be("Knight of Meadowgrain");
        c.ManaCost.Should().Be("{W}{W}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Kithkin).Should().BeTrue();
        c.HasSubtype(CardSubtype.Knight).Should().BeTrue();
        c.Power.Should().Be(2);
        c.Toughness.Should().Be(2);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void KnightOfMeadowgrain_HasFirstStrikeKeywordMarker()
    {
        var c = KnightOfMeadowgrainFactory.Create(_alice);

        // CR 702.7 — First strike. CombatAbilities.HasFirstStrike consumes
        // this marker to give the creature its combat damage in the
        // first-strike step.
        c.Abilities.OfType<KeywordAbility>()
            .Any(k => k.Keyword == "First strike").Should().BeTrue(
                "Knight of Meadowgrain has First strike (CR 702.7)");
    }

    [Fact]
    public void KnightOfMeadowgrain_HasLifelinkKeywordMarker()
    {
        var c = KnightOfMeadowgrainFactory.Create(_alice);

        // CR 702.15 — Lifelink. CombatAbilities.HasLifelink consumes this
        // marker to gain the controller life equal to damage dealt.
        c.Abilities.OfType<KeywordAbility>()
            .Any(k => k.Keyword == "Lifelink").Should().BeTrue(
                "Knight of Meadowgrain has Lifelink (CR 702.15)");
    }

    [Fact]
    public void KnightOfMeadowgrain_HasExactlyTwoKeywords()
    {
        var c = KnightOfMeadowgrainFactory.Create(_alice);

        c.Abilities.OfType<KeywordAbility>().Should().HaveCount(2,
            "First strike and Lifelink are the only printed keywords");
    }

    [Fact]
    public void KnightOfMeadowgrain_NoTriggeredOrActivatedAbilities()
    {
        var c = KnightOfMeadowgrainFactory.Create(_alice);

        c.Abilities.OfType<TriggeredAbility>().Should().BeEmpty();
        c.Abilities.OfType<ActivatedAbility>().Should().BeEmpty();
    }
}
