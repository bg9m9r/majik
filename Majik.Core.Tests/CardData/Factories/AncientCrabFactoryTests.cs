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
/// Unit tests for <see cref="AncientCrabFactory"/>.
///
/// Card: Ancient Crab — Creature — Crab {1}{U}{U} 1/5 (Amonkhet).
/// Vanilla — no printed keywords, triggers, statics, or activated abilities.
/// </summary>
public class AncientCrabFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void AncientCrab_Identity()
    {
        var c = AncientCrabFactory.Create(_alice);

        c.Name.Should().Be("Ancient Crab");
        c.ManaCost.Should().Be("{1}{U}{U}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Crab).Should().BeTrue();
        c.Power.Should().Be(1);
        c.Toughness.Should().Be(5);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void AncientCrab_ManaValue_IsThree()
    {
        var c = AncientCrabFactory.Create(_alice);

        c.ManaCost.Should().Be("{1}{U}{U}",
            "mana value 3: one generic pip plus two Blue pips");
    }

    [Fact]
    public void AncientCrab_Colors_ContainsBlueOnly()
    {
        var c = AncientCrabFactory.Create(_alice);

        var colors = CardColors.GetColors(c);
        colors.Should().Contain(ManaColor.Blue, "Ancient Crab costs {1}{U}{U}");
        colors.Should().HaveCount(1, "Ancient Crab is exactly Blue");
    }

    [Fact]
    public void AncientCrab_DispatchesViaNamedCardFactory()
    {
        var c = NamedCardFactory.Create("Ancient Crab", _alice);

        c.Should().BeOfType<Creature>();
        c.Name.Should().Be("Ancient Crab");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Crab).Should().BeTrue();
    }

    [Fact]
    public void AncientCrab_IsVanilla_NoAbilities()
    {
        var c = AncientCrabFactory.Create(_alice);

        c.Abilities.OfType<KeywordAbility>().Should().BeEmpty(
            "Ancient Crab is vanilla — no printed keywords");
        c.Abilities.OfType<TriggeredAbility>().Should().BeEmpty(
            "Ancient Crab has no triggered abilities");
        c.Abilities.OfType<ActivatedAbility>().Should().BeEmpty(
            "Ancient Crab has no activated abilities");
    }
}
