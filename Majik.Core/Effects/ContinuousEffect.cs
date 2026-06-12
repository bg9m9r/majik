using Majik.Core.Cards;
using Majik.Core.Game;

namespace Majik.Core.Effects;

/// <summary>
/// CR 613 — a single continuous effect. Applies in <see cref="Layer"/>
/// order. Stays active while <see cref="IsActive"/> returns true.
/// Mutators receive the in-flight <see cref="CreatureCharacteristics"/>
/// and modify it in place.
/// </summary>
public abstract class ContinuousEffect
{
    public abstract Layer Layer { get; }

    /// <summary>Card the effect targets — used by the service to scope.</summary>
    public abstract bool AppliesTo(Creature creature);

    /// <summary>Active while this returns true; service drops it otherwise.</summary>
    public virtual bool IsActive() => true;

    /// <summary>True if this effect expires in the cleanup step (CR 514).</summary>
    public virtual bool ExpiresAtEndOfTurn => false;

    public abstract void Apply(CreatureCharacteristics chars);

    /// <summary>
    /// CR 613 — does this effect apply to <paramref name="permanent"/>?
    /// Default routes to the creature overload when the permanent IS a
    /// creature, otherwise returns false. Permanent-level effects (e.g.
    /// land type-changers like Blood Moon) override.
    /// </summary>
    public virtual bool AppliesTo(Permanent permanent) =>
        permanent is Creature c && AppliesTo(c);

    /// <summary>
    /// CR 613 — mutate the working-set for a permanent. Default dispatches
    /// to <see cref="Apply(CreatureCharacteristics)"/> when the chars are a
    /// creature row; otherwise no-op. Permanent-level effects override.
    /// </summary>
    public virtual void Apply(PermanentCharacteristics chars)
    {
        if (chars is CreatureCharacteristics cc) Apply(cc);
    }

    // Determinism (PLAN 08 prerequisite): CR 613.7 layer-ordering timestamp
    // comes from the per-game logical clock, not wall-clock. Consumed by
    // ContinuousEffectsService layer ordering. Assigned in construction order
    // (the order UtcNow approximated) so layer ordering is unchanged but
    // reproducible on replay.
    public DateTime Timestamp { get; } = LogicalClockScope.Current.NextTimestamp();

    /// <summary>
    /// CR 613.1g — the permanent generating this continuous effect, when one
    /// exists. Null for floating effects (e.g. spell-resolution Giant Growth)
    /// that have no on-battlefield source. The service uses this to suppress
    /// effects whose source has had its abilities removed by a Humility-class
    /// Layer 6 effect.
    /// </summary>
    public virtual Permanent? Source => null;

    /// <summary>
    /// Human-readable label for the action log (CR 613 layer effects).
    /// Defaults to the source card name; notable effects (lords, control
    /// swaps) may override for a more specific phrase.
    /// </summary>
    public virtual string Description => Source?.Name is { Length: > 0 } n ? $"{n} effect" : "continuous effect";

    /// <summary>
    /// CR 613.8 — return true iff this effect depends on <paramref name="other"/>:
    /// applying <c>other</c> first would change this effect's existence, what it
    /// does, or the set of objects it applies to. Default: no dependency.
    /// </summary>
    public virtual bool DependsOn(ContinuousEffect other) => false;

    /// <summary>
    /// Sim-only: the permanent the <see cref="Majik.Core.Simulation.GameStateCloner"/> uses
    /// to locate and re-register this effect on the cloned battlefield. Defaults to
    /// <see cref="Source"/> (correct for effects whose source IS the affected permanent,
    /// e.g. lord/anthem effects). Target-capturing effects — where <see cref="Source"/>
    /// is null or a DIFFERENT permanent from the one the effect applies to (e.g.
    /// <see cref="BecomesPTEffect"/> created by Dress Down or Oko, whose source is the
    /// Dress Down/Oko enchantment/planeswalker but whose <c>_target</c> is the affected
    /// creature) — override this to return their <c>_target</c> field instead.
    ///
    /// <para>This intentionally does NOT override <see cref="Source"/>. <c>Source</c>
    /// carries live semantics (CR 613.6 ability-suppression, CES cache-invalidation
    /// wiring via <c>ActiveEffects</c>); pointing it at <c>_target</c> caused the
    /// Phase 2A B2 regression (DressDownTests / OkoTests failed because
    /// <c>ComputeStrippedSet</c> included the target in the stripped set, then the
    /// suppression filter dropped the BecomesPTEffect). The anchor is sim-only.</para>
    /// </summary>
    internal virtual Permanent? SimAnchorPermanent => Source;

    /// <summary>
    /// Sim-only: reconstruct this effect bound to <paramref name="clonedSource"/> for a
    /// search-sandbox clone, so it re-applies on the cloned battlefield. Returns null when
    /// this effect type isn't (yet) sim-cloneable — the cloner then skips it (eval loses
    /// that effect, acceptable until the bespoke long-tail is ported). P/T effects override.
    ///
    /// <para>Implementations must copy ALL configuration fields from <c>this</c> and point
    /// only the source reference at <paramref name="clonedSource"/>, so the reconstructed
    /// effect is behaviourally identical to the original within the clone universe.</para>
    /// </summary>
    internal virtual ContinuousEffect? CloneForSim(
        Permanent clonedSource,
        System.Func<System.Collections.Generic.IReadOnlyList<Majik.Core.Players.Player>>? clonedPlayers) => null;

    /// <summary>
    /// Lifecycle hook fired by <see cref="ContinuousEffectsService"/> when this
    /// effect is dropped from the registry — during end-of-turn cleanup
    /// (<see cref="ContinuousEffectsService.ExpireEndOfTurn"/>, CR 514.2),
    /// inactive-effect pruning (<see cref="ContinuousEffectsService.Prune"/>),
    /// or explicit <see cref="ContinuousEffectsService.Unregister"/>.
    ///
    /// <para>Most effects are pure mutators of the computed working set and
    /// need no teardown (default: no-op). Effects that mutate <i>actual</i>
    /// game state on registration — e.g.
    /// <see cref="TemporaryControlChangeEffect"/>, which swaps the target's
    /// real <see cref="Permanent.Controller"/> — override this to restore that
    /// state when their duration ends. The service drops the effect from
    /// <c>_effects</c> in the same pass, so the hook fires at most once per
    /// removal; making the override idempotent is still recommended (mirrors
    /// <see cref="GrantAbilityEffect.Revoke"/>).</para>
    /// </summary>
    public virtual void OnExpired() { }
}

/// <summary>
/// Mutable working-set the layer system builds up. Starts from the card's
/// printed values; each layer mutates in turn. The service hands the final
/// result back to <see cref="Creature.Power"/> / Toughness / keyword checks.
/// </summary>
public sealed class CreatureCharacteristics : PermanentCharacteristics
{
    public int Power { get; set; }
    public int Toughness { get; set; }
}
