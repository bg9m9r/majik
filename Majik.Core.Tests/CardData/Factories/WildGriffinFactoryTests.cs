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
/// Unit tests for <see cref="WildGriffinFactory"/>.
///
/// Card: Wild Griffin — {2}{W} Creature — Griffin 2/2.
///   "Flying"
/// </summary>
[Trait("Color", "W")]
public class WildGriffinFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void WildGriffin_Identity()
    {
        var c = WildGriffinFactory.Create(_alice);

        c.Name.Should().Be("Wild Griffin");
        c.ManaCost.Should().Be("{2}{W}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Griffin).Should().BeTrue();
        c.BasePower.Should().Be(2);
        c.BaseToughness.Should().Be(2);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void WildGriffin_IsWhite()
    {
        var c = WildGriffinFactory.Create(_alice);

        CardColors.GetColors(c).Should().Contain(ManaColor.White,
            "Wild Griffin has a {W} pip in its mana cost");
    }

    [Fact]
    public void WildGriffin_ManaValueIsThree()
    {
        var c = WildGriffinFactory.Create(_alice);

        // {2}{W} → generic 2 + one coloured pip = mana value 3 (CR 202.3).
        ManaCost.Parse(c.ManaCost).TotalValue.Should().Be(3);
    }

    [Fact]
    public void WildGriffin_HasFlyingKeywordMarker()
    {
        var c = WildGriffinFactory.Create(_alice);

        c.Abilities.OfType<KeywordAbility>()
            .Any(k => k.Keyword == "Flying").Should().BeTrue(
                "Wild Griffin ships with Flying as a KeywordAbility marker (CR 702.9)");
    }

    [Fact]
    public void WildGriffin_NoOtherAbilities()
    {
        var c = WildGriffinFactory.Create(_alice);

        c.Abilities.OfType<TriggeredAbility>().Should().BeEmpty();
        c.Abilities.OfType<ActivatedAbility>().Should().BeEmpty();
        c.Abilities.OfType<KeywordAbility>().Should().HaveCount(1,
            "Flying is the only printed keyword");
    }
}
