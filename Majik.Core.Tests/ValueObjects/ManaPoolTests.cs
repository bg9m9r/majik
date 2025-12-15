using FluentAssertions;
using Majik.Core.ValueObjects;
using Xunit;

namespace Majik.Core.Tests.ValueObjects;

/// <summary>
/// Unit tests for ManaPool value object.
/// Tests pool management, mana addition, payment, and validation.
/// </summary>
public class ManaPoolTests
{
    [Fact]
    public void Empty_ReturnsEmptyPool()
    {
        // Act
        var pool = ManaPool.Empty;

        // Assert
        pool.Generic.Should().Be(0);
        pool.White.Should().Be(0);
        pool.Blue.Should().Be(0);
        pool.Black.Should().Be(0);
        pool.Red.Should().Be(0);
        pool.Green.Should().Be(0);
        pool.Total.Should().Be(0);
        pool.IsEmpty.Should().BeTrue();
    }

    [Fact]
    public void Add_ValidManaCost_AddsToPool()
    {
        // Arrange
        var pool = ManaPool.Empty;
        var manaCost = ManaCost.Parse("3RR");

        // Act
        var result = pool.Add(manaCost);

        // Assert
        result.Generic.Should().Be(3);
        result.Red.Should().Be(2);
        result.Total.Should().Be(5);
        result.IsEmpty.Should().BeFalse();
    }

    [Fact]
    public void Add_NullManaCost_ThrowsException()
    {
        // Arrange
        var pool = ManaPool.Empty;

        // Act & Assert
        pool.Invoking(p => p.Add(null!))
            .Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void AddGeneric_ValidAmount_AddsGenericMana()
    {
        // Arrange
        var pool = ManaPool.Empty;

        // Act
        var result = pool.AddGeneric(5);

        // Assert
        result.Generic.Should().Be(5);
        result.Total.Should().Be(5);
    }

    [Fact]
    public void AddGeneric_NegativeAmount_ThrowsException()
    {
        // Arrange
        var pool = ManaPool.Empty;

        // Act & Assert
        pool.Invoking(p => p.AddGeneric(-1))
            .Should().Throw<ArgumentException>();
    }

    [Fact]
    public void AddColored_ValidAmounts_AddsColoredMana()
    {
        // Arrange
        var pool = ManaPool.Empty;

        // Act
        var result = pool.AddColored(white: 1, blue: 2, red: 3);

        // Assert
        result.White.Should().Be(1);
        result.Blue.Should().Be(2);
        result.Red.Should().Be(3);
        result.Total.Should().Be(6);
    }

    [Fact]
    public void AddColored_NegativeAmount_ThrowsException()
    {
        // Arrange
        var pool = ManaPool.Empty;

        // Act & Assert
        pool.Invoking(p => p.AddColored(white: -1))
            .Should().Throw<ArgumentException>();
    }

    [Fact]
    public void CanPay_SufficientMana_ReturnsTrue()
    {
        // Arrange
        var pool = ManaPool.Empty.Add(ManaCost.Parse("3RR"));
        var cost = ManaCost.Parse("1R");

        // Act
        var result = pool.CanPay(cost);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void CanPay_InsufficientColoredMana_ReturnsFalse()
    {
        // Arrange
        var pool = ManaPool.Empty.Add(ManaCost.Parse("3"));
        var cost = ManaCost.Parse("R");

        // Act
        var result = pool.CanPay(cost);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void CanPay_InsufficientTotalMana_ReturnsFalse()
    {
        // Arrange
        var pool = ManaPool.Empty.Add(ManaCost.Parse("R"));
        var cost = ManaCost.Parse("3");

        // Act
        var result = pool.CanPay(cost);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void CanPay_NullCost_ReturnsFalse()
    {
        // Arrange
        var pool = ManaPool.Empty.Add(ManaCost.Parse("R"));

        // Act
        var result = pool.CanPay(null!);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void Pay_SufficientMana_ReturnsSuccessAndNewPool()
    {
        // Arrange
        var pool = ManaPool.Empty.Add(ManaCost.Parse("3RR"));
        var cost = ManaCost.Parse("1R");

        // Act
        var (newPool, success) = pool.Pay(cost);

        // Assert
        success.Should().BeTrue();
        newPool.Red.Should().Be(1); // 2 - 1 = 1
        // Generic mana is used for generic cost, so remaining is 2 (3 - 1 = 2)
        newPool.Generic.Should().Be(2);
        newPool.Total.Should().Be(3);
    }

    [Fact]
    public void Pay_InsufficientMana_ReturnsFailureAndOriginalPool()
    {
        // Arrange
        var pool = ManaPool.Empty.Add(ManaCost.Parse("R"));
        var cost = ManaCost.Parse("3");

        // Act
        var (newPool, success) = pool.Pay(cost);

        // Assert
        success.Should().BeFalse();
        newPool.Should().Be(pool);
    }

    [Fact]
    public void Pay_NullCost_ThrowsException()
    {
        // Arrange
        var pool = ManaPool.Empty.Add(ManaCost.Parse("R"));

        // Act & Assert
        pool.Invoking(p => p.Pay(null!))
            .Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Pay_GenericCost_UsesColoredManaForGeneric()
    {
        // Arrange
        var pool = ManaPool.Empty.Add(ManaCost.Parse("RR"));
        var cost = ManaCost.Parse("2"); // Only 2 generic needed, we have 2 red

        // Act
        var (newPool, success) = pool.Pay(cost);

        // Assert
        success.Should().BeTrue();
        newPool.Total.Should().Be(0); // All mana used
    }

    [Fact]
    public void EmptyPool_ReturnsEmptyPool()
    {
        // Arrange
        var pool = ManaPool.Empty.Add(ManaCost.Parse("3RR"));

        // Act
        var result = pool.EmptyPool();

        // Assert
        result.Should().Be(ManaPool.Empty);
        result.IsEmpty.Should().BeTrue();
    }

    [Fact]
    public void Equals_SameValues_ReturnsTrue()
    {
        // Arrange
        var pool1 = ManaPool.Empty.Add(ManaCost.Parse("3RR"));
        var pool2 = ManaPool.Empty.Add(ManaCost.Parse("3RR"));

        // Act & Assert
        pool1.Equals(pool2).Should().BeTrue();
    }

    [Fact]
    public void Equals_DifferentValues_ReturnsFalse()
    {
        // Arrange
        var pool1 = ManaPool.Empty.Add(ManaCost.Parse("3RR"));
        var pool2 = ManaPool.Empty.Add(ManaCost.Parse("2RR"));

        // Act & Assert
        pool1.Equals(pool2).Should().BeFalse();
    }

    [Fact]
    public void GetHashCode_SameValues_ReturnsSameHashCode()
    {
        // Arrange
        var pool1 = ManaPool.Empty.Add(ManaCost.Parse("3RR"));
        var pool2 = ManaPool.Empty.Add(ManaCost.Parse("3RR"));

        // Act & Assert
        pool1.GetHashCode().Should().Be(pool2.GetHashCode());
    }

    [Fact]
    public void ToString_WithMana_ReturnsReadableFormat()
    {
        // Arrange
        var pool = ManaPool.Empty.Add(ManaCost.Parse("3RR"));

        // Act
        var result = pool.ToString();

        // Assert
        result.Should().Contain("3");
        result.Should().Contain("R");
    }

    [Fact]
    public void ToString_EmptyPool_ReturnsEmpty()
    {
        // Arrange
        var pool = ManaPool.Empty;

        // Act
        var result = pool.ToString();

        // Assert
        result.Should().Be("Empty");
    }
}
