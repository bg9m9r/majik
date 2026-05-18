namespace Majik.Core.Events;

/// <summary>
/// Interface for the event bus that handles event publishing and subscription.
/// </summary>
public interface IEventBus
{
    /// <summary>
    /// Subscribe to events of type T.
    /// </summary>
    /// <typeparam name="T">The type of event to subscribe to.</typeparam>
    /// <param name="handler">The handler function to call when the event is published.</param>
    void Subscribe<T>(Action<T> handler) where T : GameEvent;

    /// <summary>
    /// Unsubscribe from events of type T.
    /// </summary>
    /// <typeparam name="T">The type of event to unsubscribe from.</typeparam>
    /// <param name="handler">The handler function to remove.</param>
    void Unsubscribe<T>(Action<T> handler) where T : GameEvent;

    /// <summary>
    /// Publish an event to all subscribers.
    /// </summary>
    /// <typeparam name="T">The type of event being published.</typeparam>
    /// <param name="event">The event to publish.</param>
    void Publish<T>(T @event) where T : GameEvent;

    /// <summary>
    /// Subscribe to every published event regardless of concrete type.
    /// Used by the trigger manager so it can evaluate any event against
    /// registered triggered abilities (Rule 603).
    /// </summary>
    void SubscribeAll(Action<GameEvent> handler);

    /// <summary>
    /// Remove a handler previously added via <see cref="SubscribeAll"/>.
    /// </summary>
    void UnsubscribeAll(Action<GameEvent> handler);
}
