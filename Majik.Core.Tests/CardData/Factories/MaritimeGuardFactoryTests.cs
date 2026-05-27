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
/// Unit tests for <see cref="MaritimeGuardFactory"/>.
///
/// Card: Maritime Guard — Creature — Merfolk Soldier {1}{U} 1/3 (Portal /
/// Portal Second Age / Seventh Edition / various reprints). Vanilla — no
/// printed keywords, triggers, statics, or activated abilities.
///
/// Covers:
/// - Identity (name, mana cost, Creature type, Merfolk + Soldier subtypes, 1/3).
/// - Blue colour identity via CardColors.GetColors (CR 105 — colour from pips).
/// - Mana value 2 (CR 202.3 — {1} generic + {U} pip).
/// - Owner / controller stamped on creation.
/// - No abilities attached (vanilla card, CR 208.1).
/// - NamedCardFactory dispatch resolves the correct factory.
/// </summary>
public class MaritimeGuardFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void MaritimeGuard_Name()
    {
        var card = MaritimeGuardFactory.Create(_alice);

        card.Name.Should().Be("Maritime Guard");
    }

    [Fact]
    public void MaritimeGuard_ManaCost()
    {
        var card = MaritimeGuardFactory.Create(_alice);

        card.ManaCost.Should().Be("{1}{U}");
    }

    [Fact]
    public void MaritimeGuard_IsCreature()
    {
        var card = MaritimeGuardFactory.Create(_alice);

        card.HasType(CardType.Creature).Should().BeTrue();
        card.Should().BeOfType<Creature>();
    }

    [Fact]
    public void MaritimeGuard_HasMerfolkSubtype()
    {
        var card = MaritimeGuardFactory.Create(_alice);

        card.HasSubtype(CardSubtype.Merfolk).Should().BeTrue(
            "Maritime Guard's oracle type line reads 'Creature — Merfolk Soldier'");
    }

    [Fact]
    public void MaritimeGuard_HasSoldierSubtype()
    {
        var card = MaritimeGuardFactory.Create(_alice);

        card.HasSubtype(CardSubtype.Soldier).Should().BeTrue(
            "Maritime Guard's oracle type line reads 'Creature — Merfolk Soldier'");
    }

    [Fact]
    public void MaritimeGuard_Power1_Toughness3()
    {
        var card = (Creature)MaritimeGuardFactory.Create(_alice);

        card.BasePower.Should().Be(1,
            "Maritime Guard's printed P/T is 1/3");
        card.BaseToughness.Should().Be(3,
            "Maritime Guard's printed P/T is 1/3");
    }

    [Fact]
    public void MaritimeGuard_IsBlue()
    {
        var card = MaritimeGuardFactory.Create(_alice);

        CardColors.GetColors(card).Should().Contain(ManaColor.Blue,
            "Maritime Guard's mana cost {1}{U} contains a blue pip (CR 105)");
        CardColors.GetColors(card).Should().HaveCount(1,
            "Maritime Guard is mono-blue — no other colour pips in {1}{U}");
    }

    [Fact]
    public void MaritimeGuard_ManaValue2()
    {
        var card = MaritimeGuardFactory.Create(_alice);

        // {1}{U} → generic 1 + one coloured pip = mana value 2 (CR 202.3).
        ManaCost.Parse(card.ManaCost).TotalValue.Should().Be(2,
            "Maritime Guard's mana cost {1}{U} has mana value 2 (CR 202.3)");
    }

    [Fact]
    public void MaritimeGuard_OwnerAndControllerStamped()
    {
        var card = MaritimeGuardFactory.Create(_alice);

        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void MaritimeGuard_NoAbilities_Vanilla()
    {
        var card = MaritimeGuardFactory.Create(_alice);

        card.Abilities.OfType<KeywordAbility>().Should().BeEmpty(
            "Maritime Guard is vanilla — no printed keywords (CR 208.1)");
        card.Abilities.OfType<TriggeredAbility>().Should().BeEmpty(
            "Maritime Guard has no triggered abilities");
        card.Abilities.OfType<ActivatedAbility>().Should().BeEmpty(
            "Maritime Guard has no activated abilities");
    }

    [Fact]
    public void MaritimeGuard_DispatchesViaNamedCardFactory()
    {
        var card = NamedCardFactory.Create("Maritime Guard", _alice);

        card.Should().BeOfType<Creature>();
        card.Name.Should().Be("Maritime Guard");
        card.HasType(CardType.Creature).Should().BeTrue();
        card.HasSubtype(CardSubtype.Merfolk).Should().BeTrue();
        card.HasSubtype(CardSubtype.Soldier).Should().BeTrue();
        ((Creature)card).BasePower.Should().Be(1);
        ((Creature)card).BaseToughness.Should().Be(3);
    }
}
