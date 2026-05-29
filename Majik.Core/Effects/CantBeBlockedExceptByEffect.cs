using Majik.Core.Cards;

namespace Majik.Core.Effects;

/// <summary>
/// CR 509.1b / 702.x — "[source] can't be blocked except by [predicate]"
/// continuous effect. While registered against the source's
/// <see cref="Creature.ActiveEffects"/>, a would-be blocker is legal iff
/// <see cref="AllowedBlockerPredicate"/> returns true on it. Multiple such
/// effects on the same attacker INTERSECT — every active predicate must
/// allow the blocker for the block to be legal (CR 509.1b: any single
/// "can't be blocked" effect is enough to forbid).
///
/// Examples:
/// <list type="bullet">
///   <item>Signal Pest — predicate returns true for creatures with Flying
///         or Reach.</item>
///   <item>Phyrexian Crusader — predicate returns true for creatures
///         without protection-from-black (etc.). Conditions vary by card.</item>
///   <item>Slith Firewalker / Skulking Knight — predicate returns true
///         only for creatures of a specific power threshold.</item>
/// </list>
///
/// The effect's <see cref="Apply(CreatureCharacteristics)"/> is a no-op —
/// block legality is queried directly by
/// <see cref="Majik.Core.Combat.BlockLegality.CanBlock"/> via the source
/// creature's <see cref="Creature.ActiveEffects"/> list. Layer is set to
/// Abilities (6) which is benign because Apply is a no-op.
/// </summary>
public class CantBeBlockedExceptByEffect : ContinuousEffect
{
    /// <summary>
    /// The attacker the restriction applies to. Block legality is queried
    /// from this creature's <see cref="Creature.ActiveEffects"/>.
    /// </summary>
    public ICard SourceCard { get; }

    /// <summary>
    /// Predicate against would-be blockers. Returns true iff the blocker is
    /// allowed to block <see cref="SourceCard"/>. Predicate must be pure /
    /// side-effect-free — it is evaluated repeatedly by the combat validator.
    /// </summary>
    public Func<ICard, bool> AllowedBlockerPredicate { get; }

    private readonly bool _expiresEot;

    /// <param name="source">The attacker the restriction applies to.</param>
    /// <param name="predicate">Returns true iff the blocker is allowed to
    /// block the source. Must be pure / side-effect free.</param>
    /// <param name="expiresAtEndOfTurn">When true the restriction is swept by
    /// the end-of-turn cleanup (CR 514.2) — used by "this turn" grants such
    /// as Legion Loyalist's Battalion. Defaults to false (a static "can't be
    /// blocked except by …" like Signal Pest persists until the source
    /// leaves the battlefield). Mirrors the
    /// <see cref="CombatRestrictionEffect"/> expiry-flag convention.</param>
    public CantBeBlockedExceptByEffect(
        ICard source,
        Func<ICard, bool> predicate,
        bool expiresAtEndOfTurn = false)
    {
        SourceCard = source ?? throw new ArgumentNullException(nameof(source));
        AllowedBlockerPredicate = predicate ?? throw new ArgumentNullException(nameof(predicate));
        _expiresEot = expiresAtEndOfTurn;
    }

    // Block restrictions are consulted by the combat validator directly via
    // the source creature's ActiveEffects. Layer is irrelevant; Abilities (6)
    // is benign because Apply is a no-op.
    public override Layer Layer => Layer.Abilities;

    // CR 514.2 — when constructed as a "this turn" grant, the end-of-turn
    // cleanup sweep removes the restriction (default: persists like a static
    // "can't be blocked except by …").
    public override bool ExpiresAtEndOfTurn => _expiresEot;

    // CR 613.1g — Source-stripping (Humility) suppresses this effect through
    // the ContinuousEffectsService source-suppression path.
    public override Permanent? Source => SourceCard as Permanent;

    // AppliesTo is true only for the source creature so that future code
    // paths walking effects per creature don't double-count this effect.
    // Apply is a no-op, so it's safe either way.
    public override bool AppliesTo(Creature creature) => ReferenceEquals(SourceCard, creature);

    public override void Apply(CreatureCharacteristics chars) { /* no-op — queried instead */ }
}
