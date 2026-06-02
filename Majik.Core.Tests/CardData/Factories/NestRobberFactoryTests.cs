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
/// Unit tests for <see cref="NestRobberFactory"/>.
///
/// Card: Nest Robber — {1}{R} Creature — Dinosaur 2/1.
///   "Haste"
/// </summary>
[Trait("Color", "R")]
public class NestRobberFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void NestRobber_Identity()
    {
        var c = NestRobberFactory.Create(_alice);

        c.Name.Should().Be("Nest Robber");
        c.ManaCost.Should().Be("{1}{R}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Dinosaur).Should().BeTrue();
        c.Power.Should().Be(2);
        c.Toughness.Should().Be(1);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NestRobber_IsRed()
    {
        var c = NestRobberFactory.Create(_alice);

        CardColors.GetColors(c).Should().Contain(ManaColor.Red,
            "Nest Robber has one {R} pip in its mana cost");
    }

    [Fact]
    public void NestRobber_ManaValueIsTwo()
    {
        var c = NestRobberFactory.Create(_alice);

        // {1}{R} → generic 1 + one coloured pip = mana value 2 (CR 202.3).
        ManaCost.Parse(c.ManaCost).TotalValue.Should().Be(2);
    }

    [Fact]
    public void NestRobber_HasHasteKeywordMarker()
    {
        var c = NestRobberFactory.Create(_alice);

        c.Abilities.OfType<KeywordAbility>()
            .Any(k => k.Keyword == "Haste").Should().BeTrue(
                "Nest Robber ships with Haste as a KeywordAbility marker (CR 702.10)");
    }

    [Fact]
    public void NestRobber_NoOtherAbilities()
    {
        var c = NestRobberFactory.Create(_alice);

        c.Abilities.OfType<TriggeredAbility>().Should().BeEmpty();
        c.Abilities.OfType<ActivatedAbility>().Should().BeEmpty();
        c.Abilities.OfType<KeywordAbility>().Should().HaveCount(1,
            "Haste is the only printed keyword");
    }
}
