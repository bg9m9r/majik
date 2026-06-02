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
/// Unit tests for <see cref="GrizzlyBearsFactory"/>.
///
/// Card: Grizzly Bears — Creature — Bear {1}{G} 2/2.
/// Vanilla — empty oracle text (verified against Scryfall); no printed
/// keywords, triggers, statics, or activated abilities. The proverbial
/// vanilla 2/2 against which other two-drops are measured.
/// </summary>
[Trait("Color", "G")]
public class GrizzlyBearsFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void GrizzlyBears_Identity()
    {
        var c = (Creature)NamedCardFactory.Create("Grizzly Bears", _alice);

        c.Name.Should().Be("Grizzly Bears");
        c.ManaCost.Should().Be("{1}{G}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Bear).Should().BeTrue();
        c.Power.Should().Be(2);
        c.Toughness.Should().Be(2);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void GrizzlyBears_ManaValue_IsTwo()
    {
        var c = (Creature)NamedCardFactory.Create("Grizzly Bears", _alice);

        // {1}{G} = 1 generic + 1 green = mana value 2 (CR 202.3).
        c.ManaCost.Should().Be("{1}{G}");
        CardColors.GetColors(c).Should().Contain(ManaColor.Green,
            "Grizzly Bears has {G} in its mana cost");
    }

    [Fact]
    public void GrizzlyBears_IsVanilla_NoAbilities()
    {
        var c = (Creature)NamedCardFactory.Create("Grizzly Bears", _alice);

        c.Abilities.OfType<KeywordAbility>().Should().BeEmpty(
            "Grizzly Bears is vanilla — no printed keywords");
        c.Abilities.OfType<TriggeredAbility>().Should().BeEmpty(
            "Grizzly Bears has no triggered abilities");
        c.Abilities.OfType<ActivatedAbility>().Should().BeEmpty(
            "Grizzly Bears has no activated abilities");
    }
}
