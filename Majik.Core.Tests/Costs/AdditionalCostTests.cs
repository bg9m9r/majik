using System.Collections.Generic;
using FluentAssertions;
using Majik.Core.Cards;
using Majik.Core.Costs;
using Majik.Core.Domain.Exceptions;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.Costs;

/// <summary>
/// Unit tests for AdditionalCost.
/// Tests tap, sacrifice, and life costs.
/// </summary>
public class AdditionalCostTests
{
    [Fact]
    public void Tap_ValidPermanent_CreatesTapCost()
    {
        // Arrange
        var permanent = new Creature("Grizzly Bears", "1G", 2, 2);

        // Act
        var cost = AdditionalCost.Tap(permanent);

        // Assert
        cost.CostType.Should().Be(AdditionalCostType.Tap);
        cost.Description.Should().Contain("Tap");
    }

    [Fact]
    public void Tap_NullPermanent_ThrowsException()
    {
        // Act & Assert
        new Action(() => AdditionalCost.Tap(null!))
            .Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void CanPay_TapCost_UntappedPermanent_ReturnsTrue()
    {
        // Arrange
        var player = new Player("Alice", 20);
        var permanent = new Creature("Grizzly Bears", "1G", 2, 2) { Controller = player };
        permanent.SetZone(ZoneType.Battlefield);
        // CR 302.6 — a creature's {T} cost is only payable once it sheds
        // summoning sickness; clear it so this test isolates the untapped /
        // tapped distinction.
        permanent.ClearSummoningSickness();
        var cost = AdditionalCost.Tap(permanent);

        // Act
        var result = cost.CanPay(player);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void CanPay_TapCost_TappedPermanent_ReturnsFalse()
    {
        // Arrange
        var player = new Player("Alice", 20);
        var permanent = new Creature("Grizzly Bears", "1G", 2, 2) { Controller = player };
        permanent.SetZone(ZoneType.Battlefield);
        permanent.Tap();
        var cost = AdditionalCost.Tap(permanent);

        // Act
        var result = cost.CanPay(player);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void Pay_TapCost_TapsPermanent()
    {
        // Arrange
        var player = new Player("Alice", 20);
        var permanent = new Creature("Grizzly Bears", "1G", 2, 2) { Controller = player };
        permanent.SetZone(ZoneType.Battlefield);
        // CR 302.6 — clear summoning sickness so the {T} cost is payable;
        // this test asserts that paying it taps the permanent.
        permanent.ClearSummoningSickness();
        var cost = AdditionalCost.Tap(permanent);

        // Act
        cost.Pay(player);

        // Assert
        permanent.IsTapped.Should().BeTrue();
    }

    [Fact]
    public void PayLife_ValidAmount_CreatesLifeCost()
    {
        // Act
        var cost = AdditionalCost.PayLife(3);

        // Assert
        cost.CostType.Should().Be(AdditionalCostType.PayLife);
        cost.Description.Should().Contain("Pay");
        cost.Description.Should().Contain("life");
    }

    [Fact]
    public void PayLife_NegativeAmount_ThrowsException()
    {
        // Act & Assert
        new Action(() => AdditionalCost.PayLife(-1))
            .Should().Throw<ArgumentException>();
    }

    [Fact]
    public void CanPay_PayLifeCost_SufficientLife_ReturnsTrue()
    {
        // Arrange
        var player = new Player("Alice", 20);
        var cost = AdditionalCost.PayLife(5);

        // Act
        var result = cost.CanPay(player);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void CanPay_PayLifeCost_InsufficientLife_ReturnsFalse()
    {
        // Arrange
        var player = new Player("Alice", 3);
        var cost = AdditionalCost.PayLife(5);

        // Act
        var result = cost.CanPay(player);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void Pay_PayLifeCost_ReducesLifeTotal()
    {
        // Arrange
        var player = new Player("Alice", 20);
        var cost = AdditionalCost.PayLife(5);

        // Act
        cost.Pay(player);

        // Assert
        player.LifeTotal.Should().Be(15);
    }

    [Fact]
    public void Sacrifice_ValidPermanent_CreatesSacrificeCost()
    {
        // Arrange
        var permanent = new Creature("Grizzly Bears", "1G", 2, 2);

        // Act
        var cost = AdditionalCost.Sacrifice(permanent);

        // Assert
        cost.CostType.Should().Be(AdditionalCostType.Sacrifice);
        cost.Description.Should().Contain("Sacrifice");
    }

    // -----------------------------------------------------------------------
    // Bus-aware sac cost — pays down thread-bus-into-edict-sacrifice-closures.
    // Paying a "Sacrifice this permanent" activation cost with an event bus
    // publishes a PermanentSacrificedEvent crediting the cost-payer (the
    // permanent's controller, CR 701.16a) so aristocrat payoffs fire.
    // -----------------------------------------------------------------------

    [Fact]
    public void Pay_SacrificeCost_WithEventBus_PublishesPermanentSacrificedEvent()
    {
        // Arrange
        var player = new Player("Alice", 20);
        var permanent = new Creature("Grizzly Bears", "1G", 2, 2);
        permanent.SetOwner(player);
        permanent.SetController(player);
        player.Zones.Battlefield.AddCard(permanent);
        permanent.SetZone(ZoneType.Battlefield);

        var bus = new EventBus();
        var sacrificed = new List<PermanentSacrificedEvent>();
        bus.Subscribe<PermanentSacrificedEvent>(ev => sacrificed.Add(ev));

        var cost = AdditionalCost.Sacrifice(permanent, bus);

        // Act
        cost.Pay(player);

        // Assert — real sacrifice + a single event crediting the payer.
        permanent.Zone.Should().Be(ZoneType.Graveyard);
        sacrificed.Should().ContainSingle()
            .Which.Should().Match<PermanentSacrificedEvent>(ev =>
                ev.SacrificedCard == permanent
                && ev.SacrificingPlayer == player
                && !ev.WasToken);
    }

    // -----------------------------------------------------------------------
    // CENTRAL SEAM — pays down icost-pay-central-bus-seam. A bus-less
    // AdditionalCost.Sacrifice(...) still publishes a PermanentSacrificedEvent
    // when paid through the central cost-payment path (CostPayment.PayCosts
    // with a bus) because AdditionalCost is IBusAwareCost. This obsoletes the
    // per-factory "thread eventBus into the cost at construction"
    // (Festival-Crasher) pattern for the broad class-(b) sac-cost tail
    // (Goblin Cratermaker, Cathar Commando, Mind Stone, …): the central Pay
    // drive site hands the live bus to any IBusAwareCost (CR 701.16a).
    // -----------------------------------------------------------------------

    [Fact]
    public void IBusAwareCost_BuslessSacrifice_PaidThroughCentralSeam_Publishes()
    {
        // Arrange — a sacrifice cost constructed WITHOUT a bus (the way the
        // class-(b) tail factories build it once the seam lands).
        var player = new Player("Alice", 20);
        var permanent = new Creature("Grizzly Bears", "1G", 2, 2);
        permanent.SetOwner(player);
        permanent.SetController(player);
        player.Zones.Battlefield.AddCard(permanent);
        permanent.SetZone(ZoneType.Battlefield);

        var bus = new EventBus();
        var sacrificed = new List<PermanentSacrificedEvent>();
        bus.Subscribe<PermanentSacrificedEvent>(ev => sacrificed.Add(ev));

        var cost = AdditionalCost.Sacrifice(permanent); // NO bus at construction

        // Act — pay through the central cost-payment seam WITH a bus.
        new CostPayment().PayCosts(
            player,
            new ICost[] { cost },
            Majik.Core.Mana.ManaSpendContext.None,
            bus);

        // Assert — the sacrifice happened AND the event fired off the central
        // seam, crediting the cost-payer (CR 701.16a).
        permanent.Zone.Should().Be(ZoneType.Graveyard);
        sacrificed.Should().ContainSingle()
            .Which.Should().Match<PermanentSacrificedEvent>(ev =>
                ev.SacrificedCard == permanent
                && ev.SacrificingPlayer == player
                && !ev.WasToken);
    }

    [Fact]
    public void IBusAwareCost_BuslessSacrifice_PaidThroughCentralSeam_NoBus_NoPublish()
    {
        // Arrange — bus-less cost, central seam called WITHOUT a bus: legacy
        // publish-nothing posture, but the sacrifice still resolves.
        var player = new Player("Alice", 20);
        var permanent = new Creature("Grizzly Bears", "1G", 2, 2);
        permanent.SetOwner(player);
        permanent.SetController(player);
        player.Zones.Battlefield.AddCard(permanent);
        permanent.SetZone(ZoneType.Battlefield);

        var cost = AdditionalCost.Sacrifice(permanent);

        // Act
        new CostPayment().PayCosts(player, new ICost[] { cost });

        // Assert
        permanent.Zone.Should().Be(ZoneType.Graveyard);
    }

    [Fact]
    public void IBusAwareCost_ConstructionBus_TakesPrecedence_OverSeamBus_NoDoublePublish()
    {
        // Arrange — a cost built WITH a construction bus, then ALSO paid via
        // the central seam with a DIFFERENT bus. The cost must publish exactly
        // once (no double-fire). The construction bus wins (back-compat: a
        // factory that explicitly threaded a bus keeps that exact behaviour).
        var player = new Player("Alice", 20);
        var permanent = new Creature("Grizzly Bears", "1G", 2, 2);
        permanent.SetOwner(player);
        permanent.SetController(player);
        player.Zones.Battlefield.AddCard(permanent);
        permanent.SetZone(ZoneType.Battlefield);

        var ctorBus = new EventBus();
        var ctorSeen = new List<PermanentSacrificedEvent>();
        ctorBus.Subscribe<PermanentSacrificedEvent>(ctorSeen.Add);

        var seamBus = new EventBus();
        var seamSeen = new List<PermanentSacrificedEvent>();
        seamBus.Subscribe<PermanentSacrificedEvent>(seamSeen.Add);

        var cost = AdditionalCost.Sacrifice(permanent, ctorBus);

        // Act
        new CostPayment().PayCosts(
            player, new ICost[] { cost }, Majik.Core.Mana.ManaSpendContext.None, seamBus);

        // Assert — exactly one publish, on the construction bus only.
        permanent.Zone.Should().Be(ZoneType.Graveyard);
        ctorSeen.Should().ContainSingle();
        seamSeen.Should().BeEmpty();
    }

    [Fact]
    public void Pay_SacrificeCost_NoEventBus_PublishesNothing_StillSacrifices()
    {
        // Arrange
        var player = new Player("Alice", 20);
        var permanent = new Creature("Grizzly Bears", "1G", 2, 2);
        permanent.SetOwner(player);
        permanent.SetController(player);
        player.Zones.Battlefield.AddCard(permanent);
        permanent.SetZone(ZoneType.Battlefield);

        var cost = AdditionalCost.Sacrifice(permanent);

        // Act — legacy posture: no bus, the card still hits the graveyard.
        cost.Pay(player);

        // Assert
        permanent.Zone.Should().Be(ZoneType.Graveyard);
    }

    // -----------------------------------------------------------------------
    // STAGE 1 (re-sourceable abilities) — RebindSource re-homes a
    // source-capturing cost ({T} / sacrifice) onto a new permanent so a
    // re-sourced activated ability pays its cost with the new source (CR 707.2).
    // -----------------------------------------------------------------------

    [Fact]
    public void RebindSource_TapCost_Matching_TapsNewPermanent_NotOld()
    {
        // Arrange — a {T} cost captured on permanent A.
        var player = new Player("Alice", 20);
        var a = new Creature("Grizzly Bears", "1G", 2, 2) { Controller = player };
        a.SetZone(ZoneType.Battlefield);
        a.ClearSummoningSickness();

        var b = new Creature("Llanowar Elves", "G", 1, 1) { Controller = player };
        b.SetZone(ZoneType.Battlefield);
        b.ClearSummoningSickness();

        var cost = AdditionalCost.Tap(a);

        // Act — rebind old=A to new=B, then pay.
        var rebound = cost.RebindSource(a, b);
        rebound.Pay(player);

        // Assert — B is tapped (the rebound source); A is untouched.
        rebound.Should().NotBeSameAs(cost);
        b.IsTapped.Should().BeTrue();
        a.IsTapped.Should().BeFalse();
        rebound.Description.Should().Contain("Llanowar Elves");
    }

    [Fact]
    public void RebindSource_SacrificeCost_Matching_SacrificesNewPermanent_NotOld()
    {
        // Arrange — a sacrifice cost captured on permanent A.
        var player = new Player("Alice", 20);

        var a = new Creature("Grizzly Bears", "1G", 2, 2);
        a.SetOwner(player);
        a.SetController(player);
        player.Zones.Battlefield.AddCard(a);
        a.SetZone(ZoneType.Battlefield);

        var b = new Creature("Llanowar Elves", "G", 1, 1);
        b.SetOwner(player);
        b.SetController(player);
        player.Zones.Battlefield.AddCard(b);
        b.SetZone(ZoneType.Battlefield);

        var cost = AdditionalCost.Sacrifice(a);

        // Act — rebind old=A to new=B, then pay.
        var rebound = cost.RebindSource(a, b);
        rebound.Pay(player);

        // Assert — B is sacrificed; A stays on the battlefield.
        b.Zone.Should().Be(ZoneType.Graveyard);
        a.Zone.Should().Be(ZoneType.Battlefield);
        rebound.Description.Should().Contain("Llanowar Elves");
    }

    [Fact]
    public void RebindSource_TapCost_NonMatching_ReturnsUnchanged()
    {
        // Arrange — a {T} cost on A, but rebind asks to swap a DIFFERENT old.
        var a = new Creature("Grizzly Bears", "1G", 2, 2);
        var unrelated = new Creature("Memnite", "0", 1, 1);
        var b = new Creature("Llanowar Elves", "G", 1, 1);
        var cost = AdditionalCost.Tap(a);

        // Act — oldSource is not the captured permanent.
        var result = cost.RebindSource(unrelated, b);

        // Assert — returned unchanged (same instance, still captures A).
        result.Should().BeSameAs(cost);
        result.Description.Should().Contain("Grizzly Bears");
    }

    [Fact]
    public void RebindSource_ManaCost_ReturnsUnchanged()
    {
        // Arrange — pay-life cost references no permanent.
        var cost = AdditionalCost.PayLife(2);
        var b = new Creature("Llanowar Elves", "G", 1, 1);

        // Act
        var result = cost.RebindSource(new object(), b);

        // Assert — non-source cost types pass through untouched.
        result.Should().BeSameAs(cost);
    }
}
