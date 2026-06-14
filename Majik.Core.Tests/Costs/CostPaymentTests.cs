using FluentAssertions;
using Majik.Core.Cards;
using Majik.Core.Costs;
using Majik.Core.Domain.Exceptions;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.Costs;

/// <summary>
/// Unit tests for CostPayment service.
/// Tests cost payment validation and execution.
/// </summary>
public class CostPaymentTests
{
    [Fact]
    public void PayCosts_ValidCosts_PaysAllCosts()
    {
        // Arrange
        var payment = new CostPayment();
        var player = new Player("Alice", 20);
        player.AddManaToPool(ManaCost.Parse("RR"));
        var costs = new List<ICost> { new ManaCostCost("RR") };

        // Act
        payment.PayCosts(player, costs);

        // Assert
        player.ManaPool.IsEmpty.Should().BeTrue();
    }

    [Fact]
    public void PayCosts_MultipleCosts_PaysAllInOrder()
    {
        // Arrange
        var payment = new CostPayment();
        var player = new Player("Alice", 20);
        player.AddManaToPool(ManaCost.Parse("R"));
        var permanent = new Creature("Grizzly Bears", "1G", 2, 2) { Owner = player, Controller = player };
        permanent.SetZone(ZoneType.Battlefield);
        // CR 302.6 — a creature must shed summoning sickness before its {T}
        // cost can be paid; clear it so this test exercises multi-cost
        // ordering rather than the sickness gate.
        permanent.ClearSummoningSickness();
        var costs = new List<ICost>
        {
            new ManaCostCost("R"),
            AdditionalCost.Tap(permanent)
        };

        // Act
        payment.PayCosts(player, costs);

        // Assert
        player.ManaPool.IsEmpty.Should().BeTrue();
        permanent.IsTapped.Should().BeTrue();
    }

    [Fact]
    public void PayCosts_InsufficientMana_ThrowsException()
    {
        // Arrange
        var payment = new CostPayment();
        var player = new Player("Alice", 20);
        // No mana added
        var costs = new List<ICost> { new ManaCostCost("R") };

        // Act & Assert
        payment.Invoking(p => p.PayCosts(player, costs))
            .Should().Throw<InvalidPlayerActionException>();
    }

    [Fact]
    public void PayCosts_NullPlayer_ThrowsException()
    {
        // Arrange
        var payment = new CostPayment();
        var costs = new List<ICost> { new ManaCostCost("R") };

        // Act & Assert
        payment.Invoking(p => p.PayCosts(null!, costs))
            .Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void PayCosts_NullCosts_ThrowsException()
    {
        // Arrange
        var payment = new CostPayment();
        var player = new Player("Alice", 20);

        // Act & Assert
        payment.Invoking(p => p.PayCosts(player, null!))
            .Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void CanPayCosts_AllCostsCanBePaid_ReturnsTrue()
    {
        // Arrange
        var payment = new CostPayment();
        var player = new Player("Alice", 20);
        player.AddManaToPool(ManaCost.Parse("R"));
        var costs = new List<ICost> { new ManaCostCost("R") };

        // Act
        var result = payment.CanPayCosts(player, costs);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void CanPayCosts_SomeCostsCannotBePaid_ReturnsFalse()
    {
        // Arrange
        var payment = new CostPayment();
        var player = new Player("Alice", 20);
        // No mana added
        var costs = new List<ICost> { new ManaCostCost("R") };

        // Act
        var result = payment.CanPayCosts(player, costs);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void CanPayCosts_NullPlayer_ReturnsFalse()
    {
        // Arrange
        var payment = new CostPayment();
        var costs = new List<ICost> { new ManaCostCost("R") };

        // Act
        var result = payment.CanPayCosts(null!, costs);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void CanPayCosts_NullCosts_ReturnsFalse()
    {
        // Arrange
        var payment = new CostPayment();
        var player = new Player("Alice", 20);

        // Act
        var result = payment.CanPayCosts(player, null!);

        // Assert
        result.Should().BeFalse();
    }

    // ------------------------------------------------------------------
    // icost-pay-central-bus-seam — CR 701.16. A bus-aware sacrifice cost
    // (SacrificeSelfCost implementing IBusAwareCost) must publish a
    // PermanentSacrificedEvent through the central cost-payment path when a
    // bus is supplied, so "whenever a/an [player] sacrifices …" aristocrat
    // triggers fire on a "Sacrifice CARDNAME:" activated-ability cost — not
    // only on the Fx.Sacrifice(perm, player, bus) effect overload.
    // ------------------------------------------------------------------
    [Fact]
    public void PayCosts_SacrificeSelfCost_WithBus_PublishesPermanentSacrificedEvent()
    {
        // Arrange
        var bus = new EventBus();
        var seen = new List<PermanentSacrificedEvent>();
        bus.Subscribe<PermanentSacrificedEvent>(seen.Add);
        var payment = new CostPayment();
        var player = new Player("Alice", 20);
        var permanent = new Creature("Spore Frog", "G", 1, 1) { Owner = player, Controller = player };
        permanent.SetZone(ZoneType.Battlefield);
        player.Zones.Battlefield.AddCard(permanent);
        var costs = new List<ICost> { new SacrificeSelfCost(permanent) };

        // Act — pay through the bus-aware central seam.
        payment.PayCosts(player, costs, Majik.Core.Mana.ManaSpendContext.None, bus);

        // Assert — the permanent left for its owner's graveyard …
        permanent.Zone.Should().Be(ZoneType.Graveyard);
        player.Zones.Graveyard.ContainsCard(permanent).Should().BeTrue();

        // … and a PermanentSacrificedEvent fired with the sacrificing player.
        seen.Should().ContainSingle().Which.Should().Match<PermanentSacrificedEvent>(
            ev => ev.SacrificedCard == permanent
                  && ev.SacrificingPlayer == player
                  && !ev.WasToken);
    }

    [Fact]
    public void PayCosts_SacrificeSelfCost_WithoutBus_StillSacrifices_NoEvent()
    {
        // Arrange — legacy bus-less path still works (back-compat).
        var payment = new CostPayment();
        var player = new Player("Alice", 20);
        var permanent = new Creature("Spore Frog", "G", 1, 1) { Owner = player, Controller = player };
        permanent.SetZone(ZoneType.Battlefield);
        player.Zones.Battlefield.AddCard(permanent);
        var costs = new List<ICost> { new SacrificeSelfCost(permanent) };

        // Act
        payment.PayCosts(player, costs);

        // Assert — sacrifice still happens; no bus, so nothing to publish.
        permanent.Zone.Should().Be(ZoneType.Graveyard);
        player.Zones.Graveyard.ContainsCard(permanent).Should().BeTrue();
    }
}
