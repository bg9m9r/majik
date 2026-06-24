using System.Linq;
using FluentAssertions;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Combat;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for <see cref="FearOfIsolationFactory"/>.
///
/// Fear of Isolation — {1}{U} Enchantment Creature — Nightmare 2/3:
///   "As an additional cost to cast this spell, return a permanent you control
///    to its owner's hand.
///    Flying"
///
/// Same Nightmare enchantment-creature flyer shape as
/// <see cref="FearOfTheDarkFactory"/>; the additional-cost self-bounce reuses
/// the "return a permanent you control" payment shape of
/// <see cref="KorSkyfisherFactory"/>, modelled at resolve (documented deviation,
/// same posture as <see cref="ThrillOfPossibilityFactory"/>).
///
/// Covers only the card's UNIQUE behaviour (the additional-cost return + Flying)
/// plus a single identity assert. Dispatch + well-formedness are covered for
/// every implemented card by CardFactoryContractTests.
/// </summary>
[Trait("Color", "U")]
public class FearOfIsolationFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void FearOfIsolation_IsBlueNightmareEnchantmentCreatureFlyer_2_3()
    {
        var card = FearOfIsolationFactory.Create(_alice);

        card.Should().BeOfType<Creature>();
        card.Name.Should().Be("Fear of Isolation");
        card.HasType(CardType.Creature).Should().BeTrue();
        card.HasType(CardType.Enchantment).Should().BeTrue("Enchantment Creature (CR 301.1)");
        card.HasSubtype(CardSubtype.Nightmare).Should().BeTrue();
        card.BasePower.Should().Be(2);
        card.BaseToughness.Should().Be(3);
        card.ManaCostValue.TotalValue.Should().Be(2, "{1}{U} is mana value 2");
        CardColors.GetColors(card).Should().Contain(ManaColor.Blue);
        CombatAbilities.HasFlying(card).Should().BeTrue("CR 702.9 — Flying");
    }

    [Fact]
    public void AdditionalCost_ReturnsAControlledPermanentToOwnersHand()
    {
        // A land Alice controls is the eligible permanent to return.
        var land = new Land("Island") { Owner = _alice, Controller = _alice };
        _alice.Zones.Battlefield.AddCard(land);
        land.SetZone(ZoneType.Battlefield);

        // Fear of Isolation is being cast — pay the additional cost. It isn't a
        // permanent yet, so it can't be the returned permanent (CR 601.2g).
        var self = FearOfIsolationFactory.Create(_alice);

        var effects = FearOfIsolationFactory.BuildAdditionalCostPayment(_alice, self);
        foreach (var e in effects) e.Execute();

        // CR 701.10 — the controlled permanent went to its owner's hand.
        _alice.Zones.Battlefield.GetCards().Should().NotContain(land);
        _alice.Zones.Hand.GetCards().Should().ContainSingle()
            .Which.Should().BeSameAs(land);
        land.Zone.Should().Be(ZoneType.Hand);
    }

    [Fact]
    public void AdditionalCost_NoEligiblePermanent_IsNoOp()
    {
        // Caster controls nothing else — printed text forbids the cast; v1's
        // resolve-side payment is a documented no-op (deviation).
        var self = FearOfIsolationFactory.Create(_alice);

        var effects = FearOfIsolationFactory.BuildAdditionalCostPayment(_alice, self);
        foreach (var e in effects) e.Execute();

        _alice.Zones.Hand.GetCards().Should().BeEmpty();
        _alice.Zones.Battlefield.GetCards().Should().BeEmpty();
    }
}
