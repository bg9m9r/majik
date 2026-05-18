using Majik.Core.Cards;

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

    public DateTime Timestamp { get; } = DateTime.UtcNow;
}

/// <summary>
/// Mutable working-set the layer system builds up. Starts from the card's
/// printed values; each layer mutates in turn. The service hands the final
/// result back to <see cref="Creature.Power"/> / Toughness / keyword checks.
/// </summary>
public sealed class CreatureCharacteristics
{
    public int Power { get; set; }
    public int Toughness { get; set; }
    public HashSet<string> Keywords { get; } = new(StringComparer.OrdinalIgnoreCase);
}
