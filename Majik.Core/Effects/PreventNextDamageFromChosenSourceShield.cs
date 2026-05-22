using Majik.Core.Players;

namespace Majik.Core.Effects;

/// <summary>
/// CR 615 — one-shot "the next time a source of your choice would deal
/// damage to you this turn, prevent that damage" shield. Backs the
/// Deflecting Palm / Honorable Passage / Intervention Pact / Reverse
/// Damage family.
///
/// The shield is registered on resolution and fires at most once: the
/// first <see cref="DamageIntent"/> aimed at <see cref="Beneficiary"/>
/// is cancelled, then an optional <see cref="OnPrevent"/> callback runs
/// with the prevented amount so riders ("deals that much damage to that
/// source's controller", "you gain life equal to the damage prevented")
/// can fire. Auto-drops at end of turn via <see cref="IEndOfTurnExpirable"/>.
///
/// "Choose a source" is lossy at v1 — we accept the first qualifying
/// damage intent rather than gating on a player-selected source. This
/// matches every text in the family for the common case (caster targeted
/// directly by a single damage spell or combat damage step).
/// </summary>
public sealed class PreventNextDamageFromChosenSourceShield
    : IReplacementEffect<DamageIntent>, IEndOfTurnExpirable
{
    /// <summary>
    /// The player the damage is being prevented for ("you" in oracle
    /// text). v1 templates target the caster.
    /// </summary>
    public Player Beneficiary { get; }

    /// <summary>
    /// Optional rider — invoked with the amount of damage prevented
    /// once the intent is cancelled. Used by Deflecting Palm (deals that
    /// much damage to source's controller), Intervention Pact / Reverse
    /// Damage (you gain that much life), etc.
    /// </summary>
    public Action<int, DamageIntent>? OnPrevent { get; }

    public PreventNextDamageFromChosenSourceShield(
        Player beneficiary,
        Action<int, DamageIntent>? onPrevent = null)
    {
        Beneficiary = beneficiary ?? throw new ArgumentNullException(nameof(beneficiary));
        OnPrevent = onPrevent;
    }

    public bool OneShot => true;
    public object? Tag => this;
    public bool ExpiresAtEndOfTurn => true;

    public bool Applies(DamageIntent intent, IReadOnlyList<object> history) =>
        intent.Amount > 0 && ReferenceEquals(intent.TargetPlayer, Beneficiary);

    public DamageIntent? Replace(DamageIntent intent, IReadOnlyList<object> history)
    {
        // CR 615.1 — prevention cancels the damage entirely. Capture the
        // amount BEFORE we return null so the rider sees what was prevented.
        OnPrevent?.Invoke(intent.Amount, intent);
        return null;
    }
}
