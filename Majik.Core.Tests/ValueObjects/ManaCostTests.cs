using FluentAssertions;
using Majik.Core.ValueObjects;
using Xunit;

namespace Majik.Core.Tests.ValueObjects;

/// <summary>
/// Unit tests for ManaCost value object.
/// Tests parsing, equality, validation, and string conversion.
/// </summary>
public class ManaCostTests
{
    [Fact]
    public void Parse_ValidString_ReturnsCorrectManaCost()
    {
        // Arrange
        var input = "3RR";

        // Act
        var result = ManaCost.Parse(input);

        // Assert
        result.Generic.Should().Be(3);
        result.Red.Should().Be(2);
        result.White.Should().Be(0);
        result.Blue.Should().Be(0);
        result.Black.Should().Be(0);
        result.Green.Should().Be(0);
    }

    [Theory]
    [InlineData("3RR", 3, 0, 0, 0, 2, 0)]
    [InlineData("1WU", 1, 1, 1, 0, 0, 0)]
    [InlineData("2BB", 2, 0, 0, 2, 0, 0)]
    [InlineData("G", 0, 0, 0, 0, 0, 1)]
    [InlineData("", 0, 0, 0, 0, 0, 0)]
    [InlineData("5", 5, 0, 0, 0, 0, 0)]
    public void Parse_VariousInputs_ReturnsExpectedValues(
        string input, 
        int expectedGeneric, 
        int expectedWhite, 
        int expectedBlue, 
        int expectedBlack, 
        int expectedRed, 
        int expectedGreen)
    {
        // Act
        var result = ManaCost.Parse(input);

        // Assert
        result.Generic.Should().Be(expectedGeneric);
        result.White.Should().Be(expectedWhite);
        result.Blue.Should().Be(expectedBlue);
        result.Black.Should().Be(expectedBlack);
        result.Red.Should().Be(expectedRed);
        result.Green.Should().Be(expectedGreen);
    }

    [Fact]
    public void Parse_StringWithX_SetsHasX()
    {
        // Arrange
        var input = "XRR";

        // Act
        var result = ManaCost.Parse(input);

        // Assert
        result.HasX.Should().BeTrue();
        result.Red.Should().Be(2);
    }

    [Fact]
    public void Zero_ReturnsZeroManaCost()
    {
        // Act
        var result = ManaCost.Zero;

        // Assert
        result.Generic.Should().Be(0);
        result.White.Should().Be(0);
        result.Blue.Should().Be(0);
        result.Black.Should().Be(0);
        result.Red.Should().Be(0);
        result.Green.Should().Be(0);
        result.HasX.Should().BeFalse();
        result.IsZero.Should().BeTrue();
    }

    [Fact]
    public void Equals_SameValues_ReturnsTrue()
    {
        // Arrange
        var cost1 = ManaCost.Parse("3RR");
        var cost2 = ManaCost.Parse("3RR");

        // Act & Assert
        cost1.Equals(cost2).Should().BeTrue();
        (cost1 == cost2).Should().BeTrue();
    }

    [Fact]
    public void Equals_DifferentValues_ReturnsFalse()
    {
        // Arrange
        var cost1 = ManaCost.Parse("3RR");
        var cost2 = ManaCost.Parse("2RR");

        // Act & Assert
        cost1.Equals(cost2).Should().BeFalse();
        (cost1 != cost2).Should().BeTrue();
    }

    [Fact]
    public void GetHashCode_SameValues_ReturnsSameHashCode()
    {
        // Arrange
        var cost1 = ManaCost.Parse("3RR");
        var cost2 = ManaCost.Parse("3RR");

        // Act & Assert
        cost1.GetHashCode().Should().Be(cost2.GetHashCode());
    }

    [Fact]
    public void TotalValue_CalculatesCorrectly()
    {
        // Arrange
        var cost = ManaCost.Parse("3RR");

        // Act
        var total = cost.TotalValue;

        // Assert
        total.Should().Be(5); // 3 generic + 2 red
    }

    [Fact]
    public void ToString_ReturnsReadableFormat()
    {
        // Arrange
        var cost = ManaCost.Parse("3RR");

        // Act
        var result = cost.ToString();

        // Assert
        result.Should().Contain("3");
        result.Should().Contain("R");
    }
}
