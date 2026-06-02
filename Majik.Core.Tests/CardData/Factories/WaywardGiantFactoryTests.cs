using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="WaywardGiantFactory"/>.
///
/// Card: Wayward Giant — {4}{R} Creature — Giant 4/5.
///   "Menace"
/// </summary>
[Trait("Color", "R")]
public class WaywardGiantFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void WaywardGiant_Identity()
    {
        var c = WaywardGiantFactory.Create(_alice);

        c.Name.Should().Be("Wayward Giant");
        c.ManaCost.Should().Be("{4}{R}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Giant).Should().BeTrue();
        c.Power.Should().Be(4);
        c.Toughness.Should().Be(5);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void WaywardGiant_IsRed()
    {
        var c = WaywardGiantFactory.Create(_alice);

        CardColors.GetColors(c).Should().Contain(ManaColor.Red,
            "Wayward Giant has one {R} pip in its mana cost");
    }

    [Fact]
    public void WaywardGiant_ManaValueIsFive()
    {
        var c = WaywardGiantFactory.Create(_alice);

        // {4}{R} → generic 4 + one coloured pip = mana value 5 (CR 202.3).
        ManaCost.Parse(c.ManaCost).TotalValue.Should().Be(5);
    }

    [Fact]
    public void WaywardGiant_HasMenaceKeywordMarker()
    {
        var c = WaywardGiantFactory.Create(_alice);

        c.Abilities.OfType<KeywordAbility>()
            .Any(k => k.Keyword == "Menace").Should().BeTrue(
                "Wayward Giant ships with Menace as a KeywordAbility marker (CR 702.110)");
    }

    [Fact]
    public void WaywardGiant_NoOtherAbilities()
    {
        var c = WaywardGiantFactory.Create(_alice);

        c.Abilities.OfType<TriggeredAbility>().Should().BeEmpty();
        c.Abilities.OfType<ActivatedAbility>().Should().BeEmpty();
        c.Abilities.OfType<KeywordAbility>().Should().HaveCount(1,
            "Menace is the only printed keyword");
    }
}
