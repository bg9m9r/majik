using Majik.Core.Cards;

namespace Majik.Core.Effects;

/// <summary>
/// CR 509.1c / 508.1c / 702.x — per-turn (or longer) combat restriction.
/// Registered on the <see cref="ContinuousEffectsService"/> alongside P/T
/// and keyword effects so it shares the same end-of-turn expiry plumbing
/// (CR 514.2), but its <see cref="Apply"/> is a no-op — restrictions are
/// queried directly via
/// <see cref="ContinuousEffectsService.HasRestriction"/> rather than baked
/// into <see cref="CreatureCharacteristics"/>.
///
/// Three scoping modes:
/// - <b>Single target</b>: <see cref="Target"/> set, <see cref="Predicate"/>
///   null — restriction scopes to one creature (Mark for Death, Bound in
///   Gold, Blighted Agent).
/// - <b>Mass</b>: both <see cref="Target"/> and <see cref="Predicate"/>
///   null — applies to every creature (Falter, Magmatic Chasm).
/// - <b>Predicate</b>: <see cref="Predicate"/> set — applies to whichever
///   creatures satisfy the predicate at query time (Ensnaring Bridge's
///   "creatures with power greater than the number of cards in your
///   hand can't attack" — recomputed at every attack-validation pass so
///   hand-size + P/T fluctuations take effect immediately).
///
/// The <see cref="IsActiveGate"/> closure is consulted by
/// <see cref="IsActive"/>; supplying it gates the restriction on, e.g.,
/// the source permanent staying on the battlefield, without sub-classing
/// (the type stays sealed so the layer system's effect-bag handling
/// stays uniform).
/// </summary>
public sealed class CombatRestrictionEffect : ContinuousEffect
{
    public CombatRestriction Restriction { get; }
    public Creature? Target { get; }

    /// <summary>
    /// Per-creature predicate used by the "applies to whichever creatures
    /// satisfy a dynamic condition" mode. Null for the static-target and
    /// mass modes. When set, <see cref="Target"/> must be null —
    /// <see cref="ContinuousEffectsService.HasRestriction"/> evaluates the
    /// predicate against the queried creature on every attack/block
    /// validation pass (Ensnaring Bridge — power vs. hand size,
    /// recomputed each combat).
    /// </summary>
    public Func<Creature, bool>? Predicate { get; }

    /// <summary>
    /// Optional active-while gate. When supplied, <see cref="IsActive"/>
    /// returns the gate's value (used by the layer system's prune sweep
    /// and by HasRestriction's per-effect filter). Lets a continuous
    /// effect track its source-permanent zone without subclassing
    /// (Ensnaring Bridge — gate on <c>bridge.Zone == Battlefield</c>).
    /// </summary>
    public Func<bool>? IsActiveGate { get; }

    private readonly bool _expiresEot;

    public CombatRestrictionEffect(
        CombatRestriction restriction,
        Creature? target,
        bool expiresAtEndOfTurn = true)
    {
        Restriction = restriction;
        Target = target;
        _expiresEot = expiresAtEndOfTurn;
    }

    /// <summary>
    /// Predicate-mode constructor. The restriction applies to any creature
    /// for which <paramref name="predicate"/> returns true at query time.
    /// <paramref name="isActiveGate"/> (optional) gates the entire effect on
    /// some external state (e.g. the source permanent being on the
    /// battlefield); when null the effect is always active until
    /// unregistered.
    /// </summary>
    public CombatRestrictionEffect(
        CombatRestriction restriction,
        Func<Creature, bool> predicate,
        Func<bool>? isActiveGate = null,
        bool expiresAtEndOfTurn = false)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        Restriction = restriction;
        Target = null;
        Predicate = predicate;
        IsActiveGate = isActiveGate;
        _expiresEot = expiresAtEndOfTurn;
    }

    // Restrictions don't change P/T or keywords — they're consulted by the
    // combat validator directly. Layer is irrelevant; Abilities (6) is
    // benign because Apply is a no-op.
    public override Layer Layer => Layer.Abilities;

    // AppliesTo returns true for the targeted creature (single-target
    // mode), for every creature (mass mode), or for whichever creature
    // satisfies the predicate (predicate mode). Compute still calls
    // Apply, but it's a no-op.
    public override bool AppliesTo(Creature creature)
    {
        if (Predicate != null) return Predicate(creature);
        return Target == null || ReferenceEquals(Target, creature);
    }

    public override bool ExpiresAtEndOfTurn => _expiresEot;

    public override bool IsActive() => IsActiveGate?.Invoke() ?? true;

    public override void Apply(CreatureCharacteristics chars) { /* no-op — queried instead */ }
}
