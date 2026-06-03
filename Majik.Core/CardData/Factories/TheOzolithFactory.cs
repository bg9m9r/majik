using System;
using System.Collections.Generic;
using System.Linq;
using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Counters;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.StateMachine;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for The Ozolith (Ikoria: Lair of Behemoths, {1}).
///
/// Legendary Artifact. Oracle text (Scryfall, verified 2026-06-02):
///   "Whenever a creature you control leaves the battlefield, if it had
///    counters on it, put those counters on The Ozolith.
///    At the beginning of combat on your turn, if The Ozolith has counters
///    on it, you may move all counters from The Ozolith onto target
///    creature."
///
/// ## Shape source
///
/// Card identity (name, {1}, Legendary Artifact) is loaded from
/// <c>Majik.Core/CardData/Cards/the-ozolith.json</c> via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> and built through
/// <see cref="CardDefinitionFactory"/>. The two triggered abilities are wired
/// in code below.
///
/// ## Implemented (v1)
///
/// - <b>Leaves-the-battlefield capture trigger</b> (CR 603.6d — leaves-the-
///   battlefield abilities trigger based on the game state immediately before
///   the event, i.e. last-known information). Wired as a
///   <see cref="TriggeredAbility"/> over <see cref="CardMovedEvent"/> with
///   FromZone = Battlefield, matching a creature controlled by The Ozolith's
///   controller. The intervening-if ("if it had counters on it") is checked
///   at fire time AND re-checked on resolution by reading the leaving
///   permanent's <see cref="Permanent.Counters"/> (which the zone-move path
///   leaves intact on the moved object — same last-known-information posture
///   that Falkenrath Noble / Blood Artist rely on for the dying permanent).
///   On resolution every counter kind on the leaving permanent is copied onto
///   The Ozolith via <see cref="CountersService.Add"/> (CR 122 — "put those
///   counters", preserving kind and quantity), then cleared from the
///   now-departed permanent's bag so it is not double-counted.
/// - <b>Beginning-of-combat move trigger</b> (CR 508.1 — "At the beginning of
///   combat on your turn"). Wired as a targeted
///   <see cref="TriggeredAbility"/> over
///   <see cref="Triggers.OnStepBegin"/> for
///   <see cref="StepStateType.BeginningOfCombat"/> restricted to the
///   controller's own turns, with a 1..1 "target creature"
///   <see cref="TargetRequest"/>. The intervening-if ("if The Ozolith has
///   counters on it") gates the trigger at fire time AND on resolution. On
///   resolution every counter is moved off The Ozolith onto the chosen target
///   creature (CR 122 — move = remove-then-place, preserving kind and
///   quantity), routed through <see cref="CountersService.Add"/> so Hardened
///   Scales / Winding Constrictor / Doubling Season replacements on the
///   destination still apply per CR 614 (the counters are being PUT on the
///   target). Target legality is re-checked on resolution (CR 608.2b — the
///   chosen target must still be a creature on the battlefield).
///
/// ## Notes / "you may"
///
/// The beginning-of-combat clause is a may-clause ("you may move all
/// counters"). v1 auto-accepts the may when a legal target has been chosen
/// (same posture as Cori-Steel Cutter / Eternal Witness / Snapcaster Mage):
/// supplying no chosen target is the natural "decline" — the move body
/// no-ops and the counters stay on The Ozolith.
///
/// ## Lifecycle
///
/// The single-arg <see cref="Create(Player)"/> overload omits all service
/// wiring and produces the correct card shape only (both triggers attached to
/// the card for inspection but registered with no <see cref="TriggerManager"/>
/// and driven by no event bus). Use the
/// (owner, replacements, eventBus, triggers) overload for fully-wired
/// behaviour.
/// </summary>
[CardName("The Ozolith")]
public static class TheOzolithFactory
{
    public const string CardName = "The Ozolith";

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("the-ozolith");

    /// <summary>
    /// Constructs The Ozolith with card identity only — both triggered
    /// abilities are attached to the card shape but registered with no
    /// <see cref="TriggerManager"/> (so they never fire). Suitable for
    /// shape / dispatcher tests.
    /// </summary>
    public static Artifact Create(Player owner)
        => Create(owner, replacements: null, eventBus: null, triggers: null);

    /// <summary>
    /// Constructs The Ozolith with optional runtime services. When
    /// <paramref name="triggers"/> is supplied both triggered abilities are
    /// registered so the bus drives them automatically. When
    /// <paramref name="replacements"/> is supplied the begin-of-combat move
    /// routes counter placement on the destination creature through the bus
    /// (Hardened Scales / Winding Constrictor / Doubling Season apply,
    /// CR 614). When <paramref name="eventBus"/> is supplied the post-commit
    /// <see cref="CounterAddedEvent"/> is published for downstream
    /// "whenever one or more counters are put on …" triggers (CR 603.6).
    /// </summary>
    public static Artifact Create(
        Player owner,
        ReplacementBus? replacements,
        IEventBus? eventBus,
        TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = (Artifact)CardDefinitionFactory.Build(Definition, owner);
        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // Trigger 1 — "Whenever a creature you control leaves the
        // battlefield, if it had counters on it, put those counters on The
        // Ozolith." (CR 603.6d — leaves-the-battlefield ability; reads the
        // game state immediately before the creature left, i.e. LKI.)
        //
        // CardMovedEvent fires after the move but the leaving object still
        // carries its counter bag (the zone-move path does not clear it),
        // same last-known-information access that Falkenrath Noble / Blood
        // Artist use for the dying permanent.
        // ----------------------------------------------------------------
        // The leaving permanent must be captured at trigger time (the
        // CardMovedEvent carries it) because the effect closure runs at
        // resolution, after the event has passed. Stash the matched permanent
        // in a local the condition writes and the effect reads — one per card
        // instance, written-then-read within a single fire/resolve pass.
        Permanent? leavingPermanent = null;

        var captureCondition = new EventTriggerCondition<CardMovedEvent>((e, _) =>
        {
            if (e.FromZone != ZoneType.Battlefield) return false;
            if (e.Card is not Permanent leaving) return false;
            if (!leaving.HasType(CardType.Creature)) return false;
            // "a creature YOU control" — controller match (LKI controller).
            if (!ReferenceEquals(leaving.Controller, card.Controller ?? owner)) return false;
            // Intervening-if (CR 603.4) — "if it had counters on it".
            if (!leaving.Counters.HasAny) return false;

            leavingPermanent = leaving;
            return true;
        });

        var captureEffect = new Effect(
            $"{CardName}: put the leaving creature's counters on The Ozolith",
            () =>
            {
                if (leavingPermanent == null) return;
                MoveAllCounters(
                    from: leavingPermanent,
                    to: card,
                    replacements: replacements,
                    eventBus: eventBus);
                leavingPermanent = null;
            });

        var captureTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: captureCondition,
            effects: new IEffect[] { captureEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(captureTrigger);
        triggers?.RegisterTriggeredAbility(captureTrigger);

        // ----------------------------------------------------------------
        // Trigger 2 — "At the beginning of combat on your turn, if The
        // Ozolith has counters on it, you may move all counters from The
        // Ozolith onto target creature." (CR 508.1 begin-combat trigger +
        // CR 603.4 intervening-if.)
        // ----------------------------------------------------------------
        var beginCombatCondition = new EventTriggerCondition<StepStartedEvent>((e, _) =>
            // CR 508.1 — begin-of-combat on the controller's own turn.
            e.StepType == StepStateType.BeginningOfCombat
            && ReferenceEquals(e.Player, card.Controller ?? owner)
            // CR 603.4 — intervening-if checked as the trigger would fire.
            && card.Counters.HasAny);

        TriggeredAbility? moveTrigger = null;
        var moveEffect = new Effect(
            $"{CardName}: move all counters from The Ozolith onto target creature",
            () =>
            {
                if (moveTrigger == null) return;
                // CR 603.4 — intervening-if re-checked on resolution.
                if (!card.Counters.HasAny) return;
                // "you may" — declining = no chosen target (auto-accept when
                // a legal target is present, same posture as Cori-Steel
                // Cutter / Eternal Witness).
                if (moveTrigger.ChosenTargets.Count == 0) return;
                if (moveTrigger.ChosenTargets[0].Count == 0) return;
                if (moveTrigger.ChosenTargets[0][0] is not Permanent target) return;

                // CR 608.2b — resolve-time legality recheck: still a creature
                // on the battlefield.
                if (target.Zone != ZoneType.Battlefield) return;
                if (!target.HasType(CardType.Creature)) return;

                MoveAllCounters(
                    from: card,
                    to: target,
                    replacements: replacements,
                    eventBus: eventBus);
            });

        moveTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: beginCombatCondition,
            effects: new IEffect[] { moveEffect },
            activeZones: new[] { ZoneType.Battlefield },
            targetRequests: new[]
            {
                new TargetRequest(
                    Description: "target creature",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.Buff),
            });

        card.AddAbility(moveTrigger);
        triggers?.RegisterTriggeredAbility(moveTrigger);

        return card;
    }

    /// <summary>
    /// CR 122 — move EVERY counter (all kinds, full quantity) from
    /// <paramref name="from"/> onto <paramref name="to"/>. Removal from the
    /// source is unconditional; placement on the destination routes through
    /// <see cref="CountersService.Add"/> so destination replacement effects
    /// (Hardened Scales / Winding Constrictor / Doubling Season — CR 614)
    /// and the post-commit <see cref="CounterAddedEvent"/> (CR 603.6) apply.
    /// </summary>
    private static void MoveAllCounters(
        Permanent from,
        Permanent to,
        ReplacementBus? replacements,
        IEventBus? eventBus)
    {
        // Snapshot before mutating (the dictionary is cleared below).
        var snapshot = from.Counters.All
            .Where(kv => kv.Value > 0)
            .Select(kv => (kv.Key, kv.Value))
            .ToList();
        if (snapshot.Count == 0) return;

        from.Counters.Clear();

        foreach (var (type, amount) in snapshot)
        {
            CountersService.Add(to, type, amount, replacements, eventBus);
        }
    }
}
