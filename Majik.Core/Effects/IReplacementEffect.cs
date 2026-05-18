namespace Majik.Core.Effects;

/// <summary>
/// CR 614 — replacement effect. Inspects an "intent" (a "would-happen"
/// event) and either lets it through unchanged, transforms it, or cancels
/// it (returns null). One-shot effects unregister after firing.
///
/// Each effect type is parameterised by the intent type it inspects;
/// the bus dispatches on the runtime intent type.
/// </summary>
public interface IReplacementEffect<TIntent>
    where TIntent : class
{
    bool Applies(TIntent intent, IReadOnlyList<object> history);
    TIntent? Replace(TIntent intent, IReadOnlyList<object> history);
    bool OneShot { get; }
    object? Tag { get; }
}

/// <summary>Convenience implementation backed by delegates.</summary>
public sealed class LambdaReplacement<TIntent> : IReplacementEffect<TIntent>
    where TIntent : class
{
    private readonly Func<TIntent, IReadOnlyList<object>, bool> _applies;
    private readonly Func<TIntent, IReadOnlyList<object>, TIntent?> _replace;

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

    public bool Applies(TIntent intent, IReadOnlyList<object> history) => _applies(intent, history);
    public TIntent? Replace(TIntent intent, IReadOnlyList<object> history) => _replace(intent, history);
}
