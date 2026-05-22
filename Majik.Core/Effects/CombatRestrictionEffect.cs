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
/// A null <see cref="Target"/> means "applies to every creature" (mass
/// effects like Falter / Magmatic Chasm / Awe for the Guilds). A specific
/// target scopes the restriction to that one creature (Mark for Death,
/// Ground Rift).
/// </summary>
public sealed class CombatRestrictionEffect : ContinuousEffect
{
    public CombatRestriction Restriction { get; }
    public Creature? Target { get; }
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

    // Restrictions don't change P/T or keywords — they're consulted by the
    // combat validator directly. Layer is irrelevant; Abilities (6) is
    // benign because Apply is a no-op.
    public override Layer Layer => Layer.Abilities;

    // AppliesTo only returns true for the targeted creature (or any creature
    // when Target is null) so that future code paths walking effects per
    // creature don't double-count this effect. Compute still calls Apply,
    // but it's a no-op.
    public override bool AppliesTo(Creature creature) =>
        Target == null || ReferenceEquals(Target, creature);

    public override bool ExpiresAtEndOfTurn => _expiresEot;

    public override void Apply(CreatureCharacteristics chars) { /* no-op — queried instead */ }
}
