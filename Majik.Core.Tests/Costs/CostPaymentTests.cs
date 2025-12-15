using FluentAssertions;
using Majik.Core.Cards;
using Majik.Core.Costs;
using Majik.Core.Domain.Exceptions;
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
        permanent.Zone = ZoneType.Battlefield;
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
}
