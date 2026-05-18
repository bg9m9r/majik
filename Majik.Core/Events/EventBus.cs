namespace Majik.Core.Events;

/// <summary>
/// Default implementation of the event bus.
/// Provides type-safe event subscriptions and publishing.
/// </summary>
public class EventBus : IEventBus
{
    private readonly Dictionary<Type, List<Delegate>> _handlers = new();
    private readonly List<Action<GameEvent>> _globalHandlers = new();

    public void Subscribe<T>(Action<T> handler) where T : GameEvent
    {
        var eventType = typeof(T);

        if (!_handlers.ContainsKey(eventType))
        {
            _handlers[eventType] = new List<Delegate>();
        }

        _handlers[eventType].Add(handler);
    }

    public void Unsubscribe<T>(Action<T> handler) where T : GameEvent
    {
        var eventType = typeof(T);

        if (_handlers.TryGetValue(eventType, out var handlers))
        {
            handlers.Remove(handler);

            if (handlers.Count == 0)
            {
                _handlers.Remove(eventType);
            }
        }
    }

    public void Publish<T>(T @event) where T : GameEvent
    {
        var eventType = typeof(T);

        if (_handlers.TryGetValue(eventType, out var handlers))
        {
            foreach (var handler in handlers.ToList())
            {
                if (handler is Action<T> typedHandler)
                {
                    typedHandler(@event);
                }
            }
        }

        foreach (var global in _globalHandlers.ToList())
        {
            global(@event);
        }
    }

    public void SubscribeAll(Action<GameEvent> handler)
    {
        if (handler == null)
        {
            throw new ArgumentNullException(nameof(handler));
        }

        _globalHandlers.Add(handler);
    }

    public void UnsubscribeAll(Action<GameEvent> handler)
    {
        if (handler == null)
        {
            return;
        }

        _globalHandlers.Remove(handler);
    }
}
