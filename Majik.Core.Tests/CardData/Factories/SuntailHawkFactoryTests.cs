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
/// Unit tests for <see cref="SuntailHawkFactory"/>.
///
/// Card: Suntail Hawk — {W} Creature — Bird 1/1.
///   "Flying"
/// </summary>
[Trait("Color", "W")]
public class SuntailHawkFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void SuntailHawk_Identity()
    {
        var c = SuntailHawkFactory.Create(_alice);

        c.Name.Should().Be("Suntail Hawk");
        c.ManaCost.Should().Be("{W}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Bird).Should().BeTrue();
        c.BasePower.Should().Be(1);
        c.BaseToughness.Should().Be(1);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void SuntailHawk_IsWhite()
    {
        var c = SuntailHawkFactory.Create(_alice);

        CardColors.GetColors(c).Should().Contain(ManaColor.White,
            "Suntail Hawk has a {W} pip in its mana cost");
    }

    [Fact]
    public void SuntailHawk_ManaValueIsOne()
    {
        var c = SuntailHawkFactory.Create(_alice);

        // {W} → one coloured pip = mana value 1 (CR 202.3).
        ManaCost.Parse(c.ManaCost).TotalValue.Should().Be(1);
    }

    [Fact]
    public void SuntailHawk_HasFlyingKeywordMarker()
    {
        var c = SuntailHawkFactory.Create(_alice);

        c.Abilities.OfType<KeywordAbility>()
            .Any(k => k.Keyword == "Flying").Should().BeTrue(
                "Suntail Hawk ships with Flying as a KeywordAbility marker (CR 702.9)");
    }

    [Fact]
    public void SuntailHawk_NoOtherAbilities()
    {
        var c = SuntailHawkFactory.Create(_alice);

        c.Abilities.OfType<TriggeredAbility>().Should().BeEmpty();
        c.Abilities.OfType<ActivatedAbility>().Should().BeEmpty();
        c.Abilities.OfType<KeywordAbility>().Should().HaveCount(1,
            "Flying is the only printed keyword");
    }
}
