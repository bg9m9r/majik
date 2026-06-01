using Majik.Core.Abilities;

namespace Majik.Core.Effects;

/// <summary>
/// CR 614 — replacement effect. Inspects an "intent" (a "would-happen"
/// event) and either lets it through unchanged, transforms it, or cancels
/// it (returns null). One-shot effects unregister after firing.
///
/// Each effect type is parameterised by the intent type it inspects;
/// the bus dispatches on the runtime intent type.
///
/// <para>
/// PLAN 08 — replacement effects that PROMPT a player (shock-land "pay 2
/// life", Mox Diamond "discard a land", Dredge "dredge?") override
/// <see cref="ReplaceAsync"/> so the bus can <c>await</c> the agent off the
/// live <see cref="ResolutionContext"/> instead of blocking a thread-pool
/// thread on a sync-over-async bridge. The synchronous <see cref="Replace"/>
/// is retained for the non-prompting / registry-fallback paths and for the
/// many direct-call unit tests that drive the bus via
/// <see cref="ReplacementBus.Apply{TIntent}"/>; its default
/// <see cref="ReplaceAsync"/> shim simply forwards to it.
/// </para>
/// </summary>
public interface IReplacementEffect<TIntent>
    where TIntent : class
{
    bool Applies(TIntent intent, IReadOnlyList<object> history);
    TIntent? Replace(TIntent intent, IReadOnlyList<object> history);

    /// <summary>
    /// PLAN 08 — async replacement (CR 614). Prompting replacements override
    /// this to <c>await</c> the agent off <paramref name="ctx"/>; the default
    /// shim forwards to the synchronous <see cref="Replace"/> so non-prompting
    /// replacements need no change. Called by
    /// <see cref="ReplacementBus.ApplyAsync{TIntent}"/>.
    /// </summary>
    ValueTask<TIntent?> ReplaceAsync(
        TIntent intent, IReadOnlyList<object> history, ResolutionContext ctx)
        => new(Replace(intent, history));

    bool OneShot { get; }
    object? Tag { get; }
}

/// <summary>Convenience implementation backed by delegates.</summary>
public sealed class LambdaReplacement<TIntent> : IReplacementEffect<TIntent>
    where TIntent : class
{
    private readonly Func<TIntent, IReadOnlyList<object>, bool> _applies;
    private readonly Func<TIntent, IReadOnlyList<object>, TIntent?> _replace;
    private readonly Func<TIntent, IReadOnlyList<object>, ResolutionContext, ValueTask<TIntent?>>? _replaceAsync;

    public bool OneShot { get; }
    public object? Tag { get; }

    public LambdaReplacement(
        Func<TIntent, IReadOnlyList<object>, bool> applies,
        Func<TIntent, IReadOnlyList<object>, TIntent?> replace,
        bool oneShot = false,
        object? tag = null)
    {
        _applies = applies ?? throw new ArgumentNullException(nameof(applies));
        _replace = replace ?? throw new ArgumentNullException(nameof(replace));
        OneShot = oneShot;
        Tag = tag;
    }

    /// <summary>
    /// PLAN 08 — async-aware ctor. Supply a <paramref name="replaceAsync"/>
    /// body for prompting replacements (Dredge — CR 702.52) so
    /// <see cref="ReplacementBus.ApplyAsync{TIntent}"/> can <c>await</c> the
    /// agent. The synchronous <paramref name="replace"/> is still required as
    /// the fallback used by <see cref="ReplacementBus.Apply{TIntent}"/> (the
    /// registry-derived / no-context path + direct-call unit tests).
    /// </summary>
    public LambdaReplacement(
        Func<TIntent, IReadOnlyList<object>, bool> applies,
        Func<TIntent, IReadOnlyList<object>, TIntent?> replace,
        Func<TIntent, IReadOnlyList<object>, ResolutionContext, ValueTask<TIntent?>> replaceAsync,
        bool oneShot = false,
        object? tag = null)
    {
        _applies = applies ?? throw new ArgumentNullException(nameof(applies));
        _replace = replace ?? throw new ArgumentNullException(nameof(replace));
        _replaceAsync = replaceAsync ?? throw new ArgumentNullException(nameof(replaceAsync));
        OneShot = oneShot;
        Tag = tag;
    }

    public bool Applies(TIntent intent, IReadOnlyList<object> history) => _applies(intent, history);
    public TIntent? Replace(TIntent intent, IReadOnlyList<object> history) => _replace(intent, history);

    public ValueTask<TIntent?> ReplaceAsync(
        TIntent intent, IReadOnlyList<object> history, ResolutionContext ctx)
        => _replaceAsync != null
            ? _replaceAsync(intent, history, ctx)
            : new ValueTask<TIntent?>(_replace(intent, history));
}
