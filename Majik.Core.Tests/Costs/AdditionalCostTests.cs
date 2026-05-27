using FluentAssertions;
using Majik.Core.Cards;
using Majik.Core.Costs;
using Majik.Core.Domain.Exceptions;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.Costs;

/// <summary>
/// Unit tests for AdditionalCost.
/// Tests tap, sacrifice, discard, and life costs.
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

    [Fact]
    public void Discard_ValidCard_CreatesDiscardCost()
    {
        // Arrange
        var card = new Instant("Lightning Bolt", "R");

        // Act
        var cost = AdditionalCost.Discard(card);

        // Assert
        cost.CostType.Should().Be(AdditionalCostType.Discard);
        cost.Description.Should().Contain("Discard");
    }
}
