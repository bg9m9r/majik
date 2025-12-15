using Majik.Core.Events;
using System.Collections.Generic;

namespace Majik.Core.Tests.Helpers;

/// <summary>
/// Test implementation of IEventBus that captures events for verification.
/// </summary>
public class TestEventBus : IEventBus
{
    private readonly List<GameEvent> _publishedEvents = new();
    private readonly Dictionary<Type, List<Delegate>> _handlers = new();

    public IReadOnlyList<GameEvent> PublishedEvents => _publishedEvents.AsReadOnly();

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
        }
    }

    public void Publish<T>(T @event) where T : GameEvent
    {
        _publishedEvents.Add(@event);

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

    public void Clear()
    {
        _publishedEvents.Clear();
        _handlers.Clear();
    }

    public T? GetLastEventOfType<T>() where T : GameEvent
    {
        return _publishedEvents.OfType<T>().LastOrDefault();
    }

    public int GetEventCount<T>() where T : GameEvent
    {
        return _publishedEvents.OfType<T>().Count();
    }
}
