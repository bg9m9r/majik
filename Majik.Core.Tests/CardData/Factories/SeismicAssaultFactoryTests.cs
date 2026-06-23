using System.Linq;
using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="SeismicAssaultFactory"/> — Seismic Assault
/// (Tempest / Modern Horizons, Enchantment {R}{R}{R}).
///
/// Oracle text (verified against Scryfall, 2026-06-23):
///   "Discard a land card: This enchantment deals 2 damage to any target."
///
/// Covers the card's UNIQUE behaviour:
/// - Identity ({R}{R}{R} Enchantment).
/// - Discard-a-land burn: cost is a single DiscardALandCardCost + one 1..1
///   "any target" request (CR 118.5).
/// - Cost gate: cannot pay without a land card in hand; paying discards it
///   (CR 701.16a).
/// - Resolution deals 2 damage to a creature / player target (CR 119.3 /
///   120.3).
/// </summary>
[Trait("Color", "R")]
public class SeismicAssaultFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void SeismicAssault_Identity_EnchantmentAtCostRRR()
    {
        var card = SeismicAssaultFactory.Create(_alice);

        card.Name.Should().Be("Seismic Assault");
        card.Should().BeOfType<Enchantment>();
        card.HasType(CardType.Enchantment).Should().BeTrue();
        card.ManaCost.Should().Be("{R}{R}{R}", "Seismic Assault costs {R}{R}{R}");
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    // -----------------------------------------------------------------------
    // Discard-a-land burn ability
    // -----------------------------------------------------------------------

    [Fact]
    public void BurnAbility_HasDiscardALandCardCost_AndOneAnyTarget()
    {
        var card = SeismicAssaultFactory.Create(_alice);
        var burn = card.Abilities.OfType<ActivatedAbility>().Single();

        burn.Costs.OfType<DiscardALandCardCost>().Should().ContainSingle(
            "the discard-a-land cost (CR 118.5)");
        burn.TargetRequests.Should().HaveCount(1);
        burn.TargetRequests[0].MinTargets.Should().Be(1);
        burn.TargetRequests[0].MaxTargets.Should().Be(1);
    }

    [Fact]
    public void BurnCost_CannotPayWithoutALandCardInHand()
    {
        var card = SeismicAssaultFactory.Create(_alice);
        var cost = card.Abilities.OfType<ActivatedAbility>().Single()
            .Costs.OfType<DiscardALandCardCost>().Single();

        // A creature card in hand does not satisfy "discard a land card".
        var bear = new Creature("Grizzly Bears", "1G", 2, 2);
        bear.SetOwner(_alice);
        _alice.Zones.Hand.AddCard(bear);

        cost.CanPay(_alice).Should().BeFalse("no land card in hand to discard");
    }

    [Fact]
    public void BurnCost_CanPayWithALandCardInHand_AndDiscardsIt()
    {
        var card = SeismicAssaultFactory.Create(_alice);
        var cost = card.Abilities.OfType<ActivatedAbility>().Single()
            .Costs.OfType<DiscardALandCardCost>().Single();

        var land = new Land("Mountain", subtypes: new[] { CardSubtype.Mountain });
        land.SetOwner(_alice);
        _alice.Zones.Hand.AddCard(land);
        land.SetZone(ZoneType.Hand);

        cost.CanPay(_alice).Should().BeTrue("a land card is available to discard");

        cost.Pay(_alice);
        _alice.Zones.Hand.GetCards().Should().NotContain(land);
        _alice.Zones.Graveyard.GetCards().Should().Contain(land,
            "CR 701.16a — the discarded land card moves to the graveyard");
    }

    [Fact]
    public void BurnEffect_DealsTwoDamageToTargetCreature()
    {
        var bob = new Player("Bob", 20);
        var card = SeismicAssaultFactory.Create(_alice);

        var target = new Creature("Wall of Roots", "1G", 0, 5);
        target.SetOwner(bob);
        target.SetController(bob);
        target.SetZone(ZoneType.Battlefield);

        var burn = card.Abilities.OfType<ActivatedAbility>().Single();
        burn.SetChosenTargets(new IReadOnlyList<object>[] { new object[] { target } });
        foreach (var effect in burn.Effects) effect.Execute();

        target.Damage.Should().Be(2, "deals 2 damage to any target (CR 120.3)");
    }

    [Fact]
    public void BurnEffect_DealsTwoDamageToPlayer()
    {
        var bob = new Player("Bob", 20);
        var card = SeismicAssaultFactory.Create(_alice);

        var burn = card.Abilities.OfType<ActivatedAbility>().Single();
        burn.SetChosenTargets(new IReadOnlyList<object>[] { new object[] { bob } });
        foreach (var effect in burn.Effects) effect.Execute();

        bob.LifeTotal.Should().Be(18, "20 - 2 = 18 (CR 119.3)");
    }
}
