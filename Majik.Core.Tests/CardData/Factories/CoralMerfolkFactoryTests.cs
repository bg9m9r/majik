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
/// Unit tests for <see cref="CoralMerfolkFactory"/>.
///
/// Card: Coral Merfolk — Creature — Merfolk {1}{U} 2/1 (Alpha / Revised /
/// Modern reprints). Vanilla — no printed keywords, triggers, statics, or
/// activated abilities.
/// </summary>
[Trait("Color", "U")]
public class CoralMerfolkFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void CoralMerfolk_Identity()
    {
        var c = CoralMerfolkFactory.Create(_alice);

        c.Name.Should().Be("Coral Merfolk");
        c.ManaCost.Should().Be("{1}{U}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Merfolk).Should().BeTrue();
        c.Power.Should().Be(2);
        c.Toughness.Should().Be(1);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void CoralMerfolk_ManaValue_IsTwo()
    {
        var c = CoralMerfolkFactory.Create(_alice);

        c.ManaCost.Should().Be("{1}{U}",
            "mana value 2: one generic pip + one Blue pip");
    }

    [Fact]
    public void CoralMerfolk_Colors_ContainsBlueOnly()
    {
        var c = CoralMerfolkFactory.Create(_alice);

        var colors = CardColors.GetColors(c);
        colors.Should().Contain(ManaColor.Blue, "Coral Merfolk costs {1}{U}");
        colors.Should().HaveCount(1, "Coral Merfolk is exactly Blue");
    }
    [Fact]
    public void CoralMerfolk_IsVanilla_NoAbilities()
    {
        var c = CoralMerfolkFactory.Create(_alice);

        c.Abilities.OfType<KeywordAbility>().Should().BeEmpty(
            "Coral Merfolk is vanilla — no printed keywords");
        c.Abilities.OfType<TriggeredAbility>().Should().BeEmpty(
            "Coral Merfolk has no triggered abilities");
        c.Abilities.OfType<ActivatedAbility>().Should().BeEmpty(
            "Coral Merfolk has no activated abilities");
    }
}
