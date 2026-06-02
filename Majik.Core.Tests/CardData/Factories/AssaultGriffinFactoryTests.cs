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
/// Unit tests for <see cref="AssaultGriffinFactory"/>.
///
/// Card: Assault Griffin — {3}{W} Creature — Griffin 3/2.
///   "Flying"
/// </summary>
[Trait("Color", "W")]
public class AssaultGriffinFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void AssaultGriffin_Identity()
    {
        var c = AssaultGriffinFactory.Create(_alice);

        c.Name.Should().Be("Assault Griffin");
        c.ManaCost.Should().Be("{3}{W}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Griffin).Should().BeTrue();
        c.Power.Should().Be(3);
        c.Toughness.Should().Be(2);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void AssaultGriffin_IsWhite()
    {
        var c = AssaultGriffinFactory.Create(_alice);

        CardColors.GetColors(c).Should().Contain(ManaColor.White,
            "Assault Griffin has one {W} pip in its mana cost");
    }

    [Fact]
    public void AssaultGriffin_ManaValueIsFour()
    {
        var c = AssaultGriffinFactory.Create(_alice);

        // {3}{W} → generic 3 + one coloured pip = mana value 4 (CR 202.3).
        ManaCost.Parse(c.ManaCost).TotalValue.Should().Be(4);
    }

    [Fact]
    public void AssaultGriffin_HasFlyingKeywordMarker()
    {
        var c = AssaultGriffinFactory.Create(_alice);

        c.Abilities.OfType<KeywordAbility>()
            .Any(k => k.Keyword == "Flying").Should().BeTrue(
                "Assault Griffin ships with Flying as a KeywordAbility marker (CR 702.9)");
    }

    [Fact]
    public void AssaultGriffin_NoOtherAbilities()
    {
        var c = AssaultGriffinFactory.Create(_alice);

        c.Abilities.OfType<TriggeredAbility>().Should().BeEmpty();
        c.Abilities.OfType<ActivatedAbility>().Should().BeEmpty();
        c.Abilities.OfType<KeywordAbility>().Should().HaveCount(1,
            "Flying is the only printed keyword");
    }
}
