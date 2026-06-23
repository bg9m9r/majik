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
/// Unit tests for <see cref="BearCubFactory"/>.
///
/// Card: Bear Cub — Creature — Bear {1}{G} 2/2.
/// Vanilla — empty oracle text (verified against Scryfall 2026-06); no printed
/// keywords, triggers, statics, or activated abilities. A functional reprint of
/// Grizzly Bears.
/// </summary>
[Trait("Color", "G")]
public class BearCubFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void BearCub_Identity()
    {
        var c = (Creature)NamedCardFactory.Create("Bear Cub", _alice);

        c.Name.Should().Be("Bear Cub");
        c.ManaCost.Should().Be("{1}{G}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Bear).Should().BeTrue();
        c.Power.Should().Be(2);
        c.Toughness.Should().Be(2);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);

        // {1}{G} = 1 generic + 1 green = mana value 2 (CR 202.3).
        CardColors.GetColors(c).Should().Contain(ManaColor.Green,
            "Bear Cub has {G} in its mana cost");
    }

    [Fact]
    public void BearCub_IsVanilla_NoAbilities()
    {
        var c = (Creature)NamedCardFactory.Create("Bear Cub", _alice);

        c.Abilities.OfType<KeywordAbility>().Should().BeEmpty(
            "Bear Cub is vanilla — no printed keywords");
        c.Abilities.OfType<TriggeredAbility>().Should().BeEmpty(
            "Bear Cub has no triggered abilities");
        c.Abilities.OfType<ActivatedAbility>().Should().BeEmpty(
            "Bear Cub has no activated abilities");
    }
}
