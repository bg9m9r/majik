namespace Majik.Core.Effects;

/// <summary>
/// CR 615 — "Prevent the next N damage that would be dealt to any target
/// this turn." Maintains a per-turn pool of damage points (<see cref="RemainingPool"/>).
/// Each incoming <see cref="DamageIntent"/> is reduced by
/// <c>min(intent.Amount, RemainingPool)</c>; when the pool hits 0 the
/// shield expires and stops applying. If the pool isn't drained before
/// cleanup, <see cref="IEndOfTurnExpirable"/> drops the shield anyway.
///
/// Backs cards in the Healing Salve / Hold at Bay / Mending Hands /
/// Shieldmate's Blessing family — single shield, no target-side filter
/// (the oracle text says "any target", and the shield happily soaks the
/// first qualifying damage intent regardless of who's getting hit).
///
/// CR 615.1 — when a replacement effect reads "prevent the next N", the
/// pool is shared across whatever damage events arrive in registration
/// order; that's the behavior here.
/// </summary>
public sealed class PreventNextNDamageToAnyTargetShield
    : IReplacementEffect<DamageIntent>, IEndOfTurnExpirable
{
    /// <summary>Damage points still available to prevent.</summary>
    public int RemainingPool { get; private set; }

    public PreventNextNDamageToAnyTargetShield(int amount)
    {
        if (amount < 0) throw new ArgumentOutOfRangeException(nameof(amount));
        RemainingPool = amount;
    }

    public bool OneShot => false;
    public object? Tag => this;  // fires once per intent
    public bool ExpiresAtEndOfTurn => true;

    public bool Applies(DamageIntent intent, IReadOnlyList<object> history) =>
        RemainingPool > 0 && intent.Amount > 0;

    public DamageIntent? Replace(DamageIntent intent, IReadOnlyList<object> history)
    {
        var absorbed = Math.Min(RemainingPool, intent.Amount);
        RemainingPool -= absorbed;
        var remaining = intent.Amount - absorbed;
        // CR 615.1 — if the shield fully soaks the intent, return null to
        // cancel it entirely. Otherwise pass through a reduced-Amount copy.
        return remaining == 0 ? null : intent with { Amount = remaining };
    }
}
