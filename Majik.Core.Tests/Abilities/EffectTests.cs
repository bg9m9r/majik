using FluentAssertions;
using Majik.Core.Abilities;
using Xunit;

namespace Majik.Core.Tests.Abilities;

/// <summary>
/// Unit tests for Effect class.
/// Tests effect execution.
/// </summary>
public class EffectTests
{
    [Fact]
    public void Constructor_ValidInput_CreatesEffect()
    {
        // Arrange
        var action = new Action(() => { });

        // Act
        var effect = new Effect("Deal 3 damage", action);

        // Assert
        effect.Description.Should().Be("Deal 3 damage");
    }

    [Fact]
    public void Constructor_NullDescription_ThrowsException()
    {
        // Arrange
        var action = new Action(() => { });

        // Act & Assert
        new Action(() => new Effect(null!, action))
            .Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_NullAction_ThrowsException()
    {
        // Act & Assert
        new Action(() => new Effect("Test", null!))
            .Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Execute_RunsAction()
    {
        // Arrange
        bool executed = false;
        var action = new Action(() => { executed = true; });
        var effect = new Effect("Test", action);

        // Act
        effect.Execute();

        // Assert
        executed.Should().BeTrue();
    }
}
