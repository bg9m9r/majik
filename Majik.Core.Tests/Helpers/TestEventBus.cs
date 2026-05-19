using Majik.Core.Events;

namespace Majik.Core.Tests.Helpers;

/// <summary>
/// Captures every published event for verification while delegating
/// subscriber dispatch to the production <see cref="EventBus"/>. Inherits
/// the full sync + async surface so new contract changes don't require
/// re-implementing the bus in tests.
/// </summary>
public class TestEventBus : EventBus
{
    private readonly List<GameEvent> _publishedEvents = new();

    public IReadOnlyList<GameEvent> PublishedEvents => _publishedEvents.AsReadOnly();

    public new void Publish<T>(T @event) where T : GameEvent
    {
        _publishedEvents.Add(@event);
        base.Publish(@event);
    }

    public new Task PublishAsync<T>(T @event) where T : GameEvent
    {
        _publishedEvents.Add(@event);
        return base.PublishAsync(@event);
    }

    public void Clear() => _publishedEvents.Clear();

    public T? GetLastEventOfType<T>() where T : GameEvent
        => _publishedEvents.OfType<T>().LastOrDefault();

    public int GetEventCount<T>() where T : GameEvent
        => _publishedEvents.OfType<T>().Count();
}
