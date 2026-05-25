using Majik.Core.Players;

namespace Majik.Core.Spells;

/// <summary>
/// CR 701.59 (2024 Bloomburrow errata) — "Gift" cast-time alternative.
/// A spell with a gift clause may, as it is cast, have its caster
/// "promise an opponent a gift". If the promise is made, the chosen
/// opponent receives the named gift (a tapped 1/1 blue Fish token
/// for Into the Flood Maw) and the spell's printed body upgrades
/// (Into the Flood Maw flips its target predicate from "creature an
/// opponent controls" to "nonland permanent an opponent controls").
///
/// <para>v1 contract — gift delivery is a CAST-TIME side-effect
/// (DeliverTo runs as the gift promise is recorded by SpellCastFlow
/// BEFORE the spell hits the stack), not a resolve-time effect.
/// This deviates from the strict CR 701.59 reading ("create the
/// token before the spell's other effects" places it inside the
/// resolution, so a countered gift spell would deliver no token);
/// we ship the simpler engine model so the recipient receives the
/// gift even when the spell is later countered. Documented in the
/// xmldoc on each implementing factory so the deviation is visible
/// at the call site.</para>
///
/// <para>Implementing factories also branch their resolve body on
/// <see cref="Majik.Core.Cards.Card.HasGiftPromised"/> to apply the
/// upgraded printed effect when the gift was promised.</para>
/// </summary>
public interface IGiftClause
{
    /// <summary>
    /// Human-readable label for the promised gift. Surfaced by the
    /// agent prompt UI ("Promise <i>{Description}</i> to an opponent?").
    /// Convention: lower-case noun phrase ("a tapped 1/1 blue Fish
    /// creature token").
    /// </summary>
    string Description { get; }

    /// <summary>
    /// Materialize the promised gift for <paramref name="recipient"/>.
    /// Called by <see cref="Majik.Core.Game.SpellCastFlow"/> right after
    /// the caster confirms the gift promise (see v1 cast-time vs.
    /// resolve-time delivery note on <see cref="IGiftClause"/>).
    /// </summary>
    /// <param name="recipient">The opponent who was promised the gift.
    /// Token / card creation should be attributed to this player.</param>
    /// <param name="spell">The gift-bearing spell. Implementations may
    /// inspect <see cref="Spell.Controller"/> or <see cref="Spell.Card"/>
    /// for context (e.g. to clone-stamp a token with the spell's
    /// controller as the token's "source" for trigger attribution).</param>
    void DeliverTo(Player recipient, Spell spell);
}
