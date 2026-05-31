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
///
/// <para><strong>DEBUG fail-fast:</strong> <c>GameFacade</c> wires
/// <see cref="OnHandlerError"/> to a sink that <em>rethrows</em> in
/// DEBUG builds (via <c>GameFacadeErrorSinkInitializer</c>). This is
/// intentional: event-handler bodies must not throw on normal execution
/// paths — a throw in DEBUG means a real engine bug and the process
/// crash surfaces it immediately. Release builds leave the sink null so
/// handler exceptions are silently swallowed, preserving live-game
/// isolation. Consequence: if you add a new event handler that can throw
/// under ordinary conditions you will see an unhandled-exception crash in
/// tests/DEBUG before anything reaches production.</para>
/// </summary>
public class EventBus : IEventBus
{
    // Copy-on-write handler stores. Each value/field below holds an
    // IMMUTABLE list reference. Subscribe/Unsubscribe replace the reference
    // with a fresh list (never mutate in place); Publish/PublishAsync read
    // the current reference and iterate it directly with NO per-publish copy.
    //
    // Re-entrancy: an in-flight Publish has already captured the list
    // reference for the channel it is iterating, so a Subscribe/Unsubscribe
    // performed by a handler swaps the field to a NEW list and leaves the
    // captured snapshot untouched. This reproduces exactly the old
    // `.ToArray()` semantics — a handler subscribed or removed mid-dispatch
    // is NOT observed by the in-flight loop — without allocating on the hot
    // publish path.
    private Dictionary<Type, IReadOnlyList<Delegate>> _syncHandlers = new();
    private Dictionary<Type, IReadOnlyList<Delegate>> _asyncHandlers = new();
    private IReadOnlyList<Action<GameEvent>> _globalSyncHandlers = Array.Empty<Action<GameEvent>>();
    private IReadOnlyList<Func<GameEvent, Task>> _globalAsyncHandlers = Array.Empty<Func<GameEvent, Task>>();

    /// <summary>
    /// Optional sink invoked when a handler throws. Receives the
    /// exception and the event being published. Set to null to swallow
    /// silently (default). The bus never propagates handler exceptions
    /// to the publisher.
    ///
    /// <para><strong>DEBUG fail-fast:</strong> <c>GameFacade</c> sets this
    /// to a rethrowing sink in DEBUG builds so any handler exception
    /// crashes the process immediately — surfacing real engine bugs during
    /// development and in the test suite. Handler bodies must therefore not
    /// throw on normal execution paths. See <c>GameFacadeErrorSinkInitializer</c>
    /// and the class-level summary for details.</para>
    /// </summary>
    public Action<Exception, GameEvent>? OnHandlerError { get; set; }

    public void Subscribe<T>(Action<T> handler) where T : GameEvent
    {
        ArgumentNullException.ThrowIfNull(handler);
        Add(ref _syncHandlers, typeof(T), handler);
    }

    public void Subscribe<T>(Func<T, Task> handler) where T : GameEvent
    {
        ArgumentNullException.ThrowIfNull(handler);
        Add(ref _asyncHandlers, typeof(T), handler);
    }

    public void Unsubscribe<T>(Action<T> handler) where T : GameEvent
        => Remove(ref _syncHandlers, typeof(T), handler);

    public void Unsubscribe<T>(Func<T, Task> handler) where T : GameEvent
        => Remove(ref _asyncHandlers, typeof(T), handler);

    public void Publish<T>(T @event) where T : GameEvent
    {
        ArgumentNullException.ThrowIfNull(@event);

        var type = typeof(T);
        // Capture each channel's live reference once. COW guarantees the
        // captured list is never mutated by a re-entrant subscribe/unsubscribe
        // (those swap the field), so iterating directly is re-entrancy-safe
        // and matches the old `.ToArray()` snapshot semantics with zero copy.
        if (_syncHandlers.TryGetValue(type, out var sync))
        {
            for (var i = 0; i < sync.Count; i++)
            {
                if (sync[i] is Action<T> typed)
                    SafeInvoke(() => typed(@event), @event);
            }
        }

        if (_asyncHandlers.TryGetValue(type, out var asyncHandlers))
        {
            for (var i = 0; i < asyncHandlers.Count; i++)
            {
                if (asyncHandlers[i] is Func<T, Task> typed)
                    SafeFireAndForget(() => typed(@event), @event);
            }
        }

        var globalSync = _globalSyncHandlers;
        for (var i = 0; i < globalSync.Count; i++)
        {
            var g = globalSync[i];
            SafeInvoke(() => g(@event), @event);
        }

        var globalAsync = _globalAsyncHandlers;
        for (var i = 0; i < globalAsync.Count; i++)
        {
            var g = globalAsync[i];
            SafeFireAndForget(() => g(@event), @event);
        }
    }

    public async Task PublishAsync<T>(T @event) where T : GameEvent
    {
        ArgumentNullException.ThrowIfNull(@event);

        var type = typeof(T);
        if (_syncHandlers.TryGetValue(type, out var sync))
        {
            for (var i = 0; i < sync.Count; i++)
            {
                if (sync[i] is Action<T> typed)
                    SafeInvoke(() => typed(@event), @event);
            }
        }

        if (_asyncHandlers.TryGetValue(type, out var asyncHandlers))
        {
            for (var i = 0; i < asyncHandlers.Count; i++)
            {
                if (asyncHandlers[i] is Func<T, Task> typed)
                    await SafeAwait(() => typed(@event), @event).ConfigureAwait(false);
            }
        }

        var globalSync = _globalSyncHandlers;
        for (var i = 0; i < globalSync.Count; i++)
        {
            var g = globalSync[i];
            SafeInvoke(() => g(@event), @event);
        }

        var globalAsync = _globalAsyncHandlers;
        for (var i = 0; i < globalAsync.Count; i++)
        {
            var g = globalAsync[i];
            await SafeAwait(() => g(@event), @event).ConfigureAwait(false);
        }
    }

    public void SubscribeAll(Action<GameEvent> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _globalSyncHandlers = Append(_globalSyncHandlers, handler);
    }

    public void SubscribeAll(Func<GameEvent, Task> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _globalAsyncHandlers = Append(_globalAsyncHandlers, handler);
    }

    public void UnsubscribeAll(Action<GameEvent> handler)
    {
        if (handler == null) return;
        _globalSyncHandlers = RemoveFrom(_globalSyncHandlers, handler);
    }

    public void UnsubscribeAll(Func<GameEvent, Task> handler)
    {
        if (handler == null) return;
        _globalAsyncHandlers = RemoveFrom(_globalAsyncHandlers, handler);
    }

    // --- Copy-on-write store mutators ---------------------------------
    // Each swaps a fresh list/dictionary reference into the field. Never
    // mutates a list an in-flight Publish may be iterating.

    private static void Add(ref Dictionary<Type, IReadOnlyList<Delegate>> map, Type t, Delegate d)
    {
        var next = new Dictionary<Type, IReadOnlyList<Delegate>>(map);
        if (next.TryGetValue(t, out var existing))
        {
            var list = new List<Delegate>(existing.Count + 1);
            list.AddRange(existing);
            list.Add(d);
            next[t] = list;
        }
        else
        {
            next[t] = new List<Delegate> { d };
        }
        map = next;
    }

    private static void Remove(ref Dictionary<Type, IReadOnlyList<Delegate>> map, Type t, Delegate d)
    {
        if (!map.TryGetValue(t, out var existing)) return;
        var idx = IndexOf(existing, d);
        if (idx < 0) return;

        var next = new Dictionary<Type, IReadOnlyList<Delegate>>(map);
        if (existing.Count == 1)
        {
            next.Remove(t);
        }
        else
        {
            var list = new List<Delegate>(existing.Count - 1);
            for (var i = 0; i < existing.Count; i++)
            {
                if (i != idx) list.Add(existing[i]);
            }
            next[t] = list;
        }
        map = next;
    }

    private static IReadOnlyList<TDelegate> Append<TDelegate>(IReadOnlyList<TDelegate> source, TDelegate d)
    {
        var list = new List<TDelegate>(source.Count + 1);
        list.AddRange(source);
        list.Add(d);
        return list;
    }

    private static IReadOnlyList<TDelegate> RemoveFrom<TDelegate>(IReadOnlyList<TDelegate> source, TDelegate d)
    {
        var idx = -1;
        for (var i = 0; i < source.Count; i++)
        {
            if (Equals(source[i], d)) { idx = i; break; }
        }
        if (idx < 0) return source;

        var list = new List<TDelegate>(source.Count - 1);
        for (var i = 0; i < source.Count; i++)
        {
            if (i != idx) list.Add(source[i]);
        }
        return list;
    }

    private static int IndexOf(IReadOnlyList<Delegate> source, Delegate d)
    {
        for (var i = 0; i < source.Count; i++)
        {
            if (Equals(source[i], d)) return i;
        }
        return -1;
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
