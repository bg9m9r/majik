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

    // -----------------------------------------------------------------------
    // CR 702.49 — Imprint storage
    // -----------------------------------------------------------------------

    [Fact]
    public void Permanent_ImprintedCards_StartsEmpty()
    {
        var artifact = new Permanent("Test Artifact", "{2}", new[] { CardType.Artifact });

        artifact.ImprintedCards.Should().BeEmpty();
    }

    [Fact]
    public void Permanent_AddImprinted_StoresCard()
    {
        var artifact = new Permanent("Test Artifact", "{2}", new[] { CardType.Artifact });
        var bear = new Creature("Bear", "1G", 2, 2);

        artifact.AddImprinted(bear);

        artifact.ImprintedCards.Should().ContainSingle()
            .Which.Should().BeSameAs(bear);
    }

    [Fact]
    public void Permanent_AddImprinted_NullCard_Throws()
    {
        var artifact = new Permanent("Test Artifact", "{2}", new[] { CardType.Artifact });

        artifact.Invoking(p => p.AddImprinted(null!))
            .Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Permanent_AddImprinted_MultipleCards_PreservesOrder()
    {
        var artifact = new Permanent("Test Artifact", "{2}", new[] { CardType.Artifact });
        var cardX = new Card("X", "");
        var cardY = new Card("Y", "");

        artifact.AddImprinted(cardX);
        artifact.AddImprinted(cardY);

        artifact.ImprintedCards.Should().HaveCount(2);
        artifact.ImprintedCards[0].Should().BeSameAs(cardX);
        artifact.ImprintedCards[1].Should().BeSameAs(cardY);
    }

    [Fact]
    public void Permanent_ClearImprinted_RemovesAll()
    {
        var artifact = new Permanent("Test Artifact", "{2}", new[] { CardType.Artifact });
        artifact.AddImprinted(new Card("X", ""));
        artifact.AddImprinted(new Card("Y", ""));

        artifact.ClearImprinted();

        artifact.ImprintedCards.Should().BeEmpty();
    }
}
