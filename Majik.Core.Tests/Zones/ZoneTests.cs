using FluentAssertions;
using Majik.Core.Cards;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.Zones;

/// <summary>
/// Unit tests for Zone class.
/// Tests card addition, removal, and zone property updates.
/// </summary>
public class ZoneTests
{
    [Fact]
    public void Constructor_ValidInput_CreatesZone()
    {
        // Act
        var zone = new Zone(ZoneType.Hand, "Hand");

        // Assert
        zone.Type.Should().Be(ZoneType.Hand);
        zone.Name.Should().Be("Hand");
        zone.Count.Should().Be(0);
    }

    [Fact]
    public void AddCard_ValidCard_AddsToZone()
    {
        // Arrange
        var zone = new Zone(ZoneType.Hand, "Hand");
        var card = new Instant("Lightning Bolt", "R");

        // Act
        zone.AddCard(card);

        // Assert
        zone.Count.Should().Be(1);
        zone.ContainsCard(card).Should().BeTrue();
        card.Zone.Should().Be(ZoneType.Hand);
    }

    [Fact]
    public void AddCard_NullCard_ThrowsException()
    {
        // Arrange
        var zone = new Zone(ZoneType.Hand, "Hand");

        // Act & Assert
        zone.Invoking(z => z.AddCard(null!))
            .Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void AddCard_DuplicateCard_DoesNotAddTwice()
    {
        // Arrange
        var zone = new Zone(ZoneType.Hand, "Hand");
        var card = new Instant("Lightning Bolt", "R");

        // Act
        zone.AddCard(card);
        zone.AddCard(card);

        // Assert
        zone.Count.Should().Be(1);
    }

    [Fact]
    public void RemoveCard_ExistingCard_RemovesFromZone()
    {
        // Arrange
        var zone = new Zone(ZoneType.Hand, "Hand");
        var card = new Instant("Lightning Bolt", "R");
        zone.AddCard(card);

        // Act
        var result = zone.RemoveCard(card);

        // Assert
        result.Should().BeTrue();
        zone.Count.Should().Be(0);
        zone.ContainsCard(card).Should().BeFalse();
    }

    [Fact]
    public void RemoveCard_NonExistentCard_ReturnsFalse()
    {
        // Arrange
        var zone = new Zone(ZoneType.Hand, "Hand");
        var card = new Instant("Lightning Bolt", "R");

        // Act
        var result = zone.RemoveCard(card);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void ContainsCard_ExistingCard_ReturnsTrue()
    {
        // Arrange
        var zone = new Zone(ZoneType.Hand, "Hand");
        var card = new Instant("Lightning Bolt", "R");
        zone.AddCard(card);

        // Act
        var result = zone.ContainsCard(card);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void ContainsCard_NonExistentCard_ReturnsFalse()
    {
        // Arrange
        var zone = new Zone(ZoneType.Hand, "Hand");
        var card = new Instant("Lightning Bolt", "R");

        // Act
        var result = zone.ContainsCard(card);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void GetCards_ReturnsAllCards()
    {
        // Arrange
        var zone = new Zone(ZoneType.Hand, "Hand");
        var card1 = new Instant("Lightning Bolt", "R");
        var card2 = new Instant("Fireball", "2RR");
        zone.AddCard(card1);
        zone.AddCard(card2);

        // Act
        var cards = zone.GetCards();

        // Assert
        cards.Should().HaveCount(2);
        cards.Should().Contain(card1);
        cards.Should().Contain(card2);
    }

    [Fact]
    public void Clear_RemovesAllCards()
    {
        // Arrange
        var zone = new Zone(ZoneType.Hand, "Hand");
        var card1 = new Instant("Lightning Bolt", "R");
        var card2 = new Instant("Fireball", "2RR");
        zone.AddCard(card1);
        zone.AddCard(card2);

        // Act
        zone.Clear();

        // Assert
        zone.Count.Should().Be(0);
    }
}
