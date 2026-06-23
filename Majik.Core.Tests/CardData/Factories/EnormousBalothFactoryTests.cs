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
/// Unit tests for <see cref="EnormousBalothFactory"/>.
///
/// Card: Enormous Baloth — Creature — Beast {6}{G} 7/7.
/// Vanilla — empty oracle text (verified against Scryfall 2026-06); no printed
/// keywords, triggers, statics, or activated abilities. A green seven-drop
/// beatstick.
/// </summary>
[Trait("Color", "G")]
public class EnormousBalothFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void EnormousBaloth_Identity()
    {
        var c = (Creature)NamedCardFactory.Create("Enormous Baloth", _alice);

        c.Name.Should().Be("Enormous Baloth");
        c.ManaCost.Should().Be("{6}{G}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Beast).Should().BeTrue();
        c.Power.Should().Be(7);
        c.Toughness.Should().Be(7);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void EnormousBaloth_ManaValue_IsSeven()
    {
        var c = (Creature)NamedCardFactory.Create("Enormous Baloth", _alice);

        // {6}{G} = 6 generic + 1 green = mana value 7 (CR 202.3).
        c.ManaCost.Should().Be("{6}{G}");
        CardColors.GetColors(c).Should().Contain(ManaColor.Green,
            "Enormous Baloth has {G} in its mana cost");
        CardColors.GetColors(c).Should().HaveCount(1,
            "Enormous Baloth is exactly Green");
    }

    [Fact]
    public void EnormousBaloth_IsVanilla_NoAbilities()
    {
        var c = (Creature)NamedCardFactory.Create("Enormous Baloth", _alice);

        c.Abilities.OfType<KeywordAbility>().Should().BeEmpty(
            "Enormous Baloth is vanilla — no printed keywords");
        c.Abilities.OfType<TriggeredAbility>().Should().BeEmpty(
            "Enormous Baloth has no triggered abilities");
        c.Abilities.OfType<ActivatedAbility>().Should().BeEmpty(
            "Enormous Baloth has no activated abilities");
    }
}
