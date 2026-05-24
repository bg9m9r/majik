using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Orcish Bowmasters (The Lord of the Rings).
///
/// Creature — Orc Archer {1}{B} 1/1. Oracle text:
///   "Flash
///    When this creature enters and whenever an opponent draws a card except
///    the first one they draw in each of their draw steps, this creature deals
///    1 damage to any target. Then amass Orcs 1."
///
/// ## Implemented (v1)
/// - Flash keyword (via <see cref="KeywordAbility"/>).
/// - Correct name, type (Creature), subtypes (Orc Archer), mana cost ({1}{B}),
///   power/toughness (1/1), owner/controller.
///
/// ## Deferred (v1 gaps)
/// - <b>ETB damage trigger</b>: "when this creature enters … deals 1 damage to
///   any target" requires a targeting prompt for the damage source. Deferred
///   until the agent prompt/target system supports any-target selection.
/// - <b>Opponent-draw watcher</b>: "whenever an opponent draws a card except
///   the first one they draw in each of their draw steps" requires tracking
///   draw ordinal per-player per-draw-step across all opponents. Deferred until
///   multi-player draw-event subscription is wired.
/// - <b>Amass Orcs 1</b>: the Amass mechanic (LTR variant — "amass Orcs N")
///   requires putting N +1/+1 counters on an Orc Army token (creating one if
///   needed). No Army-token type, Amass infrastructure, or token-upsizing logic
///   exists yet. Deferred until the Amass subsystem is implemented.
/// </summary>
[CardName("Orcish Bowmasters")]
public static class OrcishBowmastersFactory
{
    /// <summary>
    /// Construct Orcish Bowmasters owned and controlled by <paramref name="owner"/>.
    /// </summary>
    public static Creature Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var ob = new Creature(
            "Orcish Bowmasters",
            manaCost: "{1}{B}",
            power: 1, toughness: 1,
            subtypes: new[] { CardSubtype.Orc, CardSubtype.Archer });
        ob.SetOwner(owner);
        ob.SetController(owner);

        // ----------------------------------------------------------------
        // Flash — CR 702.8. Allows casting at instant speed.
        // TimingRules.CanCastAtInstantSpeed checks for this keyword.
        // ----------------------------------------------------------------
        ob.AddAbility(new KeywordAbility("Flash", ob, owner));

        // Deferred:
        //  - ETB "deals 1 damage to any target" trigger (needs targeting prompt)
        //  - "Whenever an opponent draws a card" watcher trigger (needs
        //    per-player draw-step ordinal tracking across opponents)
        //  - Amass Orcs 1 (needs Army-token infrastructure)

        return ob;
    }
}
