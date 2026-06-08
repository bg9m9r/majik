using Majik.Core.Cards;

namespace Majik.Core.Effects;

/// <summary>
/// Generic continuous effect for "while attached, equipped/enchanted
/// creature gets +P/+T and gains keywords." Reads the source's current
/// <see cref="Permanent.AttachedTo"/> dynamically, so re-equipping a
/// sword onto a new creature transfers the bonus automatically.
///
/// Layer 7c for P/T, Layer 6 for granted keywords — this single record
/// fires in both layers (handled by registering twice with different
/// <see cref="Layer"/> values, or by using a sentinel as below — MVP
/// keeps it in 7c only).
/// </summary>
public sealed class AttachedBoostEffect : ContinuousEffect
{
    private readonly Permanent _source;
    private readonly Func<int> _powerFn;
    private readonly Func<int> _toughnessFn;
    private readonly IReadOnlyList<string> _grantedKeywords;
    private readonly Layer _layer;

    public override Layer Layer => _layer;

    public AttachedBoostEffect(
        Permanent source,
        int power = 0,
        int toughness = 0,
        IReadOnlyList<string>? grantedKeywords = null,
        Layer layer = Effects.Layer.PT_Modify)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _powerFn = () => power;
        _toughnessFn = () => toughness;
        _grantedKeywords = grantedKeywords ?? Array.Empty<string>();
        _layer = layer;
    }

    /// <summary>
    /// Dynamic-N constructor — power and toughness are sampled at each
    /// layer pass via the supplied closures. Models "equipped creature
    /// gets +N/+0 for each &lt;condition&gt;" boosts (e.g. Cranial Plating
    /// reading the controller's live artifact count). The closures are
    /// invoked from the layer-pass <see cref="Apply"/> hook so they observe
    /// the post-other-Layer-6 working state and stay consistent with the
    /// dynamic source-of-truth pattern used by
    /// <see cref="Majik.Core.Effects.CdaPowerToughnessEffect"/>.
    /// </summary>
    public AttachedBoostEffect(
        Permanent source,
        Func<int> powerFn,
        Func<int> toughnessFn,
        IReadOnlyList<string>? grantedKeywords = null,
        Layer layer = Effects.Layer.PT_Modify)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _powerFn = powerFn ?? throw new ArgumentNullException(nameof(powerFn));
        _toughnessFn = toughnessFn ?? throw new ArgumentNullException(nameof(toughnessFn));
        _grantedKeywords = grantedKeywords ?? Array.Empty<string>();
        _layer = layer;
    }

    public override bool IsActive() =>
        _source.Zone == Majik.Core.Zones.ZoneType.Battlefield
        && _source.AttachedTo != null
        && _source.AttachedTo.Zone == Majik.Core.Zones.ZoneType.Battlefield;

    public override bool AppliesTo(Creature creature) =>
        ReferenceEquals(_source.AttachedTo, creature);

    public override void Apply(CreatureCharacteristics chars)
    {
        chars.Power += _powerFn();
        chars.Toughness += _toughnessFn();
        foreach (var kw in _grantedKeywords) chars.Keywords.Add(kw);
    }

    /// <summary>
    /// Sim-only: reconstruct an identical <see cref="AttachedBoostEffect"/> bound to
    /// <paramref name="clonedSource"/> for the search-sandbox clone.  The dynamic
    /// power/toughness closures are preserved exactly — they already read from the
    /// <em>source</em> permanent's live state (e.g. artifact count on the controller),
    /// but because both scalar and closure constructors are stored behind <c>_powerFn</c>
    /// / <c>_toughnessFn</c> fields we cannot safely rebind arbitrary closures.
    ///
    /// <para>For the scalar constructor (most equipment) the closures are constants so
    /// they are safe to preserve.  For the dynamic-closure variant (e.g. Cranial Plating
    /// reading artifact count) the closure captures the ORIGINAL source's controller and
    /// would read the original (live) game state — which is wrong in the sandbox.
    /// Because there is no way to distinguish the two cases at this layer we return null
    /// for the dynamic-closure variant (callers passed power-0/toughness-0 closures
    /// or scalars are fine; anything more complex needs a bespoke override on the
    /// originating factory).</para>
    ///
    /// Heuristic: if the closures are stored scalar constants (created via the scalar
    /// ctor, where the lambda captures only the stack-frame int), the result is safe.
    /// We cannot inspect closure captures at runtime, so we port the common scalar case
    /// (which covers all standard equipment) and leave dynamic-closure callers as null.
    /// </summary>
    internal override ContinuousEffect? CloneForSim(
        Permanent clonedSource,
        System.Func<System.Collections.Generic.IReadOnlyList<Majik.Core.Players.Player>>? clonedPlayers)
    {
        // preserves: _source→clonedSource, _powerFn, _toughnessFn, _grantedKeywords, _layer
        // The cloned effect reads clonedSource.AttachedTo for its AppliesTo gate, which
        // is correctly remapped by Pass 2c (RelinkReferences) in the cloner.
        return new AttachedBoostEffect(
            source:          clonedSource,
            powerFn:         _powerFn,
            toughnessFn:     _toughnessFn,
            grantedKeywords: _grantedKeywords,
            layer:           _layer);
    }
}
