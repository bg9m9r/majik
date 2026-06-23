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
/// Unit tests for <see cref="SilvercoatLionFactory"/>.
///
/// Card: Silvercoat Lion — Creature — Cat {1}{W} 2/2.
/// Vanilla — empty oracle text (verified against Scryfall 2026-06); no printed
/// keywords, triggers, statics, or activated abilities. White's {1}{W} vanilla
/// 2/2, the counterpart to Grizzly Bears.
/// </summary>
[Trait("Color", "W")]
public class SilvercoatLionFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void SilvercoatLion_Identity()
    {
        var c = (Creature)NamedCardFactory.Create("Silvercoat Lion", _alice);

        c.Name.Should().Be("Silvercoat Lion");
        c.ManaCost.Should().Be("{1}{W}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Cat).Should().BeTrue();
        c.Power.Should().Be(2);
        c.Toughness.Should().Be(2);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);

        // {1}{W} = 1 generic + 1 white = mana value 2 (CR 202.3).
        CardColors.GetColors(c).Should().Contain(ManaColor.White,
            "Silvercoat Lion has {W} in its mana cost");
    }

    [Fact]
    public void SilvercoatLion_IsVanilla_NoAbilities()
    {
        var c = (Creature)NamedCardFactory.Create("Silvercoat Lion", _alice);

        c.Abilities.OfType<KeywordAbility>().Should().BeEmpty(
            "Silvercoat Lion is vanilla — no printed keywords");
        c.Abilities.OfType<TriggeredAbility>().Should().BeEmpty(
            "Silvercoat Lion has no triggered abilities");
        c.Abilities.OfType<ActivatedAbility>().Should().BeEmpty(
            "Silvercoat Lion has no activated abilities");
    }
}
