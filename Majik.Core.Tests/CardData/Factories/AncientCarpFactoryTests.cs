using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="AncientCarpFactory"/>.
///
/// Card: Ancient Carp — {4}{U} Creature — Fish 2/5 (Modern Horizons).
/// Vanilla — no printed keywords, triggers, statics, or activated abilities.
/// </summary>
[Trait("Color", "U")]
public class AncientCarpFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void AncientCarp_Identity()
    {
        var c = AncientCarpFactory.Create(_alice);

        c.Name.Should().Be("Ancient Carp");
        c.ManaCost.Should().Be("{4}{U}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Fish).Should().BeTrue();
        c.Power.Should().Be(2);
        c.Toughness.Should().Be(5);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void AncientCarp_IsBlue()
    {
        var c = AncientCarpFactory.Create(_alice);

        CardColors.GetColors(c).Should().Contain(ManaColor.Blue,
            "mana cost {4}{U} makes Ancient Carp blue — CR 105.2");
    }

    [Fact]
    public void AncientCarp_ManaValue_IsFive()
    {
        var c = AncientCarpFactory.Create(_alice);

        c.ManaCostValue.TotalValue.Should().Be(5,
            "4 generic + 1 blue = MV 5 — CR 202.3");
    }

    [Fact]
    public void AncientCarp_IsVanilla_NoAbilities()
    {
        var c = AncientCarpFactory.Create(_alice);

        c.Abilities.OfType<KeywordAbility>().Should().BeEmpty(
            "Ancient Carp is vanilla — no printed keywords");
        c.Abilities.OfType<TriggeredAbility>().Should().BeEmpty(
            "Ancient Carp has no triggered abilities");
        c.Abilities.OfType<ActivatedAbility>().Should().BeEmpty(
            "Ancient Carp has no activated abilities");
    }
}
