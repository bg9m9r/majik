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
    /// CR 613.8 — return true iff this effect depends on <paramref name="other"/>:
    /// applying <c>other</c> first would change this effect's existence, what it
    /// does, or the set of objects it applies to. Default: no dependency.
    /// </summary>
    public virtual bool DependsOn(ContinuousEffect other) => false;
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
