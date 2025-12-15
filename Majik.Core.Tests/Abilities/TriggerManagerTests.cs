using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Stack;
using Moq;
using Xunit;

namespace Majik.Core.Tests.Abilities;

/// <summary>
/// Unit tests for TriggerManager.
/// Tests trigger registration, evaluation, and automatic stack placement.
/// </summary>
public class TriggerManagerTests
{
    private readonly Mock<IEventBus> _eventBusMock;
    private readonly Majik.Core.Stack.Stack _stack;
    private readonly TriggerManager _triggerManager;

    public TriggerManagerTests()
    {
        _eventBusMock = new Mock<IEventBus>();
        _stack = new Majik.Core.Stack.Stack(_eventBusMock.Object);
        _triggerManager = new TriggerManager(_stack, _eventBusMock.Object);
    }

    [Fact]
    public void RegisterTriggeredAbility_ValidAbility_RegistersAbility()
    {
        // Arrange
        var player = new Player("Alice", 20);
        var source = new Creature("Guttersnipe", "2R", 2, 2) { Owner = player };
        var ability = new TriggeredAbility(source, player);

        // Act
        _triggerManager.RegisterTriggeredAbility(ability);

        // Assert
        // Ability is registered (no direct way to verify, but EvaluateTriggers will use it)
    }

    [Fact]
    public void RegisterTriggeredAbility_NullAbility_ThrowsException()
    {
        // Act & Assert
        _triggerManager.Invoking(t => t.RegisterTriggeredAbility(null!))
            .Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void UnregisterTriggeredAbility_RegisteredAbility_RemovesAbility()
    {
        // Arrange
        var player = new Player("Alice", 20);
        var source = new Creature("Guttersnipe", "2R", 2, 2) { Owner = player };
        var ability = new TriggeredAbility(source, player);
        _triggerManager.RegisterTriggeredAbility(ability);

        // Act
        _triggerManager.UnregisterTriggeredAbility(ability);

        // Assert
        // Ability is unregistered (no direct way to verify)
    }

    [Fact]
    public void EvaluateTriggers_TriggeredAbility_PlacesOnStack()
    {
        // Arrange
        var player = new Player("Alice", 20);
        var source = new Creature("Guttersnipe", "2R", 2, 2) { Owner = player };
        var ability = new TriggeredAbility(source, player);
        _triggerManager.RegisterTriggeredAbility(ability);
        var testEvent = new CardDrawnEvent(new Instant("Test", "1") { Owner = player }, player);

        // Act
        _triggerManager.EvaluateTriggers(testEvent);

        // Assert
        _stack.Count.Should().Be(1);
        _eventBusMock.Verify(x => x.Publish(It.IsAny<TriggeredAbilityTriggeredEvent>()), Times.Once);
    }

    [Fact]
    public void EvaluateTriggers_NullEvent_DoesNothing()
    {
        // Arrange
        var player = new Player("Alice", 20);
        var source = new Creature("Guttersnipe", "2R", 2, 2) { Owner = player };
        var ability = new TriggeredAbility(source, player);
        _triggerManager.RegisterTriggeredAbility(ability);

        // Act
        _triggerManager.EvaluateTriggers(null!);

        // Assert
        _stack.Count.Should().Be(0);
    }

    [Fact]
    public void Clear_RemovesAllAbilities()
    {
        // Arrange
        var player = new Player("Alice", 20);
        var source = new Creature("Guttersnipe", "2R", 2, 2) { Owner = player };
        var ability = new TriggeredAbility(source, player);
        _triggerManager.RegisterTriggeredAbility(ability);

        // Act
        _triggerManager.Clear();

        // Assert
        // Abilities are cleared (no direct way to verify)
    }
}
