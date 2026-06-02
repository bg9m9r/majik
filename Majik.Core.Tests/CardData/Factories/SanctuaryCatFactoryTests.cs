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
/// Unit tests for <see cref="SanctuaryCatFactory"/>.
///
/// Card: Sanctuary Cat — Creature — Cat {W} 1/2 (Amonkhet).
/// Vanilla — no printed keywords, triggers, statics, or activated abilities.
/// </summary>
[Trait("Color", "W")]
public class SanctuaryCatFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void SanctuaryCat_Identity()
    {
        var c = SanctuaryCatFactory.Create(_alice);

        c.Name.Should().Be("Sanctuary Cat");
        c.ManaCost.Should().Be("{W}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Cat).Should().BeTrue();
        c.Power.Should().Be(1);
        c.Toughness.Should().Be(2);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void SanctuaryCat_ManaValue_IsOne()
    {
        var c = SanctuaryCatFactory.Create(_alice);

        c.ManaCost.Should().Be("{W}",
            "mana value 1: one White pip, zero generic");
    }

    [Fact]
    public void SanctuaryCat_Colors_ContainsWhiteOnly()
    {
        var c = SanctuaryCatFactory.Create(_alice);

        var colors = CardColors.GetColors(c);
        colors.Should().Contain(ManaColor.White, "Sanctuary Cat costs {W}");
        colors.Should().HaveCount(1, "Sanctuary Cat is exactly White");
    }
    [Fact]
    public void SanctuaryCat_IsVanilla_NoAbilities()
    {
        var c = SanctuaryCatFactory.Create(_alice);

        c.Abilities.OfType<KeywordAbility>().Should().BeEmpty(
            "Sanctuary Cat is vanilla — no printed keywords");
        c.Abilities.OfType<TriggeredAbility>().Should().BeEmpty(
            "Sanctuary Cat has no triggered abilities");
        c.Abilities.OfType<ActivatedAbility>().Should().BeEmpty(
            "Sanctuary Cat has no activated abilities");
    }
}
