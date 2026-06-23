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
/// Unit tests for <see cref="FireElementalFactory"/>.
///
/// Card: Fire Elemental — Creature — Elemental {3}{R}{R} 5/4.
/// Vanilla — empty oracle text (verified against the embedded Modern seed,
/// Scryfall id dc506f58-048d-49cc-ad8c-2eb851b08bb6); no printed keywords,
/// triggers, statics, or activated abilities.
/// </summary>
[Trait("Color", "R")]
public class FireElementalFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void FireElemental_Identity()
    {
        var c = (Creature)NamedCardFactory.Create("Fire Elemental", _alice);

        c.Name.Should().Be("Fire Elemental");
        c.ManaCost.Should().Be("{3}{R}{R}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Elemental).Should().BeTrue();
        c.Power.Should().Be(5);
        c.Toughness.Should().Be(4);
        // {3}{R}{R} = 3 generic + 2 red = mana value 5 (CR 202.3); red colour
        // derived from the {R} pips (CR 105.2).
        CardColors.GetColors(c).Should().Contain(ManaColor.Red,
            "Fire Elemental has {R} in its mana cost");
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void FireElemental_IsVanilla_NoAbilities()
    {
        var c = (Creature)NamedCardFactory.Create("Fire Elemental", _alice);

        c.Abilities.OfType<KeywordAbility>().Should().BeEmpty(
            "Fire Elemental is vanilla — no printed keywords");
        c.Abilities.OfType<TriggeredAbility>().Should().BeEmpty(
            "Fire Elemental has no triggered abilities");
        c.Abilities.OfType<ActivatedAbility>().Should().BeEmpty(
            "Fire Elemental has no activated abilities");
    }
}
