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
/// Unit tests for <see cref="StormCrowFactory"/>.
///
/// Card: Storm Crow — {1}{U} Creature — Bird 1/2.
///   "Flying"
/// </summary>
[Trait("Color", "U")]
public class StormCrowFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void StormCrow_Identity()
    {
        var c = StormCrowFactory.Create(_alice);

        c.Name.Should().Be("Storm Crow");
        c.ManaCost.Should().Be("{1}{U}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Bird).Should().BeTrue();
        c.Power.Should().Be(1);
        c.Toughness.Should().Be(2);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void StormCrow_IsBlue()
    {
        var c = StormCrowFactory.Create(_alice);

        CardColors.GetColors(c).Should().Contain(ManaColor.Blue,
            "Storm Crow has a {U} pip in its mana cost");
    }

    [Fact]
    public void StormCrow_ManaValueIsTwo()
    {
        var c = StormCrowFactory.Create(_alice);

        // {1}{U} → generic 1 + one coloured pip = mana value 2 (CR 202.3).
        ManaCost.Parse(c.ManaCost).TotalValue.Should().Be(2);
    }

    [Fact]
    public void StormCrow_HasFlyingKeywordMarker()
    {
        var c = StormCrowFactory.Create(_alice);

        c.Abilities.OfType<KeywordAbility>()
            .Any(k => k.Keyword == "Flying").Should().BeTrue(
                "Storm Crow ships with Flying as a KeywordAbility marker (CR 702.9)");
    }

    [Fact]
    public void StormCrow_NoOtherAbilities()
    {
        var c = StormCrowFactory.Create(_alice);

        c.Abilities.OfType<TriggeredAbility>().Should().BeEmpty();
        c.Abilities.OfType<ActivatedAbility>().Should().BeEmpty();
        c.Abilities.OfType<KeywordAbility>().Should().HaveCount(1,
            "Flying is the only printed keyword");
    }
}
