using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Goblin Bushwhacker (Worldwake / Zendikar block,
/// {R}).
///
/// Creature — Goblin Warrior 1/1. Oracle text:
///   "Kicker {R} (You may pay an additional {R} as you cast this spell.)
///    When Goblin Bushwhacker enters, if it was kicked, creatures you
///    control get +1/+0 and gain haste until end of turn."
///
/// ## Implemented (v1)
/// - 1/1 Creature — Goblin Warrior at printed cost {R}, owner / controller
///   wired. Subtypes Goblin + Warrior so Goblin Chieftain / Warchief /
///   Krenko / Munitions Expert see Bushwhacker correctly under their
///   "Goblins you control" gates.
/// - <b>Kicker (CR 702.33)</b>: shipped as a real
///   <see cref="KickerAdditionalCost"/> via <see cref="BuildAdditionalCost"/>
///   (same primitive Burst Lightning uses). Caller layers the cost onto
///   <see cref="Majik.Core.Game.SpellCastFlow.CastAsync"/>'s
///   <c>additionalCosts</c> parameter; on payment the cost stamps
///   <see cref="Card.WasKicked"/> = true so the ETB intervening-if branch
///   sees the kicked posture (CR 702.33b — "if [spell] was kicked").
/// - <b>Bot-side discovery</b>: registered in
///   <see cref="Players.Agents.KickerAltCostProbe.DefaultLookup"/> so the
///   bot's kicker-cost probe recognises Bushwhacker as a {R}-kicker card
///   without per-card wiring at the bot layer.
/// - <b>ETB intervening-if triggered ability (CR 603.6a / CR 603.4)</b>:
///   "When Goblin Bushwhacker enters, <b>if it was kicked</b>, creatures
///   you control get +1/+0 and gain haste until end of turn." Wired over
///   <see cref="CardMovedEvent"/> with
///   <see cref="TriggeredAbility.InterveningIf"/> = <c>card.WasKicked</c>:
///   if the spell was NOT kicked, the trigger doesn't go on the stack
///   (CR 603.4 — the intervening-if is checked when the trigger would
///   trigger AND when it would resolve; a false read at either point
///   removes it). On resolution the effect snapshots the controller's
///   battlefield creatures and registers two riders on each:
///     <ul>
///       <li><see cref="PumpUntilEndOfTurnEffect"/>(+1, 0) — CR 613.1c
///           Layer 7c +P/+T, EOT cleanup via CR 514.2.</li>
///       <li><see cref="GrantKeywordUntilEndOfTurnEffect"/>("Haste") —
///           CR 613.1c Layer 6 keyword grant, EOT cleanup.</li>
///     </ul>
///   Same effect shape as <see cref="ViolentOutburstFactory.ApplyPumpAndHaste"/>;
///   the pump/haste body is identical, only the trigger source differs
///   (ETB-from-kicked vs cascade-resolve-on-cast).
///
/// ## Self-pumps and Bushwhacker himself
/// "Creatures you control" includes Bushwhacker — he's on the battlefield
/// by the time the ETB trigger resolves (CR 603.6a — ETB triggers see the
/// new permanent already on the battlefield). So a solo kicked
/// Bushwhacker hits as a 2/1 with haste, the canonical "Bushwhacker
/// alpha-strike off mana surge" play. With more creatures out, every
/// untapped creature on the controller's side picks up +1/+0 + haste
/// until end of turn. Tokens minted later in the same turn do NOT pick
/// up the pump/haste (the resolution snapshot is one-shot per CR 608.2 —
/// same caveat as Violent Outburst).
///
/// ## Kicker / WasKicked lifecycle caveat (v1)
/// <see cref="Majik.Core.Game.SpellCastFlow"/> appends a cleanup effect
/// that clears <see cref="Card.WasKicked"/> after the spell's printed
/// body runs. For a Creature spell the "printed body" is empty (the
/// card just moves to battlefield via the post-resolution zone hook),
/// so the cleanup runs <em>before</em> the ETB trigger resolves —
/// meaning the ETB's read of <c>card.WasKicked</c> would see <c>false</c>
/// in a full SpellCastFlow round-trip. The intervening-if check
/// (CR 603.4) is evaluated at trigger-announce, which fires off
/// <see cref="CardMovedEvent"/> immediately after the battlefield move
/// (which itself happens after the cleanup) — same window.
///
/// <para>Bushwhacker tests exercise the kicked branch by pre-stamping
/// <see cref="Card.SetWasKicked"/> on a hand-built card and either firing
/// the trigger directly OR driving the card through
/// <see cref="Services.ZoneService.MoveCard"/> so
/// <see cref="CardMovedEvent"/> publishes with the flag still set. The
/// production fix is to mirror the kicker stamp onto a battlefield-exit-
/// scoped sentinel (same shape as <see cref="Card.WasCastFromHand"/> which
/// is cleared by <see cref="Services.ZoneService"/> on Battlefield → any
/// transition, not during spell cleanup). Until that mirror lands, the
/// kicker cleanup timing is a known v1 gap for creature-with-kicker
/// (Bushwhacker is the first such factory; Burst Lightning is unaffected
/// because instant resolution runs the printed body inline before the
/// cleanup).</para>
///
/// ## Deferred (v1 gaps)
/// - <b>Kicker cleanup timing for creature ETBs</b> — see above.
/// - <b>Tokens entering after resolution</b>: the pump/haste snapshot is
///   one-shot per CR 608.2; tokens or creatures that enter the battlefield
///   after this trigger resolves do NOT pick up the rider. Matches
///   Violent Outburst's posture.
/// </summary>
[CardName("Goblin Bushwhacker")]
public static class GoblinBushwhackerFactory
{
    public const string CardName = "Goblin Bushwhacker";
    public const string PrintedManaCost = "{R}";
    public const string KickerCostText = "{R}";
    public const int Power = 1;
    public const int Toughness = 1;

    /// <summary>+P pump magnitude on the kicked-ETB rider — +1/+0.</summary>
    public const int PumpPower = 1;

    /// <summary>+T pump magnitude on the kicked-ETB rider — +1/+0.</summary>
    public const int PumpToughness = 0;

    /// <summary>Granted keyword on the kicked-ETB rider — CR 702.10 Haste.</summary>
    public const string GrantedKeyword = "Haste";

    /// <summary>
    /// Construct Goblin Bushwhacker. The ETB intervening-if trigger is
    /// attached to the card shape; call
    /// <see cref="Majik.Core.Services.TriggerManager.BindCard"/> on the
    /// returned creature to register it with the live trigger manager so
    /// it fires off the bus.
    /// </summary>
    public static Creature Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            subtypes: new[] { CardSubtype.Goblin, CardSubtype.Warrior });

        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // ETB intervening-if trigger — CR 603.6a + CR 603.4.
        //   "When Goblin Bushwhacker enters, if it was kicked, creatures
        //    you control get +1/+0 and gain haste until end of turn."
        //
        // CR 603.4: intervening-if is checked when the trigger would
        // trigger AND when it would resolve; a false read at either
        // point removes the trigger from the stack without effect. The
        // gate reads Card.WasKicked off the card directly — set during
        // KickerAdditionalCost.Pay at cast announcement.
        //
        // CR 608.2 — resolution snapshot. Effect builds the list of
        // creatures the controller controls AT THE MOMENT of resolution
        // (Bushwhacker is on the battlefield by then; he picks up the
        // rider himself).
        // ----------------------------------------------------------------
        var etbCondition = new EventTriggerCondition<CardMovedEvent>(
            (e, _) => ReferenceEquals(e.Card, card) && e.ToZone == ZoneType.Battlefield);

        var etbEffect = new Effect(
            $"{CardName}: creatures you control get +{PumpPower}/+{PumpToughness} and gain {GrantedKeyword} until end of turn",
            () =>
            {
                // CR 603.4 — second-pass intervening-if. Defensive
                // re-check at resolution mirrors Field of the Dead's
                // resolution-time re-check pattern.
                if (card is not Card concrete || !concrete.WasKicked) return;

                var controller = card.Controller ?? owner;
                ApplyPumpAndHaste(controller);
            });

        var etbTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: etbCondition,
            effects: new IEffect[] { etbEffect },
            // CR 603.4 — queue-time intervening-if. False at announce =
            // the trigger doesn't go on the stack at all.
            interveningIf: () => card is Card c && c.WasKicked,
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(etbTrigger);

        return card;
    }

    /// <summary>
    /// CR 702.33 — construct Goblin Bushwhacker's kicker rider for the
    /// supplied <paramref name="card"/> instance. Layer the returned cost
    /// onto the cast via <see cref="Majik.Core.Game.SpellCastFlow.CastAsync"/>'s
    /// <c>additionalCosts</c> parameter to pay the {R} kicker (same wiring
    /// shape as Burst Lightning).
    /// </summary>
    public static IAdditionalCost BuildAdditionalCost(ICard card)
    {
        ArgumentNullException.ThrowIfNull(card);
        return new KickerAdditionalCost(card, ManaCost.Parse(KickerCostText));
    }

    /// <summary>
    /// Apply Goblin Bushwhacker's "+1/+0 and Haste until end of turn"
    /// rider to every creature <paramref name="controller"/> controls at
    /// the moment this effect runs. CR 608.2 — snapshot at resolution
    /// time. CR 613.1c — Layer 6 (keyword grant) + Layer 7c (+P/+T).
    /// CR 514.2 — EOT cleanup via <see cref="PumpUntilEndOfTurnEffect"/>'s
    /// + <see cref="GrantKeywordUntilEndOfTurnEffect"/>'s expiry flags.
    /// </summary>
    public static void ApplyPumpAndHaste(Player controller)
    {
        ArgumentNullException.ThrowIfNull(controller);

        // Snapshot to a list before applying so any same-step zone-move
        // side effects don't disturb enumeration. Same posture as
        // Violent Outburst / Pyroclasm.
        var creatures = controller.Zones.Battlefield.GetCards()
            .OfType<Creature>()
            .ToList();

        foreach (var creature in creatures)
        {
            // Shape-only safety — without a live ContinuousEffectsService
            // wired onto the creature, the pump/haste body silently
            // no-ops rather than NRE'ing. Mirrors Violent Outburst's
            // defensive guard.
            if (creature.ActiveEffects == null) continue;

            // CR 613.1c Layer 7c — +1/+0 pump.
            creature.ActiveEffects.Register(
                new PumpUntilEndOfTurnEffect(creature, PumpPower, PumpToughness));

            // CR 613.1c Layer 6 — keyword grant: Haste (CR 702.10).
            creature.ActiveEffects.Register(
                new GrantKeywordUntilEndOfTurnEffect(creature, GrantedKeyword));
        }
    }
}
