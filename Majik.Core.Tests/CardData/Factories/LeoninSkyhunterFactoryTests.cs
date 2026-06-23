using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="LeoninSkyhunterFactory"/>.
///
/// Card: Leonin Skyhunter — {W}{W} Creature — Cat Knight 2/2.
///   "Flying"
/// </summary>
[Trait("Color", "W")]
public class LeoninSkyhunterFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void LeoninSkyhunter_Identity()
    {
        var c = LeoninSkyhunterFactory.Create(_alice);

        c.Name.Should().Be("Leonin Skyhunter");
        c.ManaCost.Should().Be("{W}{W}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Cat).Should().BeTrue();
        c.HasSubtype(CardSubtype.Knight).Should().BeTrue();
        c.Power.Should().Be(2);
        c.Toughness.Should().Be(2);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void LeoninSkyhunter_IsWhite()
    {
        var c = LeoninSkyhunterFactory.Create(_alice);

        // {W}{W} → white color identity (CR 202.2c).
        CardColors.GetColors(c).Should().Contain(ManaColor.White,
            "Leonin Skyhunter has two {W} pips in its mana cost");
    }

    [Fact]
    public void LeoninSkyhunter_HasFlyingKeywordMarker()
    {
        var c = LeoninSkyhunterFactory.Create(_alice);

        c.Abilities.OfType<KeywordAbility>()
            .Any(k => k.Keyword == "Flying").Should().BeTrue(
                "Leonin Skyhunter ships with Flying as a KeywordAbility marker (CR 702.9)");
    }

    [Fact]
    public void LeoninSkyhunter_NoOtherAbilities()
    {
        var c = LeoninSkyhunterFactory.Create(_alice);

        c.Abilities.OfType<TriggeredAbility>().Should().BeEmpty();
        c.Abilities.OfType<ActivatedAbility>().Should().BeEmpty();
        c.Abilities.OfType<KeywordAbility>().Should().HaveCount(1,
            "Flying is the only printed keyword");
    }
}
