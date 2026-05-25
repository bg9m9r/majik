using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Reckless Bushwhacker (Oath of the Gatewatch,
/// {2}{R}).
///
/// Creature — Goblin Berserker 2/1. Oracle text:
///   "Surge {R} (You may cast this spell for its surge cost if you or a
///    teammate has cast another spell this turn.)
///    When this creature enters, if its surge cost was paid, creatures
///    you control get +1/+0 and gain haste until end of turn."
///
/// ## Implemented (v1)
/// - <b>Card shape</b>: 2/1 Creature — Goblin Berserker at printed cost
///   {2}{R}.
/// - <b>Surge alternative cost (CR 702.115)</b>: <see cref="BuildAlternativeCost"/>
///   returns a <see cref="SurgeAlternativeCost"/> constructed against the
///   live <see cref="TurnState"/> (caller supplies). The cast pipeline
///   replaces the printed mana cost with the surge cost {R} when this
///   alt-cost is supplied and the cast-time "you cast another spell this
///   turn" predicate (<see cref="SurgeAlternativeCost.IsLegalInContext"/>)
///   is satisfied. On resolution the alt-cost stamps
///   <see cref="Card.WasCastForSurge"/> on the card (and
///   <see cref="Majik.Core.Game.SpellCastFlow"/> mirrors the stamp at
///   announce time so ETB resolve bodies see the flag).
/// - <b>ETB triggered ability (CR 603.6a)</b>: fires on Stack →
///   Battlefield. Intervening-if (CR 603.4) reads
///   <see cref="Card.WasCastForSurge"/>: if the surge cost was paid, the
///   resolve body applies +1/+0 pump + Haste grant to every creature the
///   controller controls (snapshot at resolution time per CR 608.2),
///   reusing the <see cref="ViolentOutburstFactory.ApplyPumpAndHaste"/>
///   helper (Layers 6/7c per CR 613.1c, EOT cleanup per CR 514.2). If
///   the surge cost was NOT paid, the trigger silently no-ops at
///   intervening-if check time (CR 603.4 — trigger goes on the stack but
///   does nothing on resolution; v1 short-circuits the check at resolve
///   to avoid stack churn since the printed wording is structurally an
///   intervening-if).
///
/// ## Notes
/// - Reckless Bushwhacker is the canonical Surge consumer alongside
///   Reckless Wurm and Goblin Dark-Dwellers + Bushwhacker swarm decks
///   (Modern). The Surge alt-cost primitive (<see cref="SurgeAlternativeCost"/>)
///   is general; this factory only wires the "if its surge cost was paid"
///   intervening-if branch.
/// - <b>Reach to the bot probe layer</b> (Surge alt-cost discovery for
///   the bot's bidding heuristic) is deferred — same shape as
///   <see cref="Players.Agents.KickerAltCostProbe"/> / EvokeAltCostProbe;
///   added when the bot needs Surge-aware play.
/// </summary>
[CardName("Reckless Bushwhacker")]
public static class RecklessBushwhackerFactory
{
    public const string CardName = "Reckless Bushwhacker";
    public const string PrintedManaCost = "{2}{R}";
    /// <summary>CR 702.115 — printed Surge mana cost: {R}.</summary>
    public const string SurgeManaCost = "{R}";

    public const int BasePower = 2;
    public const int BaseToughness = 1;

    /// <summary>+P pump magnitude. Reckless Bushwhacker prints +1/+0.</summary>
    public const int PumpPower = 1;
    /// <summary>+T pump magnitude. Reckless Bushwhacker prints +1/+0.</summary>
    public const int PumpToughness = 0;
    /// <summary>Granted keyword — CR 702.10 Haste.</summary>
    public const string GrantedKeyword = "Haste";

    /// <summary>
    /// CR 702.115 — build the Surge alt-cost ({R}, gated on
    /// <paramref name="turnState"/>.<see cref="TurnState.SpellsCastByPlayer"/>
    /// for the caster). The cast pipeline picks this up via direct
    /// <c>SpellCaster</c> calls; the cast-flow announce path checks
    /// <see cref="SurgeAlternativeCost.IsLegalInContext"/> against the
    /// caster before applying the alt-cost.
    /// </summary>
    public static SurgeAlternativeCost BuildAlternativeCost(TurnState turnState) =>
        new(ManaCost.Parse(SurgeManaCost), turnState);

    /// <summary>
    /// Single-arg dispatcher path. Attaches the ETB trigger structurally
    /// so card shape is correct; no <see cref="TriggerManager"/> wiring.
    /// </summary>
    public static Creature Create(Player owner) => Create(owner, triggers: null);

    /// <summary>
    /// Fully-wired construction. <paramref name="triggers"/> registers
    /// the ETB triggered ability so a <see cref="CardMovedEvent"/>
    /// (Stack → Battlefield) on this card fires the surge-conditional
    /// pump+haste rider automatically.
    /// </summary>
    public static Creature Create(Player owner, TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: BasePower,
            toughness: BaseToughness,
            supertypes: null,
            subtypes: new[] { CardSubtype.Goblin, CardSubtype.Berserker });

        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // ETB triggered ability — CR 603.6a + CR 603.4 (intervening-if).
        //   "When this creature enters, if its surge cost was paid, creatures
        //    you control get +1/+0 and gain haste until end of turn."
        // Intervening-if (CR 603.4) reads Card.WasCastForSurge — set by
        // SurgeAlternativeCost.OnResolved (and mirrored at announce time
        // by SpellCastFlow so the flag is live on the card by the time
        // the ETB trigger evaluates).
        // ----------------------------------------------------------------
        var condition = new EventTriggerCondition<CardMovedEvent>(
            (e, _) => ReferenceEquals(e.Card, card) && e.ToZone == ZoneType.Battlefield);

        var effect = new Effect(
            $"{CardName}: if surge cost was paid, creatures you control get +{PumpPower}/+{PumpToughness} and gain {GrantedKeyword} until end of turn",
            () =>
            {
                // CR 603.4 — intervening-if at resolution. Short-circuit
                // when the surge cost wasn't paid (v1 collapses the
                // "trigger goes on stack but does nothing" path into a
                // resolve-time no-op for stack-churn reduction; matches
                // the engine convention used by other "if [cost] was
                // paid" intervening-ifs).
                if (!card.WasCastForSurge) return;

                // CR 608.2 — snapshot the caster's battlefield creatures
                // at resolution time and apply the pump+haste rider.
                // Reuses Violent Outburst's helper since the printed
                // body is structurally identical (creatures you control
                // get +1/+0 and gain haste until end of turn).
                var controller = card.Controller ?? owner;
                ViolentOutburstFactory.ApplyPumpAndHaste(controller);
            });

        var etbTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: condition,
            effects: new[] { effect },
            interveningIf: null,
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(etbTrigger);
        triggers?.RegisterTriggeredAbility(etbTrigger);

        return card;
    }
}
