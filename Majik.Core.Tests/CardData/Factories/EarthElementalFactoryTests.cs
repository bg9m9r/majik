using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="EarthElementalFactory"/>.
///
/// Card: Earth Elemental — Creature — Elemental {3}{R}{R} 4/5.
/// Vanilla — empty oracle text (verified against Scryfall 2026-06); no printed
/// keywords, triggers, statics, or activated abilities. A red beatstick.
/// </summary>
[Trait("Color", "R")]
public class EarthElementalFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void EarthElemental_Identity()
    {
        var c = (Creature)NamedCardFactory.Create("Earth Elemental", _alice);

        c.Name.Should().Be("Earth Elemental");
        c.ManaCost.Should().Be("{3}{R}{R}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Elemental).Should().BeTrue();
        c.Power.Should().Be(4);
        c.Toughness.Should().Be(5);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);

        // {3}{R}{R} = 3 generic + 2 red = mana value 5 (CR 202.3); exactly Red.
        var colors = CardColors.GetColors(c);
        colors.Should().Contain(ManaColor.Red, "Earth Elemental costs {3}{R}{R}");
        colors.Should().HaveCount(1, "Earth Elemental is exactly Red");
    }

    [Fact]
    public void EarthElemental_IsVanilla_NoAbilities()
    {
        var c = (Creature)NamedCardFactory.Create("Earth Elemental", _alice);

        c.Abilities.OfType<KeywordAbility>().Should().BeEmpty(
            "Earth Elemental is vanilla — no printed keywords");
        c.Abilities.OfType<TriggeredAbility>().Should().BeEmpty(
            "Earth Elemental has no triggered abilities");
        c.Abilities.OfType<ActivatedAbility>().Should().BeEmpty(
            "Earth Elemental has no activated abilities");
    }
}
