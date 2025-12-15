using FluentAssertions;
using Majik.Core.Events;
using Majik.Core.Players;
using Xunit;

namespace Majik.Core.Tests.Events;

/// <summary>
/// Unit tests for EventBus.
/// Tests event subscription, unsubscription, and publishing.
/// </summary>
public class EventBusTests
{
    [Fact]
    public void Subscribe_Handler_SubscribesToEvent()
    {
        // Arrange
        var eventBus = new EventBus();
        bool handlerCalled = false;

        // Act
        eventBus.Subscribe<GameStartedEvent>(evt => { handlerCalled = true; });
        eventBus.Publish(new GameStartedEvent());

        // Assert
        handlerCalled.Should().BeTrue();
    }

    [Fact]
    public void Subscribe_MultipleHandlers_AllHandlersCalled()
    {
        // Arrange
        var eventBus = new EventBus();
        int callCount = 0;

        // Act
        eventBus.Subscribe<GameStartedEvent>(evt => { callCount++; });
        eventBus.Subscribe<GameStartedEvent>(evt => { callCount++; });
        eventBus.Publish(new GameStartedEvent());

        // Assert
        callCount.Should().Be(2);
    }

    [Fact]
    public void Unsubscribe_Handler_RemovesHandler()
    {
        // Arrange
        var eventBus = new EventBus();
        bool handlerCalled = false;
        Action<GameStartedEvent> handler = evt => { handlerCalled = true; };

        // Act
        eventBus.Subscribe(handler);
        eventBus.Unsubscribe(handler);
        eventBus.Publish(new GameStartedEvent());

        // Assert
        handlerCalled.Should().BeFalse();
    }

    [Fact]
    public void Publish_NoSubscribers_DoesNotThrow()
    {
        // Arrange
        var eventBus = new EventBus();

        // Act & Assert
        eventBus.Invoking(e => e.Publish(new GameStartedEvent()))
            .Should().NotThrow();
    }

    [Fact]
    public void Publish_DifferentEventType_DoesNotCallHandler()
    {
        // Arrange
        var eventBus = new EventBus();
        bool handlerCalled = false;

        // Act
        eventBus.Subscribe<GameStartedEvent>(evt => { handlerCalled = true; });
        eventBus.Publish(new TurnStartedEvent(new Player("Alice", 20), 1));

        // Assert
        handlerCalled.Should().BeFalse();
    }
}
