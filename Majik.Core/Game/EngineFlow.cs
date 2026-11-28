namespace Majik.Core.Game;

/// <summary>
/// Marks the async flow a game engine runs on, so state reads can tell the
/// engine (which may walk live game objects) from any other thread (which must
/// read a published snapshot). The owner is set where a facade starts its
/// engine task and flows into every engine continuation and every handler the
/// engine invokes synchronously.
///
/// <para>Consumer-supplied callbacks (event / prompt subscribers, a bot's
/// "thinking" callback, a bot decision sink, diagnostics loggers) are external
/// code: invoke them through <see cref="RunExternal"/> so neither they nor any
/// work they schedule (<c>Task.Run</c>, timers, await continuations capture the
/// ExecutionContext) is ever treated as the engine.</para>
/// </summary>
public static class EngineFlow
{
    private static readonly AsyncLocal<object?> Owner = new();

    /// <summary>The engine that owns the calling flow, or null.</summary>
    internal static object? Current
    {
        get => Owner.Value;
        set => Owner.Value = value;
    }

    /// <summary>
    /// Invoke an external callback with the engine-flow mark cleared, restoring
    /// it afterwards. Exceptions propagate to the caller.
    /// </summary>
    public static void RunExternal(Action callback)
    {
        ArgumentNullException.ThrowIfNull(callback);
        var previous = Owner.Value;
        if (previous is null)
        {
            callback();
            return;
        }
        Owner.Value = null;
        try { callback(); }
        finally { Owner.Value = previous; }
    }
}
