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
/// Unit tests for <see cref="IsamaruFactory"/>.
///
/// Card: Isamaru, Hound of Konda — Legendary Creature — Dog {W} 2/2 (Champions
/// of Kamigawa / Modern reprints). Vanilla — no printed keywords, triggers,
/// statics, or activated abilities.
/// </summary>
public class IsamaruFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void Isamaru_Identity()
    {
        var c = IsamaruFactory.Create(_alice);

        c.Name.Should().Be("Isamaru, Hound of Konda");
        c.ManaCost.Should().Be("{W}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSupertype(CardSupertype.Legendary).Should().BeTrue();
        c.HasSubtype(CardSubtype.Dog).Should().BeTrue();
        c.Power.Should().Be(2);
        c.Toughness.Should().Be(2);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void Isamaru_ManaValue_IsOne()
    {
        var c = IsamaruFactory.Create(_alice);

        c.ManaCost.Should().Be("{W}",
            "mana value 1: one White pip, zero generic");
    }

    [Fact]
    public void Isamaru_Colors_ContainsWhiteOnly()
    {
        var c = IsamaruFactory.Create(_alice);

        var colors = CardColors.GetColors(c);
        colors.Should().Contain(ManaColor.White, "Isamaru costs {W}");
        colors.Should().HaveCount(1, "Isamaru is exactly White");
    }

    [Fact]
    public void Isamaru_IsLegendary()
    {
        var c = IsamaruFactory.Create(_alice);

        c.HasSupertype(CardSupertype.Legendary).Should().BeTrue(
            "Isamaru, Hound of Konda is a Legendary Creature (CR 205.4)");
    }

    [Fact]
    public void Isamaru_DispatchesViaNamedCardFactory()
    {
        var c = NamedCardFactory.Create("Isamaru, Hound of Konda", _alice);

        c.Should().BeOfType<Creature>();
        c.Name.Should().Be("Isamaru, Hound of Konda");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSupertype(CardSupertype.Legendary).Should().BeTrue();
        c.HasSubtype(CardSubtype.Dog).Should().BeTrue();
    }

    [Fact]
    public void Isamaru_IsVanilla_NoAbilities()
    {
        var c = IsamaruFactory.Create(_alice);

        c.Abilities.OfType<KeywordAbility>().Should().BeEmpty(
            "Isamaru is vanilla — no printed keywords");
        c.Abilities.OfType<TriggeredAbility>().Should().BeEmpty(
            "Isamaru has no triggered abilities");
        c.Abilities.OfType<ActivatedAbility>().Should().BeEmpty(
            "Isamaru has no activated abilities");
    }
}
