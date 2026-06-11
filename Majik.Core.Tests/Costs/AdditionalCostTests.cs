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
