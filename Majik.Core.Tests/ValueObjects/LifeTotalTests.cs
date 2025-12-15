using FluentAssertions;
using Majik.Core.ValueObjects;
using Xunit;

namespace Majik.Core.Tests.ValueObjects;

/// <summary>
/// Unit tests for LifeTotal value object.
/// Tests creation, operations, and loss detection.
/// </summary>
public class LifeTotalTests
{
    [Fact]
    public void Create_ValidValue_CreatesLifeTotal()
    {
        // Act
        var lifeTotal = LifeTotal.Create(20);

        // Assert
        lifeTotal.Value.Should().Be(20);
        lifeTotal.HasLost.Should().BeFalse();
    }

    [Fact]
    public void Create_NegativeValue_CreatesLifeTotal()
    {
        // Act
        var lifeTotal = LifeTotal.Create(-5);

        // Assert
        lifeTotal.Value.Should().Be(-5);
        lifeTotal.HasLost.Should().BeTrue();
    }

    [Fact]
    public void Create_ZeroValue_HasLostIsTrue()
    {
        // Act
        var lifeTotal = LifeTotal.Create(0);

        // Assert
        lifeTotal.Value.Should().Be(0);
        lifeTotal.HasLost.Should().BeTrue();
    }

    [Fact]
    public void Default_ReturnsTwenty()
    {
        // Act
        var lifeTotal = LifeTotal.Default;

        // Assert
        lifeTotal.Value.Should().Be(20);
        lifeTotal.HasLost.Should().BeFalse();
    }

    [Fact]
    public void Add_ValidAmount_IncreasesValue()
    {
        // Arrange
        var lifeTotal = LifeTotal.Create(20);

        // Act
        var result = lifeTotal.Add(5);

        // Assert
        result.Value.Should().Be(25);
        result.HasLost.Should().BeFalse();
    }

    [Fact]
    public void Add_NegativeAmount_ThrowsException()
    {
        // Arrange
        var lifeTotal = LifeTotal.Create(20);

        // Act & Assert
        lifeTotal.Invoking(lt => lt.Add(-1))
            .Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Subtract_ValidAmount_DecreasesValue()
    {
        // Arrange
        var lifeTotal = LifeTotal.Create(20);

        // Act
        var result = lifeTotal.Subtract(5);

        // Assert
        result.Value.Should().Be(15);
        result.HasLost.Should().BeFalse();
    }

    [Fact]
    public void Subtract_ReducesToZero_SetsHasLost()
    {
        // Arrange
        var lifeTotal = LifeTotal.Create(20);

        // Act
        var result = lifeTotal.Subtract(20);

        // Assert
        result.Value.Should().Be(0);
        result.HasLost.Should().BeTrue();
    }

    [Fact]
    public void Subtract_ReducesBelowZero_SetsHasLost()
    {
        // Arrange
        var lifeTotal = LifeTotal.Create(20);

        // Act
        var result = lifeTotal.Subtract(25);

        // Assert
        result.Value.Should().Be(-5);
        result.HasLost.Should().BeTrue();
    }

    [Fact]
    public void Subtract_NegativeAmount_ThrowsException()
    {
        // Arrange
        var lifeTotal = LifeTotal.Create(20);

        // Act & Assert
        lifeTotal.Invoking(lt => lt.Subtract(-1))
            .Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Equals_SameValues_ReturnsTrue()
    {
        // Arrange
        var life1 = LifeTotal.Create(20);
        var life2 = LifeTotal.Create(20);

        // Act & Assert
        life1.Equals(life2).Should().BeTrue();
        (life1 == life2).Should().BeTrue();
    }

    [Fact]
    public void Equals_DifferentValues_ReturnsFalse()
    {
        // Arrange
        var life1 = LifeTotal.Create(20);
        var life2 = LifeTotal.Create(15);

        // Act & Assert
        life1.Equals(life2).Should().BeFalse();
        (life1 != life2).Should().BeTrue();
    }

    [Fact]
    public void GetHashCode_SameValues_ReturnsSameHashCode()
    {
        // Arrange
        var life1 = LifeTotal.Create(20);
        var life2 = LifeTotal.Create(20);

        // Act & Assert
        life1.GetHashCode().Should().Be(life2.GetHashCode());
    }

    [Fact]
    public void ImplicitConversion_ToInt_ReturnsValue()
    {
        // Arrange
        var lifeTotal = LifeTotal.Create(20);

        // Act
        int value = lifeTotal;

        // Assert
        value.Should().Be(20);
    }

    [Fact]
    public void ToString_ReturnsValueAsString()
    {
        // Arrange
        var lifeTotal = LifeTotal.Create(20);

        // Act
        var result = lifeTotal.ToString();

        // Assert
        result.Should().Be("20");
    }
}
