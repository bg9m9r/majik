using FluentAssertions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.Cards;

/// <summary>
/// Unit tests for Card entity.
/// Tests card creation, types, and properties.
/// </summary>
public class CardTests
{
    [Fact]
    public void Constructor_ValidInput_CreatesCard()
    {
        // Act
        var card = new Card("Lightning Bolt", "R", new[] { CardType.Instant });

        // Assert
        card.Name.Should().Be("Lightning Bolt");
        card.ManaCost.Should().Be("R");
        card.ManaCostValue.Generic.Should().Be(0);
        card.ManaCostValue.Red.Should().Be(1);
        card.Zone.Should().Be(ZoneType.Library);
        card.Owner.Should().BeNull();
        card.Controller.Should().BeNull();
    }

    [Fact]
    public void Constructor_NullName_ThrowsException()
    {
        // Act & Assert
        new Action(() => new Card(null!, "R"))
            .Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Constructor_EmptyName_ThrowsException()
    {
        // Act & Assert
        new Action(() => new Card("", "R"))
            .Should().Throw<ArgumentException>();
    }

    [Fact]
    public void HasType_WithType_ReturnsTrue()
    {
        // Arrange
        var card = new Card("Lightning Bolt", "R", new[] { CardType.Instant });

        // Act & Assert
        card.HasType(CardType.Instant).Should().BeTrue();
        card.HasType(CardType.Sorcery).Should().BeFalse();
    }

    [Fact]
    public void HasType_MultipleTypes_ReturnsTrueForAll()
    {
        // Arrange
        var card = new Card("Test", "", new[] { CardType.Instant, CardType.Sorcery });

        // Act & Assert
        card.HasType(CardType.Instant).Should().BeTrue();
        card.HasType(CardType.Sorcery).Should().BeTrue();
    }

    [Fact]
    public void HasSupertype_WithSupertype_ReturnsTrue()
    {
        // Arrange
        var card = new Card("Forest", "", new[] { CardType.Land }, new[] { CardSupertype.Basic });

        // Act & Assert
        card.HasSupertype(CardSupertype.Basic).Should().BeTrue();
    }

    [Fact]
    public void HasSubtype_WithSubtype_ReturnsTrue()
    {
        // Arrange
        var card = new Card("Forest", "", new[] { CardType.Land }, null, new[] { CardSubtype.Forest });

        // Act & Assert
        card.HasSubtype(CardSubtype.Forest).Should().BeTrue();
    }

    [Fact]
    public void Controller_CanBeSet()
    {
        // Arrange
        var card = new Card("Lightning Bolt", "R", new[] { CardType.Instant });
        var player = new Player("Alice", 20);

        // Act
        card.SetController(player);

        // Assert
        card.Controller.Should().Be(player);
    }

    [Fact]
    public void Zone_CanBeSet()
    {
        // Arrange
        var card = new Card("Lightning Bolt", "R", new[] { CardType.Instant });

        // Act
        card.SetZone(ZoneType.Hand);

        // Assert
        card.Zone.Should().Be(ZoneType.Hand);
    }

    [Fact]
    public void ToString_ReturnsCardName()
    {
        // Arrange
        var card = new Card("Lightning Bolt", "R", new[] { CardType.Instant });

        // Act
        var result = card.ToString();

        // Assert
        result.Should().Be("Lightning Bolt");
    }
}
