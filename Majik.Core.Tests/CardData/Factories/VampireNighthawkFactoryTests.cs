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
/// Unit tests for <see cref="VampireNighthawkFactory"/>.
///
/// Card: Vampire Nighthawk — {1}{B}{B} Creature — Vampire Shaman 2/3.
///   "Flying, deathtouch, lifelink"
/// </summary>
[Trait("Color", "B")]
public class VampireNighthawkFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void VampireNighthawk_Identity()
    {
        var c = VampireNighthawkFactory.Create(_alice);

        c.Name.Should().Be("Vampire Nighthawk");
        c.ManaCost.Should().Be("{1}{B}{B}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Vampire).Should().BeTrue();
        c.HasSubtype(CardSubtype.Shaman).Should().BeTrue();
        c.Power.Should().Be(2);
        c.Toughness.Should().Be(3);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void VampireNighthawk_IsBlack()
    {
        var c = VampireNighthawkFactory.Create(_alice);

        CardColors.GetColors(c).Should().Contain(ManaColor.Black,
            "Vampire Nighthawk has {B}{B} pips in its mana cost");
    }

    [Fact]
    public void VampireNighthawk_ManaValueIsThree()
    {
        var c = VampireNighthawkFactory.Create(_alice);

        // {1}{B}{B} → generic 1 + two coloured pips = mana value 3 (CR 202.3).
        ManaCost.Parse(c.ManaCost).TotalValue.Should().Be(3);
    }

    [Fact]
    public void VampireNighthawk_HasFlyingKeywordMarker()
    {
        var c = VampireNighthawkFactory.Create(_alice);

        c.Abilities.OfType<KeywordAbility>()
            .Any(k => k.Keyword == "Flying").Should().BeTrue(
                "Vampire Nighthawk has Flying (CR 702.9)");
    }

    [Fact]
    public void VampireNighthawk_HasDeathtouchKeywordMarker()
    {
        var c = VampireNighthawkFactory.Create(_alice);

        c.Abilities.OfType<KeywordAbility>()
            .Any(k => k.Keyword == "Deathtouch").Should().BeTrue(
                "Vampire Nighthawk has Deathtouch (CR 702.2)");
    }

    [Fact]
    public void VampireNighthawk_HasLifelinkKeywordMarker()
    {
        var c = VampireNighthawkFactory.Create(_alice);

        c.Abilities.OfType<KeywordAbility>()
            .Any(k => k.Keyword == "Lifelink").Should().BeTrue(
                "Vampire Nighthawk has Lifelink (CR 702.15)");
    }

    [Fact]
    public void VampireNighthawk_HasExactlyThreeKeywords()
    {
        var c = VampireNighthawkFactory.Create(_alice);

        c.Abilities.OfType<KeywordAbility>().Should().HaveCount(3,
            "Flying, Deathtouch, and Lifelink are the only printed keywords");
    }

    [Fact]
    public void VampireNighthawk_NoTriggeredOrActivatedAbilities()
    {
        var c = VampireNighthawkFactory.Create(_alice);

        c.Abilities.OfType<TriggeredAbility>().Should().BeEmpty();
        c.Abilities.OfType<ActivatedAbility>().Should().BeEmpty();
    }
}
