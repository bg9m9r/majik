using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="HealersFlockFactory"/>.
///
/// Card: Healer's Flock — {W}{W}{W} Creature — Bird 3/3.
///   "Flying, lifelink"
/// </summary>
[Trait("Color", "W")]
public class HealersFlockFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void HealersFlock_Identity()
    {
        var c = HealersFlockFactory.Create(_alice);

        c.Name.Should().Be("Healer's Flock");
        c.ManaCost.Should().Be("{W}{W}{W}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Bird).Should().BeTrue();
        c.Power.Should().Be(3);
        c.Toughness.Should().Be(3);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void HealersFlock_IsWhite()
    {
        var c = HealersFlockFactory.Create(_alice);

        CardColors.GetColors(c).Should().Contain(ManaColor.White,
            "Healer's Flock has {W}{W}{W} pips in its mana cost");
    }

    [Fact]
    public void HealersFlock_HasFlyingKeywordMarker()
    {
        var c = HealersFlockFactory.Create(_alice);

        c.Abilities.OfType<KeywordAbility>()
            .Any(k => k.Keyword == "Flying").Should().BeTrue(
                "Healer's Flock has Flying (CR 702.9)");
    }

    [Fact]
    public void HealersFlock_HasLifelinkKeywordMarker()
    {
        var c = HealersFlockFactory.Create(_alice);

        c.Abilities.OfType<KeywordAbility>()
            .Any(k => k.Keyword == "Lifelink").Should().BeTrue(
                "Healer's Flock has Lifelink (CR 702.15)");
    }

    [Fact]
    public void HealersFlock_HasExactlyTwoKeywords()
    {
        var c = HealersFlockFactory.Create(_alice);

        c.Abilities.OfType<KeywordAbility>().Should().HaveCount(2,
            "Flying and Lifelink are the only printed keywords");
    }

    [Fact]
    public void HealersFlock_NoTriggeredOrActivatedAbilities()
    {
        var c = HealersFlockFactory.Create(_alice);

        c.Abilities.OfType<TriggeredAbility>().Should().BeEmpty();
        c.Abilities.OfType<ActivatedAbility>().Should().BeEmpty();
    }
}
