using FluentAssertions;
using Majik.Core.ValueObjects;
using Xunit;

namespace Majik.Core.Tests.ValueObjects;

/// <summary>
/// Unit tests for CardIdentity value object.
/// Tests creation, equality, and string representation.
/// </summary>
public class CardIdentityTests
{
    [Fact]
    public void FromName_ValidName_CreatesIdentity()
    {
        // Act
        var identity = CardIdentity.FromName("Lightning Bolt");

        // Assert
        identity.Name.Should().Be("Lightning Bolt");
        identity.SetCode.Should().BeNull();
        identity.CollectorNumber.Should().BeNull();
    }

    [Fact]
    public void FromName_NullName_ThrowsException()
    {
        // Act & Assert
        new Action(() => CardIdentity.FromName(null!))
            .Should().Throw<ArgumentException>();
    }

    [Fact]
    public void FromName_EmptyName_ThrowsException()
    {
        // Act & Assert
        new Action(() => CardIdentity.FromName(""))
            .Should().Throw<ArgumentException>();
    }

    [Fact]
    public void FromName_WhitespaceName_ThrowsException()
    {
        // Act & Assert
        new Action(() => CardIdentity.FromName("   "))
            .Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Create_WithAllFields_CreatesIdentity()
    {
        // Act
        var identity = CardIdentity.Create("Lightning Bolt", "M21", "150");

        // Assert
        identity.Name.Should().Be("Lightning Bolt");
        identity.SetCode.Should().Be("M21");
        identity.CollectorNumber.Should().Be("150");
    }

    [Fact]
    public void Create_WithSetCodeOnly_CreatesIdentity()
    {
        // Act
        var identity = CardIdentity.Create("Lightning Bolt", "M21");

        // Assert
        identity.Name.Should().Be("Lightning Bolt");
        identity.SetCode.Should().Be("M21");
        identity.CollectorNumber.Should().BeNull();
    }

    [Fact]
    public void Equals_SameValues_ReturnsTrue()
    {
        // Arrange
        var identity1 = CardIdentity.Create("Lightning Bolt", "M21", "150");
        var identity2 = CardIdentity.Create("Lightning Bolt", "M21", "150");

        // Act & Assert
        identity1.Equals(identity2).Should().BeTrue();
        (identity1 == identity2).Should().BeTrue();
    }

    [Fact]
    public void Equals_DifferentNames_ReturnsFalse()
    {
        // Arrange
        var identity1 = CardIdentity.Create("Lightning Bolt", "M21", "150");
        var identity2 = CardIdentity.Create("Fireball", "M21", "150");

        // Act & Assert
        identity1.Equals(identity2).Should().BeFalse();
        (identity1 != identity2).Should().BeTrue();
    }

    [Fact]
    public void Equals_DifferentSetCodes_ReturnsFalse()
    {
        // Arrange
        var identity1 = CardIdentity.Create("Lightning Bolt", "M21", "150");
        var identity2 = CardIdentity.Create("Lightning Bolt", "ZNR", "150");

        // Act & Assert
        identity1.Equals(identity2).Should().BeFalse();
    }

    [Fact]
    public void Equals_DifferentCollectorNumbers_ReturnsFalse()
    {
        // Arrange
        var identity1 = CardIdentity.Create("Lightning Bolt", "M21", "150");
        var identity2 = CardIdentity.Create("Lightning Bolt", "M21", "151");

        // Act & Assert
        identity1.Equals(identity2).Should().BeFalse();
    }

    [Fact]
    public void GetHashCode_SameValues_ReturnsSameHashCode()
    {
        // Arrange
        var identity1 = CardIdentity.Create("Lightning Bolt", "M21", "150");
        var identity2 = CardIdentity.Create("Lightning Bolt", "M21", "150");

        // Act & Assert
        identity1.GetHashCode().Should().Be(identity2.GetHashCode());
    }

    [Fact]
    public void ToString_NameOnly_ReturnsName()
    {
        // Arrange
        var identity = CardIdentity.FromName("Lightning Bolt");

        // Act
        var result = identity.ToString();

        // Assert
        result.Should().Be("Lightning Bolt");
    }

    [Fact]
    public void ToString_WithSetCode_ReturnsNameAndSetCode()
    {
        // Arrange
        var identity = CardIdentity.Create("Lightning Bolt", "M21");

        // Act
        var result = identity.ToString();

        // Assert
        result.Should().Be("Lightning Bolt (M21)");
    }

    [Fact]
    public void ToString_WithAllFields_ReturnsFullFormat()
    {
        // Arrange
        var identity = CardIdentity.Create("Lightning Bolt", "M21", "150");

        // Act
        var result = identity.ToString();

        // Assert
        result.Should().Be("Lightning Bolt (M21 150)");
    }
}
