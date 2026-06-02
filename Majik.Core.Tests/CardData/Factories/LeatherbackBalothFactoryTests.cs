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
/// Unit tests for <see cref="LeatherbackBalothFactory"/>.
///
/// Card: Leatherback Baloth — Creature — Beast {G}{G}{G} 4/5 (Worldwake /
/// Modern reprints). Vanilla — no printed keywords, triggers, statics, or
/// activated abilities.
/// </summary>
[Trait("Color", "G")]
public class LeatherbackBalothFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void LeatherbackBaloth_Identity()
    {
        var c = LeatherbackBalothFactory.Create(_alice);

        c.Name.Should().Be("Leatherback Baloth");
        c.ManaCost.Should().Be("{G}{G}{G}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Beast).Should().BeTrue();
        c.Power.Should().Be(4);
        c.Toughness.Should().Be(5);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void LeatherbackBaloth_ManaValue_IsThree()
    {
        var c = LeatherbackBalothFactory.Create(_alice);

        c.ManaCost.Should().Be("{G}{G}{G}",
            "mana value 3: three Green pips, zero generic");
    }

    [Fact]
    public void LeatherbackBaloth_Colors_ContainsGreenOnly()
    {
        var c = LeatherbackBalothFactory.Create(_alice);

        var colors = CardColors.GetColors(c);
        colors.Should().Contain(ManaColor.Green, "Leatherback Baloth costs {G}{G}{G}");
        colors.Should().HaveCount(1, "Leatherback Baloth is exactly Green");
    }
    [Fact]
    public void LeatherbackBaloth_IsVanilla_NoAbilities()
    {
        var c = LeatherbackBalothFactory.Create(_alice);

        c.Abilities.OfType<KeywordAbility>().Should().BeEmpty(
            "Leatherback Baloth is vanilla — no printed keywords");
        c.Abilities.OfType<TriggeredAbility>().Should().BeEmpty(
            "Leatherback Baloth has no triggered abilities");
        c.Abilities.OfType<ActivatedAbility>().Should().BeEmpty(
            "Leatherback Baloth has no activated abilities");
    }
}
