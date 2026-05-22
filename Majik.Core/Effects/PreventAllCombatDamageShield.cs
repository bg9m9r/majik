using Majik.Core.Cards;

namespace Majik.Core.Effects;

/// <summary>
/// CR 615 — Fog-style damage prevention. Cancels every
/// <see cref="DamageIntent"/> whose source is a creature (combat damage)
/// for the remainder of the turn.
///
/// Backs <c>FogTemplate</c> ("Prevent all combat damage that would be
/// dealt this turn") and similar single-clause prevention spells.
/// Auto-drops on cleanup via <see cref="IEndOfTurnExpirable"/>.
///
/// Non-combat damage (Lightning Bolt, Shock) is NOT prevented — those
/// spells today bypass <see cref="ReplacementBus"/> entirely. When direct-
/// damage spells start routing through the bus the source-is-Creature
/// gate here will keep Fog combat-only without further change.
/// </summary>
public sealed class PreventAllCombatDamageShield
    : IReplacementEffect<DamageIntent>, IEndOfTurnExpirable
{
    public bool OneShot => false;
    public object? Tag => this;  // unique per shield instance; fires once per intent
    public bool ExpiresAtEndOfTurn => true;

    public bool Applies(DamageIntent intent, IReadOnlyList<object> history) =>
        intent.Source is Creature && intent.Amount > 0;

    public DamageIntent? Replace(DamageIntent intent, IReadOnlyList<object> history) =>
        // Returning null cancels the intent entirely — no damage applied,
        // no lifelink, no deathtouch flag (CR 615.6).
        null;
}
