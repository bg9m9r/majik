using FluentAssertions;
using Majik.Core.Domain.ValueObjects;
using Xunit;

namespace Majik.Core.Tests.Domain.ValueObjects;

/// <summary>
/// Unit tests for ResolutionState value object.
/// Tests state creation and transitions.
/// </summary>
public class ResolutionStateTests
{
    [Fact]
    public void NotResolving_CreatesNotResolvingState()
    {
        // Act
        var state = ResolutionState.NotResolving();

        // Assert
        state.IsResolving.Should().BeFalse();
        state.ResolvedAt.Should().BeNull();
    }

    [Fact]
    public void Resolving_CreatesResolvingState()
    {
        // Act
        var state = ResolutionState.Resolving();

        // Assert
        state.IsResolving.Should().BeTrue();
        state.ResolvedAt.Should().NotBeNull();
    }

    [Fact]
    public void Resolved_WithTimestamp_CreatesResolvedState()
    {
        // Arrange
        var timestamp = DateTime.UtcNow;

        // Act
        var state = ResolutionState.Resolved(timestamp);

        // Assert
        state.IsResolving.Should().BeFalse();
        state.ResolvedAt.Should().Be(timestamp);
    }

    [Fact]
    public void Equals_SameValues_ReturnsTrue()
    {
        // Arrange
        var timestamp = DateTime.UtcNow;
        var state1 = ResolutionState.Resolved(timestamp);
        var state2 = ResolutionState.Resolved(timestamp);

        // Act & Assert
        state1.Equals(state2).Should().BeTrue();
        (state1 == state2).Should().BeTrue();
    }

    [Fact]
    public void Equals_DifferentValues_ReturnsFalse()
    {
        // Arrange
        var state1 = ResolutionState.NotResolving();
        var state2 = ResolutionState.Resolving();

        // Act & Assert
        state1.Equals(state2).Should().BeFalse();
        (state1 != state2).Should().BeTrue();
    }

    [Fact]
    public void GetHashCode_SameValues_ReturnsSameHashCode()
    {
        // Arrange
        var timestamp = DateTime.UtcNow;
        var state1 = ResolutionState.Resolved(timestamp);
        var state2 = ResolutionState.Resolved(timestamp);

        // Act & Assert
        state1.GetHashCode().Should().Be(state2.GetHashCode());
    }
}
