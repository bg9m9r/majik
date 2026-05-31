namespace Majik.Core.Abilities;

/// <summary>
/// Base implementation of an effect.
/// Effects are executed when spells or abilities resolve.
///
/// <para>
/// PLAN 01 migration lever — the class carries BOTH ctors:
/// <list type="bullet">
/// <item>The legacy <c>Effect(string, Action)</c> ctor keeps every existing
/// <c>new Effect("desc", () =&gt; {...})</c> factory compiling unchanged. The
/// stored <see cref="Action"/> is wrapped into a completed-<see cref="ValueTask"/>
/// adapter and also runs directly on the synchronous <see cref="Execute"/>
/// path (no <c>GetAwaiter().GetResult()</c> round-trip), preserving exact
/// behaviour.</item>
/// <item>The new <c>Effect(string, Func&lt;ResolutionContext, ValueTask&gt;)</c>
/// ctor lets effects that need the live agent / game / chosen targets do
/// real async work and read off the <see cref="ResolutionContext"/>.</item>
/// </list>
/// </para>
/// </summary>
public class Effect : IEffect
{
    // Exactly one of these is non-null. _syncAction is set by the legacy ctor;
    // _asyncBody by the async ctor.
    private readonly Action? _syncAction;
    private readonly Func<ResolutionContext, ValueTask>? _asyncBody;

    public string Description { get; }

    /// <summary>
    /// Legacy synchronous effect. The <paramref name="executeAction"/> ignores
    /// the resolution context (captures everything in its closure).
    /// </summary>
    public Effect(string description, Action executeAction)
    {
        Description = description ?? throw new ArgumentNullException(nameof(description));
        _syncAction = executeAction ?? throw new ArgumentNullException(nameof(executeAction));
    }

    /// <summary>
    /// Asynchronous effect. The <paramref name="executeBody"/> receives the
    /// live <see cref="ResolutionContext"/> and may await agent prompts /
    /// game-state work.
    /// </summary>
    public Effect(string description, Func<ResolutionContext, ValueTask> executeBody)
    {
        Description = description ?? throw new ArgumentNullException(nameof(description));
        _asyncBody = executeBody ?? throw new ArgumentNullException(nameof(executeBody));
    }

    public ValueTask ExecuteAsync(ResolutionContext ctx)
    {
        if (_asyncBody != null)
        {
            return _asyncBody(ctx);
        }

        // Legacy sync body — run it and complete synchronously. Exceptions
        // surface synchronously through the returned (already-faulted) task.
        _syncAction!();
        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// Synchronous execution. For legacy sync effects this runs the stored
    /// <see cref="Action"/> directly (no async round-trip); for async-bodied
    /// effects it falls back to the interface shim which blocks on
    /// <see cref="ExecuteAsync"/>.
    /// </summary>
    public void Execute()
    {
        if (_syncAction != null)
        {
            _syncAction();
            return;
        }

        _asyncBody!(ResolutionContext.Legacy).GetAwaiter().GetResult();
    }
}
