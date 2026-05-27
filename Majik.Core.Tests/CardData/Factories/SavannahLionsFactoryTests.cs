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
/// Unit tests for <see cref="SavannahLionsFactory"/>.
///
/// Card: Savannah Lions — Creature — Cat {W} 2/1 (Alpha / Revised / Modern
/// reprints). Vanilla — no printed keywords, triggers, statics, or activated
/// abilities.
/// </summary>
public class SavannahLionsFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void SavannahLions_Identity()
    {
        var c = SavannahLionsFactory.Create(_alice);

        c.Name.Should().Be("Savannah Lions");
        c.ManaCost.Should().Be("{W}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Cat).Should().BeTrue();
        c.Power.Should().Be(2);
        c.Toughness.Should().Be(1);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void SavannahLions_ManaValue_IsOne()
    {
        var c = SavannahLionsFactory.Create(_alice);

        c.ManaCost.Should().Be("{W}",
            "mana value 1: one White pip, zero generic");
    }

    [Fact]
    public void SavannahLions_Colors_ContainsWhiteOnly()
    {
        var c = SavannahLionsFactory.Create(_alice);

        var colors = CardColors.GetColors(c);
        colors.Should().Contain(ManaColor.White, "Savannah Lions costs {W}");
        colors.Should().HaveCount(1, "Savannah Lions is exactly White");
    }

    [Fact]
    public void SavannahLions_DispatchesViaNamedCardFactory()
    {
        var c = NamedCardFactory.Create("Savannah Lions", _alice);

        c.Should().BeOfType<Creature>();
        c.Name.Should().Be("Savannah Lions");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Cat).Should().BeTrue();
    }

    [Fact]
    public void SavannahLions_IsVanilla_NoAbilities()
    {
        var c = SavannahLionsFactory.Create(_alice);

        c.Abilities.OfType<KeywordAbility>().Should().BeEmpty(
            "Savannah Lions is vanilla — no printed keywords");
        c.Abilities.OfType<TriggeredAbility>().Should().BeEmpty(
            "Savannah Lions has no triggered abilities");
        c.Abilities.OfType<ActivatedAbility>().Should().BeEmpty(
            "Savannah Lions has no activated abilities");
    }
}
