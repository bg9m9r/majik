using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="BrightbladeStoatFactory"/>.
///
/// Card: Brightblade Stoat — {1}{W} Creature — Weasel Soldier 2/2.
///   "First strike, lifelink"
/// </summary>
[Trait("Color", "W")]
public class BrightbladeStoatFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void BrightbladeStoat_Identity()
    {
        var c = BrightbladeStoatFactory.Create(_alice);

        c.Name.Should().Be("Brightblade Stoat");
        c.ManaCost.Should().Be("{1}{W}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Weasel).Should().BeTrue();
        c.HasSubtype(CardSubtype.Soldier).Should().BeTrue();
        c.Power.Should().Be(2);
        c.Toughness.Should().Be(2);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void BrightbladeStoat_HasFirstStrikeKeywordMarker()
    {
        var c = BrightbladeStoatFactory.Create(_alice);

        // CR 702.7 — First strike. CombatAbilities.HasFirstStrike consumes
        // this marker to give the creature its combat damage in the
        // first-strike step.
        c.Abilities.OfType<KeywordAbility>()
            .Any(k => k.Keyword == "First strike").Should().BeTrue(
                "Brightblade Stoat has First strike (CR 702.7)");
    }

    [Fact]
    public void BrightbladeStoat_HasLifelinkKeywordMarker()
    {
        var c = BrightbladeStoatFactory.Create(_alice);

        // CR 702.15 — Lifelink. CombatAbilities.HasLifelink consumes this
        // marker to gain the controller life equal to damage dealt.
        c.Abilities.OfType<KeywordAbility>()
            .Any(k => k.Keyword == "Lifelink").Should().BeTrue(
                "Brightblade Stoat has Lifelink (CR 702.15)");
    }

    [Fact]
    public void BrightbladeStoat_HasExactlyTwoKeywords()
    {
        var c = BrightbladeStoatFactory.Create(_alice);

        c.Abilities.OfType<KeywordAbility>().Should().HaveCount(2,
            "First strike and Lifelink are the only printed keywords");
    }

    [Fact]
    public void BrightbladeStoat_NoTriggeredOrActivatedAbilities()
    {
        var c = BrightbladeStoatFactory.Create(_alice);

        c.Abilities.OfType<TriggeredAbility>().Should().BeEmpty();
        c.Abilities.OfType<ActivatedAbility>().Should().BeEmpty();
    }
}
