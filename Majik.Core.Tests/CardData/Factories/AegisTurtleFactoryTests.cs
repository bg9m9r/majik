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
/// Unit tests for <see cref="AegisTurtleFactory"/>.
///
/// Card: Aegis Turtle — Creature — Turtle {U} 0/5 (Core Set 2021).
/// Vanilla — no printed keywords, triggers, statics, or activated abilities.
/// </summary>
[Trait("Color", "U")]
public class AegisTurtleFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void AegisTurtle_Identity()
    {
        var c = AegisTurtleFactory.Create(_alice);

        c.Name.Should().Be("Aegis Turtle");
        c.ManaCost.Should().Be("{U}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Turtle).Should().BeTrue();
        c.Power.Should().Be(0);
        c.Toughness.Should().Be(5);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void AegisTurtle_ManaValue_IsOne()
    {
        var c = AegisTurtleFactory.Create(_alice);

        c.ManaCost.Should().Be("{U}",
            "mana value 1: one Blue pip, zero generic");
    }

    [Fact]
    public void AegisTurtle_Colors_ContainsBlueOnly()
    {
        var c = AegisTurtleFactory.Create(_alice);

        var colors = CardColors.GetColors(c);
        colors.Should().Contain(ManaColor.Blue, "Aegis Turtle costs {U}");
        colors.Should().HaveCount(1, "Aegis Turtle is exactly Blue");
    }
    [Fact]
    public void AegisTurtle_IsVanilla_NoAbilities()
    {
        var c = AegisTurtleFactory.Create(_alice);

        c.Abilities.OfType<KeywordAbility>().Should().BeEmpty(
            "Aegis Turtle is vanilla — no printed keywords");
        c.Abilities.OfType<TriggeredAbility>().Should().BeEmpty(
            "Aegis Turtle has no triggered abilities");
        c.Abilities.OfType<ActivatedAbility>().Should().BeEmpty(
            "Aegis Turtle has no activated abilities");
    }
}
