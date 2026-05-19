namespace Majik.Core.Events;

/// <summary>
/// In-process event bus consumed by the engine.
///
/// Two handler shapes are supported: synchronous <see cref="Action{T}"/>
/// for fast-path observers, and asynchronous <see cref="Func{T,Task}"/>
/// for handlers that need to await work (e.g. transport-layer bridges
/// pushing to a WebSocket). The bus invokes every handler under a
/// try/catch so a single misbehaving subscriber cannot abort the
/// publish loop.
/// </summary>
public interface IEventBus
{
    /// <summary>Subscribe a synchronous handler for events of type T.</summary>
    void Subscribe<T>(Action<T> handler) where T : GameEvent;

    /// <summary>Subscribe an asynchronous handler for events of type T.
    /// Async handlers are awaited inside <see cref="PublishAsync"/>; from
    /// the sync <see cref="Publish"/> path they are scheduled and observed
    /// for exceptions but not awaited.</summary>
    void Subscribe<T>(Func<T, Task> handler) where T : GameEvent;

    /// <summary>Remove a previously subscribed synchronous handler.</summary>
    void Unsubscribe<T>(Action<T> handler) where T : GameEvent;

    /// <summary>Remove a previously subscribed asynchronous handler.</summary>
    void Unsubscribe<T>(Func<T, Task> handler) where T : GameEvent;

    /// <summary>Publish synchronously. Async handlers are dispatched but
    /// not awaited — fine for in-process observers, not for handlers that
    /// must complete before the caller proceeds.</summary>
    void Publish<T>(T @event) where T : GameEvent;

    /// <summary>Publish and await every async handler in addition to
    /// running every sync handler. Use this on async code paths (e.g.
    /// API/transport) to guarantee handler completion before the
    /// engine continues.</summary>
    Task PublishAsync<T>(T @event) where T : GameEvent;

    /// <summary>Subscribe to every event regardless of concrete type.
    /// Used by the trigger manager (Rule 603) and any cross-cutting
    /// observer (audit log, transport bridge).</summary>
    void SubscribeAll(Action<GameEvent> handler);

    /// <summary>Async variant of <see cref="SubscribeAll"/>.</summary>
    void SubscribeAll(Func<GameEvent, Task> handler);

    /// <summary>Remove a handler previously added via the sync
    /// <see cref="SubscribeAll(Action{GameEvent})"/>.</summary>
    void UnsubscribeAll(Action<GameEvent> handler);

    /// <summary>Remove a handler previously added via the async
    /// <see cref="SubscribeAll(Func{GameEvent,Task})"/>.</summary>
    void UnsubscribeAll(Func<GameEvent, Task> handler);
}
