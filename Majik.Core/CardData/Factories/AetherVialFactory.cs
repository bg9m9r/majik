using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Costs;
using Majik.Core.Counters;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Aether Vial (Darksteel, {1}).
///
/// Artifact. Oracle text:
///   "At the beginning of your upkeep, you may put a charge counter on
///    Aether Vial."
///   "{T}: You may put a creature card with mana value equal to the
///    number of charge counters on Aether Vial from your hand onto the
///    battlefield."
///
/// ## Implemented (v1)
/// - Artifact {1} with owner/controller wired.
/// - <b>Upkeep triggered ability (CR 603.1 / CR 500.4)</b>: at the
///   beginning of the controller's upkeep, a
///   <see cref="CounterType.Charge"/> counter is added to Aether Vial.
///   v1 treats the printed "you may" as auto-accept — Aether Vial is
///   almost always vialed-up in practice, and the agent-prompt MVP has
///   not landed yet (same approach taken by Dark Confidant's reveal).
/// - <b>Tap activated ability (CR 602.1)</b>: pay {T}, then put a
///   creature card from the controller's hand with mana value equal to
///   the number of charge counters on Aether Vial onto the battlefield.
///   v1 deterministically picks the first matching creature card in
///   hand; the "you may" defaults to taking the action when an eligible
///   candidate exists. The hand → battlefield move is funnelled through
///   <see cref="ZoneService.MoveCard"/> when supplied so ETB triggers on
///   the placed creature fire (CR 603.6a). Raw zone manipulation is the
///   shape-only fallback.
///
/// ## Deferred (v1 gaps)
/// - <b>"You may" prompts</b>: both abilities auto-accept; declining
///   and target-selection are deferred to the agent-prompt MVP.
/// - <b>"Activate only any time you could cast an instant"</b>:
///   activated abilities are instant-speed by default per CR 602.1d,
///   so no extra gate is required — the absence of "as a sorcery" is
///   what the oracle text encodes.
/// </summary>
[CardName("Aether Vial")]
public static class AetherVialFactory
{
    /// <summary>
    /// Construct Aether Vial with no live runtime wiring. Both abilities
    /// are attached to the card shape; the upkeep trigger is not
    /// registered with a <see cref="TriggerManager"/>, and the tap
    /// activated ability uses raw zone manipulation for the hand →
    /// battlefield move. Suitable for shape / dispatcher tests.
    /// </summary>
    public static Artifact Create(Player owner) =>
        Create(owner, zoneService: null, eventBus: null, triggers: null);

    /// <summary>
    /// Construct Aether Vial with optional runtime services. When
    /// <paramref name="triggers"/> is supplied the upkeep trigger is
    /// registered so an Upkeep <see cref="StepStartedEvent"/> for the
    /// controller automatically surfaces it. When
    /// <paramref name="zoneService"/> is supplied the tap activated
    /// ability routes the hand → battlefield move through
    /// <see cref="ZoneService.MoveCard"/> so ETB triggers on the placed
    /// creature fire (CR 603.6a).
    /// </summary>
    public static Artifact Create(
        Player owner,
        ZoneService? zoneService,
        IEventBus? eventBus,
        TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Artifact("Aether Vial", "{1}");
        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // Upkeep trigger — CR 603.1, CR 500.4.
        //   "At the beginning of your upkeep, you may put a charge
        //    counter on Aether Vial."
        // Triggers.OnStepBegin filters StepStartedEvent on (Upkeep,
        // controller) so it only fires on the controller's own upkeeps.
        // The "you may" defaults to auto-accept at v1 (see class xmldoc).
        // ----------------------------------------------------------------
        var upkeepEffect = new Effect(
            "Aether Vial: add a charge counter",
            () =>
            {
                if (card.Zone != ZoneType.Battlefield) return;
                card.Counters.Add(CounterType.Charge);
            });

        var upkeepTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnStepBegin(owner, Majik.Core.StateMachine.PhaseStateType.Upkeep),
            effects: new IEffect[] { upkeepEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(upkeepTrigger);
        triggers?.RegisterTriggeredAbility(upkeepTrigger);

        // ----------------------------------------------------------------
        // Tap activated ability — CR 602.1.
        //   "{T}: You may put a creature card with mana value equal to
        //    the number of charge counters on Aether Vial from your hand
        //    onto the battlefield."
        // v1: deterministic — first matching creature card; "you may"
        // defaults to taking the action when an eligible candidate
        // exists. Routes the move through ZoneService when supplied so
        // ETB triggers fire on the placed creature (CR 603.6a).
        // No mana cost — the only cost is the tap (CR 602.1d implicit
        // "activate as instant").
        // ----------------------------------------------------------------
        var tapEffect = new Effect(
            "Aether Vial: put creature with mv = charge counters from hand to battlefield",
            () => PutCreatureFromHand(card, owner, zoneService));

        var tapAbility = new ActivatedAbility(
            source: card,
            controller: owner,
            costs: new ICost[] { AdditionalCost.Tap(card) },
            effects: new IEffect[] { tapEffect });

        card.AddAbility(tapAbility);

        return card;
    }

    /// <summary>
    /// Picks the first creature card in <paramref name="controller"/>'s
    /// hand whose mana value equals the number of charge counters on
    /// <paramref name="vial"/>, and moves it to the battlefield. Routes
    /// through <see cref="ZoneService.MoveCard"/> when supplied so ETB
    /// triggers fire (CR 603.6a); falls back to raw zone manipulation
    /// otherwise (shape-only path). No-ops when no matching creature is
    /// in hand (CR 117.x — "you may" with no valid target).
    /// </summary>
    private static void PutCreatureFromHand(
        Artifact vial,
        Player controller,
        ZoneService? zoneService)
    {
        var target = vial.Counters.Count(CounterType.Charge);

        var pick = controller.Zones.Hand.GetCards()
            .OfType<Creature>()
            .FirstOrDefault(c => c.ManaCostValue.TotalValue == target);

        if (pick == null) return;

        if (zoneService != null)
        {
            zoneService.MoveCard(pick, ZoneType.Hand, ZoneType.Battlefield, controller);
        }
        else
        {
            controller.Zones.Hand.RemoveCard(pick);
            controller.Zones.Battlefield.AddCard(pick);
            pick.SetZone(ZoneType.Battlefield);
            pick.SetController(controller);
        }
    }
}
