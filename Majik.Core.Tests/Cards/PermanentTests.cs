using FluentAssertions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Xunit;

namespace Majik.Core.Tests.Cards;

/// <summary>
/// Unit tests for Permanent entity.
/// Tests tapping, untapping, and summoning sickness.
/// </summary>
public class PermanentTests
{
    [Fact]
    public void Constructor_CreatesUntappedPermanent()
    {
        // Act
        var permanent = new Permanent("Forest", "", new[] { CardType.Land });

        // Assert
        permanent.IsTapped.Should().BeFalse();
        permanent.HasSummoningSickness.Should().BeTrue();
    }

    [Fact]
    public void Tap_UntappedPermanent_TapsPermanent()
    {
        // Arrange
        var permanent = new Permanent("Forest", "", new[] { CardType.Land });

        // Act
        permanent.Tap();

        // Assert
        permanent.IsTapped.Should().BeTrue();
    }

    [Fact]
    public void Tap_AlreadyTapped_ThrowsException()
    {
        // Arrange
        var permanent = new Permanent("Forest", "", new[] { CardType.Land });
        permanent.Tap();

        // Act & Assert
        permanent.Invoking(p => p.Tap())
            .Should().Throw<InvalidOperationException>()
            .WithMessage("*already tapped*");
    }

    [Fact]
    public void Untap_TappedPermanent_UntapsPermanent()
    {
        // Arrange
        var permanent = new Permanent("Forest", "", new[] { CardType.Land });
        permanent.Tap();

        // Act
        permanent.Untap();

        // Assert
        permanent.IsTapped.Should().BeFalse();
    }

    [Fact]
    public void Untap_NotTapped_ThrowsException()
    {
        // Arrange
        var permanent = new Permanent("Forest", "", new[] { CardType.Land });

        // Act & Assert
        permanent.Invoking(p => p.Untap())
            .Should().Throw<InvalidOperationException>()
            .WithMessage("*not tapped*");
    }

    [Fact]
    public void HasSummoningSickness_CanBeSet()
    {
        // Arrange
        var permanent = new Permanent("Forest", "", new[] { CardType.Land });

        // Act
        permanent.HasSummoningSickness = false;

        // Assert
        permanent.HasSummoningSickness.Should().BeFalse();
    }
}
