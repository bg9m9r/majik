namespace Majik.Core.Events;

/// <summary>
/// Default implementation of the event bus.
/// Provides type-safe event subscriptions and publishing.
/// </summary>
public class EventBus : IEventBus
{
    private readonly Dictionary<Type, List<Delegate>> _handlers = new();

    /// <summary>
    /// Subscribe to events of type T.
    /// </summary>
    public void Subscribe<T>(Action<T> handler) where T : GameEvent
    {
        var eventType = typeof(T);
        
        if (!_handlers.ContainsKey(eventType))
        {
            _handlers[eventType] = new List<Delegate>();
        }

        _handlers[eventType].Add(handler);
    }

    /// <summary>
    /// Unsubscribe from events of type T.
    /// </summary>
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

    /// <summary>
    /// Publish an event to all subscribers.
    /// </summary>
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
    }
}
