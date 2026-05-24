namespace Majik.Core.Zones;

/// <summary>
/// Reason an engine subsystem is moving a card between zones. Read by
/// <see cref="Majik.Core.CardData.OracleSpellBinder.MoveToGraveyard(Majik.Core.Cards.ICard, ZoneMoveReason)"/>
/// to decide whether Indestructible (CR 702.12) and the active
/// Regeneration shield (CR 701.15) gate the move.
///
/// Only <see cref="Destroy"/> is a "destroy" effect under CR 701.7 — the
/// only reason that consults indestructible / regeneration. Sacrifice,
/// state-based death, exile-as-destroy substitutes, and bounce all bypass
/// indestructible per CR 701.16 / CR 701.18 / CR 702.12b.
/// </summary>
public enum ZoneMoveReason
{
    /// <summary>
    /// CR 701.7 — a "destroy" effect (Murder, Wrath of God, etc.).
    /// Routes through the indestructible / regeneration gate before any
    /// zone move occurs.
    /// </summary>
    Destroy = 0,

    /// <summary>
    /// CR 701.16 — a "sacrifice" cost or effect. Bypasses indestructible
    /// (CR 702.12b — "A permanent with indestructible can't be destroyed.
    /// Such permanents aren't destroyed by lethal damage, and they ignore
    /// the state-based action that destroys creatures with lethal damage."
    /// — sacrifice is not a destroy effect).
    /// </summary>
    Sacrifice = 1,

    /// <summary>
    /// CR 704 — state-based action put this permanent in the graveyard
    /// (creature with 0 toughness, Aura with no legal attachment, etc.).
    /// SBAs already filter for indestructible / regeneration upstream of
    /// the actual zone move (<see cref="Majik.Core.Rules.Sba.Checks.CreatureDeathCheck"/>);
    /// passing this reason skips the binder's gate to avoid double-gating.
    /// </summary>
    StateBasedAction = 2,

    /// <summary>
    /// CR 701.18 / generic — caller is moving a card to the graveyard for
    /// some other reason that's not a "destroy" effect (mill resolved to
    /// battlefield card, discard, planeswalker loyalty reaching 0, etc.).
    /// Bypasses the destroy gate.
    /// </summary>
    Other = 3,

    /// <summary>
    /// CR 701.7 / 701.15c — a "destroy" effect with a "can't be
    /// regenerated" rider (Wrath of God, Damnation, Day of Judgment's
    /// "no regeneration" wording, Terminate). Indestructible still
    /// cancels the destroy (CR 702.12b is unconditional), but
    /// regeneration shields are NOT consumed — the spell tells them they
    /// don't apply, and the permanent goes to the graveyard.
    /// </summary>
    DestroyNoRegeneration = 4,
}
