using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Counters;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Mazemind Tome (Core Set 2021, {2}).
///
/// Artifact — {2}. Oracle text (verified against Scryfall):
///   "{T}, Put a page counter on this artifact: Scry 1."
///   "{2}, {T}, Put a page counter on this artifact: Draw a card."
///   "When there are four or more page counters on this artifact, exile it.
///    If you do, you gain 4 life."
///
/// ## Mechanic paid down — state-triggered counter-threshold (CR 603.8)
/// The third ability is a <b>state trigger</b> (CR 603.8): it fires whenever
/// its condition (≥4 page counters) holds, not in response to any event. The
/// engine already exposes the seam for this —
/// <see cref="StateChangeTriggerCondition"/> (rising-edge predicate) evaluated
/// by <see cref="TriggerManager.EvaluateStateChangeTriggers"/> after each
/// state-based-action pass (the CR 704 checkpoint where 603.8 triggers are
/// looked for, CR 603.3). This factory expresses the firing condition
/// declaratively via the new
/// <see cref="StateWhenCountersGeTriggerDef"/> (<c>"state_when_counters_ge"</c>,
/// <c>Counter = "Page"</c>, <c>Threshold = 4</c>), routed through
/// <see cref="TriggerDefinition.ToTrigger"/> /
/// <see cref="CardDefRuntime.BuildJsonTrigger"/> so the produced condition is
/// the same rising-edge predicate a hand-rolled
/// <see cref="StateChangeTriggerCondition"/> would build. The
/// threshold-reached payoff (exile + gain 4 life) is the trigger's effect.
///
/// ## Implemented (v1)
/// - Artifact identity, mana cost {2}, owner/controller wired.
/// - <b>{T}, Put a page counter: Scry 1.</b> — modelled as an
///   <see cref="ActivatedAbility"/> whose effect adds one
///   <see cref="CounterType.Page"/> counter. The "Put a page counter on this
///   artifact" clause is the load-bearing part for the threshold trigger; the
///   Scry 1 look itself needs an interactive agent decision
///   (<see cref="Majik.Core.Keywords.ScryAction"/>) that the synchronous
///   single-arg factory cannot reach, so it is a structural no-op here — the
///   same posture as The One Ring's deferred protection grant. The page
///   counter (which the payoff keys off) is fully real.
/// - <b>{2}, {T}, Put a page counter: Draw a card.</b> — an
///   <see cref="ActivatedAbility"/> with a {2} mana cost + the {T} tap; its
///   effect adds one page counter, then draws one card (empty library flags
///   <see cref="Player.MarkTriedToDrawFromEmptyLibrary"/> per CR 704.5b).
/// - <b>State trigger</b> (CR 603.8): "When there are four or more page
///   counters on this artifact, exile it. If you do, you gain 4 life."
///   Wired as the declarative <see cref="StateWhenCountersGeTriggerDef"/> →
///   <see cref="StateChangeTriggerCondition"/>; on resolution the effect exiles
///   the artifact (battlefield → exile, CR 603.8 "exile it") and — only if the
///   exile actually happened ("If you do", CR 608.2c intervening clause) —
///   the controller gains 4 life.
///
/// ## Deferred (v1 gap)
/// - <b>Scry 1 look</b>: the agent-driven top-card peek/reorder is a no-op in
///   the synchronous factory (no agent surface). The page-counter accrual that
///   drives the threshold payoff is exact.
/// </summary>
[CardName("Mazemind Tome")]
public static class MazemindTomeFactory
{
    public const string CardName = "Mazemind Tome";
    public const string PrintedManaCost = "{2}";

    private static readonly StateWhenCountersGeTriggerDef ThresholdTriggerDef = new()
    {
        Counter = CounterType.Page.Name,
        Threshold = 4,
    };

    /// <summary>
    /// Construct Mazemind Tome with no live bus / trigger-manager wiring.
    /// The state trigger is attached for shape inspection; tests drive it via
    /// a <see cref="TriggerManager"/> overload or by invoking the effect.
    /// </summary>
    public static Artifact Create(Player owner) =>
        Create(owner, triggers: null);

    /// <summary>
    /// Construct Mazemind Tome, optionally registering the ≥4-page-counter
    /// state trigger with <paramref name="triggers"/> so
    /// <see cref="TriggerManager.EvaluateStateChangeTriggers"/> surfaces it
    /// automatically after each SBA pass.
    /// </summary>
    public static Artifact Create(Player owner, TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var tome = new Artifact(name: CardName, manaCost: PrintedManaCost);
        tome.SetOwner(owner);
        tome.SetController(owner);

        // ----------------------------------------------------------------
        // {T}, Put a page counter on this artifact: Scry 1. (CR 602.1)
        // The page counter is the load-bearing payoff input; the Scry 1
        // look needs an interactive agent decision (deferred — see xmldoc).
        // ----------------------------------------------------------------
        var scryEffect = new Effect(
            "Mazemind Tome: put a page counter (Scry 1)",
            () => tome.Counters.Add(CounterType.Page));

        var scryAbility = new ActivatedAbility(
            source: tome,
            controller: owner,
            costs: new ICost[] { AdditionalCost.Tap(tome) },
            effects: new IEffect[] { scryEffect });

        tome.AddAbility(scryAbility);

        // ----------------------------------------------------------------
        // {2}, {T}, Put a page counter on this artifact: Draw a card. (CR 602.1)
        // ----------------------------------------------------------------
        var drawEffect = new Effect(
            "Mazemind Tome: put a page counter, draw a card",
            () =>
            {
                tome.Counters.Add(CounterType.Page);

                var controller = tome.Controller ?? owner;
                var top = controller.Zones.Library.GetCards().FirstOrDefault();
                if (top == null)
                {
                    controller.MarkTriedToDrawFromEmptyLibrary();
                    return;
                }
                controller.Zones.Library.RemoveCard(top);
                controller.Zones.Hand.AddCard(top);
                top.SetZone(ZoneType.Hand);
            });

        var drawAbility = new ActivatedAbility(
            source: tome,
            controller: owner,
            costs: new ICost[]
            {
                new ManaCostCost("{2}"),
                AdditionalCost.Tap(tome),
            },
            effects: new IEffect[] { drawEffect });

        tome.AddAbility(drawAbility);

        // ----------------------------------------------------------------
        // State trigger — CR 603.8.
        //   "When there are four or more page counters on this artifact,
        //    exile it. If you do, you gain 4 life."
        // Declarative state_when_counters_ge condition (rising-edge over
        // "≥4 Page counters"), evaluated after each SBA pass.
        // ----------------------------------------------------------------
        var thresholdEffect = new Effect(
            "Mazemind Tome: exile it; if you do, gain 4 life",
            () =>
            {
                var controller = tome.Controller ?? owner;

                // CR 603.8 "exile it". Move battlefield → exile directly
                // (same direct-zone posture as Nihil Spellbomb's exile).
                var exiled = false;
                if (tome.Zone == ZoneType.Battlefield)
                {
                    controller.Zones.Battlefield.RemoveCard(tome);
                    owner.Zones.Exile.AddCard(tome);
                    tome.SetZone(ZoneType.Exile);
                    exiled = true;
                }

                // CR 608.2c "If you do" — gain 4 life only if the exile
                // actually happened (it could have left the battlefield first).
                if (exiled)
                {
                    controller.GainLife(4);
                }
            });

        var thresholdTrigger = new TriggeredAbility(
            source: tome,
            controller: owner,
            condition: ThresholdTriggerDef.ToTrigger()(tome),
            effects: new IEffect[] { thresholdEffect },
            activeZones: new[] { ZoneType.Battlefield });

        tome.AddAbility(thresholdTrigger);
        triggers?.RegisterTriggeredAbility(thresholdTrigger);

        return tome;
    }
}
