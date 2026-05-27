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
/// Unit tests for <see cref="RumblingBalothFactory"/>.
///
/// Card: Rumbling Baloth — Creature — Beast {2}{G}{G} 4/4 (Magic 2011 /
/// Modern reprints). Vanilla — no printed keywords, triggers, statics, or
/// activated abilities.
/// </summary>
public class RumblingBalothFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void RumblingBaloth_Identity()
    {
        var c = RumblingBalothFactory.Create(_alice);

        c.Name.Should().Be("Rumbling Baloth");
        c.ManaCost.Should().Be("{2}{G}{G}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Beast).Should().BeTrue();
        c.Power.Should().Be(4);
        c.Toughness.Should().Be(4);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void RumblingBaloth_ManaValue_IsFour()
    {
        var c = RumblingBalothFactory.Create(_alice);

        c.ManaCost.Should().Be("{2}{G}{G}",
            "mana value 4: two generic plus two Green pips");
    }

    [Fact]
    public void RumblingBaloth_Colors_ContainsGreenOnly()
    {
        var c = RumblingBalothFactory.Create(_alice);

        var colors = CardColors.GetColors(c);
        colors.Should().Contain(ManaColor.Green, "Rumbling Baloth costs {2}{G}{G}");
        colors.Should().HaveCount(1, "Rumbling Baloth is exactly Green");
    }

    [Fact]
    public void RumblingBaloth_DispatchesViaNamedCardFactory()
    {
        var c = NamedCardFactory.Create("Rumbling Baloth", _alice);

        c.Should().BeOfType<Creature>();
        c.Name.Should().Be("Rumbling Baloth");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Beast).Should().BeTrue();
    }

    [Fact]
    public void RumblingBaloth_IsVanilla_NoAbilities()
    {
        var c = RumblingBalothFactory.Create(_alice);

        c.Abilities.OfType<KeywordAbility>().Should().BeEmpty(
            "Rumbling Baloth is vanilla — no printed keywords");
        c.Abilities.OfType<TriggeredAbility>().Should().BeEmpty(
            "Rumbling Baloth has no triggered abilities");
        c.Abilities.OfType<ActivatedAbility>().Should().BeEmpty(
            "Rumbling Baloth has no activated abilities");
    }
}
