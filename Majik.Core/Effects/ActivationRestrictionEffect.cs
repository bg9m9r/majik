using Majik.Core.Cards;

namespace Majik.Core.Effects;

/// <summary>
/// CR 602.5 / 605.1a — restricts activation of a permanent's activated
/// abilities. Used by Aura/static-style lockouts that say "&lt;enchanted
/// permanent&gt; can't activate non-mana abilities" (Leyline Binding,
/// Pacifism-class abilities for the activated-ability rider).
///
/// Mirrors <see cref="CombatRestrictionEffect"/>: registered on the
/// permanent's <see cref="ContinuousEffectsService"/> for end-of-turn
/// expiry plumbing (CR 514.2) but its <see cref="Apply"/> is a no-op —
/// activation restrictions are queried directly via
/// <see cref="ContinuousEffectsService.HasActivationRestriction"/>
/// rather than baked into <see cref="CreatureCharacteristics"/>.
///
/// The default is to scope the restriction to non-mana abilities (mana
/// abilities are still permitted because they don't use the stack —
/// CR 605.1a). Set <see cref="ExcludesManaAbilities"/> to false for the
/// rare "can't activate any abilities" shape (Stasis Cell-style lockdowns).
///
/// A null <see cref="Target"/> means "applies to every permanent"
/// (Stasis-style global lockouts). A specific target scopes the
/// restriction to that one permanent (Leyline Binding, Pacifism-rider).
/// </summary>
public sealed class ActivationRestrictionEffect : ContinuousEffect
{
    /// <summary>The permanent whose activated abilities are restricted.
    /// Null means a mass / "every permanent" restriction.</summary>
    public Permanent? Target { get; }

    /// <summary>
    /// When true (default), mana abilities (CR 605) are still allowed —
    /// only non-mana activated abilities are blocked. Matches the
    /// "can't activate non-mana abilities" oracle phrasing.
    /// </summary>
    public bool ExcludesManaAbilities { get; }

    private readonly bool _expiresEot;

    public ActivationRestrictionEffect(
        Permanent? target,
        bool excludesManaAbilities = true,
        bool expiresAtEndOfTurn = false)
    {
        Target = target;
        ExcludesManaAbilities = excludesManaAbilities;
        _expiresEot = expiresAtEndOfTurn;
    }

    // Activation restrictions don't change P/T or keywords — they're
    // consulted by the ability activator directly. Layer is irrelevant;
    // Abilities (6) is benign because Apply is a no-op.
    public override Layer Layer => Layer.Abilities;

    // AppliesTo only returns true for the targeted permanent (or any
    // permanent when Target is null) so future code paths walking effects
    // per permanent don't double-count this effect. Compute still calls
    // Apply, but it's a no-op.
    public override bool AppliesTo(Creature creature) =>
        Target == null || ReferenceEquals(Target, creature);

    public override bool AppliesTo(Permanent permanent) =>
        Target == null || ReferenceEquals(Target, permanent);

    public override bool ExpiresAtEndOfTurn => _expiresEot;

    public override void Apply(CreatureCharacteristics chars) { /* no-op — queried instead */ }
}
