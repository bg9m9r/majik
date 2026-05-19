namespace Majik.Core.Events;

/// <summary>
/// Default in-process <see cref="IEventBus"/>.
///
/// Isolation contract: every handler is invoked inside a try/catch. A
/// throwing handler is reported via the optional
/// <see cref="OnHandlerError"/> sink and the loop continues. This is
/// deliberate — one bad subscriber must not abort delivery to the rest
/// of the engine (combat code, SBA, trigger manager) which depend on
/// observing the same event stream.
/// </summary>
public class EventBus : IEventBus
{
    private readonly Dictionary<Type, List<Delegate>> _syncHandlers = new();
    private readonly Dictionary<Type, List<Delegate>> _asyncHandlers = new();
    private readonly List<Action<GameEvent>> _globalSyncHandlers = new();
    private readonly List<Func<GameEvent, Task>> _globalAsyncHandlers = new();

    /// <summary>
    /// Optional sink invoked when a handler throws. Receives the
    /// exception and the event being published. Set to null to swallow
    /// silently (default). The bus never propagates handler exceptions
    /// to the publisher.
    /// </summary>
    public Action<Exception, GameEvent>? OnHandlerError { get; set; }

    public void Subscribe<T>(Action<T> handler) where T : GameEvent
    {
        ArgumentNullException.ThrowIfNull(handler);
        Add(_syncHandlers, typeof(T), handler);
    }

    public void Subscribe<T>(Func<T, Task> handler) where T : GameEvent
    {
        ArgumentNullException.ThrowIfNull(handler);
        Add(_asyncHandlers, typeof(T), handler);
    }

    public void Unsubscribe<T>(Action<T> handler) where T : GameEvent
        => Remove(_syncHandlers, typeof(T), handler);

    public void Unsubscribe<T>(Func<T, Task> handler) where T : GameEvent
        => Remove(_asyncHandlers, typeof(T), handler);

    public void Publish<T>(T @event) where T : GameEvent
    {
        ArgumentNullException.ThrowIfNull(@event);

        var type = typeof(T);
        if (_syncHandlers.TryGetValue(type, out var sync))
        {
            foreach (var handler in sync.ToArray())
            {
                if (handler is Action<T> typed)
                    SafeInvoke(() => typed(@event), @event);
            }
        }

        if (_asyncHandlers.TryGetValue(type, out var async))
        {
            foreach (var handler in async.ToArray())
            {
                if (handler is Func<T, Task> typed)
                    SafeFireAndForget(() => typed(@event), @event);
            }
        }

        foreach (var g in _globalSyncHandlers.ToArray())
        {
            SafeInvoke(() => g(@event), @event);
        }

        foreach (var g in _globalAsyncHandlers.ToArray())
        {
            SafeFireAndForget(() => g(@event), @event);
        }
    }

    public async Task PublishAsync<T>(T @event) where T : GameEvent
    {
        ArgumentNullException.ThrowIfNull(@event);

        var type = typeof(T);
        if (_syncHandlers.TryGetValue(type, out var sync))
        {
            foreach (var handler in sync.ToArray())
            {
                if (handler is Action<T> typed)
                    SafeInvoke(() => typed(@event), @event);
            }
        }

        if (_asyncHandlers.TryGetValue(type, out var async))
        {
            foreach (var handler in async.ToArray())
            {
                if (handler is Func<T, Task> typed)
                    await SafeAwait(() => typed(@event), @event).ConfigureAwait(false);
            }
        }

        foreach (var g in _globalSyncHandlers.ToArray())
        {
            SafeInvoke(() => g(@event), @event);
        }

        foreach (var g in _globalAsyncHandlers.ToArray())
        {
            await SafeAwait(() => g(@event), @event).ConfigureAwait(false);
        }
    }

    public void SubscribeAll(Action<GameEvent> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _globalSyncHandlers.Add(handler);
    }

    public void SubscribeAll(Func<GameEvent, Task> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _globalAsyncHandlers.Add(handler);
    }

    public void UnsubscribeAll(Action<GameEvent> handler)
    {
        if (handler == null) return;
        _globalSyncHandlers.Remove(handler);
    }

    public void UnsubscribeAll(Func<GameEvent, Task> handler)
    {
        if (handler == null) return;
        _globalAsyncHandlers.Remove(handler);
    }

    private static void Add(Dictionary<Type, List<Delegate>> map, Type t, Delegate d)
    {
        if (!map.TryGetValue(t, out var list))
        {
            list = new List<Delegate>();
            map[t] = list;
        }
        list.Add(d);
    }

    private static void Remove(Dictionary<Type, List<Delegate>> map, Type t, Delegate d)
    {
        if (map.TryGetValue(t, out var list))
        {
            list.Remove(d);
            if (list.Count == 0) map.Remove(t);
        }
    }

    private void SafeInvoke(Action body, GameEvent @event)
    {
        try { body(); }
        catch (Exception ex) { OnHandlerError?.Invoke(ex, @event); }
    }

    private async Task SafeAwait(Func<Task> body, GameEvent @event)
    {
        try { await body().ConfigureAwait(false); }
        catch (Exception ex) { OnHandlerError?.Invoke(ex, @event); }
    }

    private void SafeFireAndForget(Func<Task> body, GameEvent @event)
    {
        // From the sync publish path we cannot await. Catch synchronous
        // throws from the handler invocation itself; observe the returned
        // task so async exceptions still reach OnHandlerError. Handlers
        // run out-of-band; callers needing ordered completion must use
        // PublishAsync instead.
        Task task;
        try { task = body(); }
        catch (Exception ex) { OnHandlerError?.Invoke(ex, @event); return; }

        if (task == null) return;
        _ = task.ContinueWith(
            t =>
            {
                if (t.Exception is { } agg)
                {
                    foreach (var inner in agg.InnerExceptions)
                        OnHandlerError?.Invoke(inner, @event);
                }
            },
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously);
    }
}
