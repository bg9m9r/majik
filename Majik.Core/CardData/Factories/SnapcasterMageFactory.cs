using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.StateMachine;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Snapcaster Mage (Innistrad, {1}{U}).
///
/// Creature — Human Wizard 2/1. Oracle text:
///   "Flash
///    When Snapcaster Mage enters, target instant or sorcery card in your
///    graveyard gains flashback until end of turn. The flashback cost is
///    equal to its mana cost."
///
/// ## Implemented (v1)
/// - 2/1 Human Wizard with Flash keyword (<see cref="KeywordAbility"/>).
/// - ETB triggered ability: declares a <see cref="TargetRequest"/> for an
///   instant or sorcery card in the controller's graveyard (cardinality
///   1..1 — the "target … card" wording is mandatory, not "up to one").
///   On resolution, stamps a runtime flashback grant on the chosen card
///   via <see cref="Card.GrantRuntimeFlashback"/>: the granted cost is
///   the card's own printed mana cost (per CR 702.33's "flashback cost is
///   equal to its mana cost" language).
/// - EOT cleanup: when an <see cref="IEventBus"/> is supplied, the ETB
///   effect subscribes a one-shot <see cref="StepStartedEvent"/> handler
///   tracking the Cleanup step; on the first Cleanup it sees, the
///   handler calls <see cref="Card.ClearRuntimeFlashback"/> on the
///   target card and unsubscribes itself. When no bus is wired (test /
///   shape-only path), the grant remains until the test clears it
///   manually.
///
/// ## How the grant is cast
/// To cast the flashback-granted card from the graveyard, callers build a
/// <see cref="Majik.Core.Costs.FlashbackAlternativeCost"/> from the card's
/// <see cref="Card.RuntimeFlashbackCost"/> and pass it to
/// <see cref="Majik.Core.Game.SpellCastFlow.CastAsync"/>. The existing
/// flashback alt-cost path handles zone-restriction (graveyard only),
/// alternative-cost-replaces-printed-cost semantics, and the
/// exile-on-resolution side effect (CR 702.33b). No new spell-cast plumbing
/// was introduced.
///
/// ## Bot-side discovery
/// <see cref="Majik.Core.Players.Agents.RuntimeFlashbackAltCostProbe"/>
/// reads <see cref="Card.RuntimeFlashbackCost"/> and yields a
/// <see cref="Majik.Core.Costs.FlashbackAlternativeCost"/> for the stamped
/// cost. A <see cref="Majik.Core.Players.Agents.HeuristicBotAgent"/>
/// constructed with that probe will bid the Snapcaster-granted flashback
/// alongside any printed alt costs.
///
/// ## Deferred (v1 gaps)
/// - <b>"Up to one" wording</b>: actual oracle is "target instant or sorcery
///   card" — mandatory single target. If no legal target exists, the
///   trigger is illegal-on-resolution per CR 603.10b and is removed from
///   the stack with no effect. The factory honours that — when no target
///   is supplied, the effect no-ops.
/// </summary>
[CardName("Snapcaster Mage")]
public static class SnapcasterMageFactory
{
    /// <summary>
    /// Construct Snapcaster Mage with no event bus wiring. The ETB grant
    /// will be set on resolution but the EOT-cleanup hook is inert (no
    /// bus subscription). Suitable for shape tests / dispatcher use.
    /// </summary>
    public static Creature Create(Player owner) => Create(owner, eventBus: null);

    /// <summary>
    /// Construct Snapcaster Mage with optional event bus. When the bus is
    /// supplied, the ETB effect subscribes to <see cref="StepStartedEvent"/>
    /// and clears the runtime flashback grant on the next Cleanup step
    /// (CR 514.2).
    /// </summary>
    public static Creature Create(Player owner, IEventBus? eventBus)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: "Snapcaster Mage",
            manaCost: "{1}{U}",
            power: 2,
            toughness: 1,
            subtypes: new[] { CardSubtype.Human, CardSubtype.Wizard });

        card.SetOwner(owner);
        card.SetController(owner);

        // CR 702.8 — Flash. Allows casting at instant speed.
        card.AddAbility(new KeywordAbility("Flash", card, owner));

        // CR 603.6a — ETB triggered ability. Fires on Snapcaster entering
        // the battlefield. Declares a 1..1 target request for an instant
        // or sorcery card in the controller's graveyard; on resolution,
        // grants flashback (CR 702.33) until end of turn with cost = the
        // chosen card's printed mana cost.
        TriggeredAbility? etb = null;
        var condition = new EventTriggerCondition<CardMovedEvent>(
            (e, _) => ReferenceEquals(e.Card, card) && e.ToZone == ZoneType.Battlefield);

        var etbEffect = new Effect(
            "Snapcaster Mage — target instant or sorcery in your graveyard gains flashback until end of turn (cost = its mana cost)",
            () =>
            {
                if (etb == null) return;
                var chosen = etb.ChosenTargets;
                if (chosen.Count == 0 || chosen[0].Count == 0) return;

                var raw = chosen[0][0];
                if (raw is not Card target) return;

                // CR 603.10b — illegal-on-resolution check. The target must
                // still be (a) in the controller's graveyard and (b) an
                // instant or sorcery card.
                if (target.Zone != ZoneType.Graveyard) return;
                if (!ReferenceEquals(target.Owner, owner)) return;
                if (!target.HasType(CardType.Instant) && !target.HasType(CardType.Sorcery)) return;

                // Stamp the grant. Cost = the target's printed mana cost.
                target.GrantRuntimeFlashback(target.ManaCostValue);

                // CR 514.2 — schedule cleanup. Subscribe to StepStartedEvent
                // and clear the grant when the first Cleanup step fires.
                // No bus → no auto-cleanup (factory was built shape-only;
                // callers manage EOT manually).
                if (eventBus == null) return;

                Action<StepStartedEvent>? handler = null;
                handler = (e) =>
                {
                    if (e.StepType != PhaseStateType.Cleanup) return;
                    target.ClearRuntimeFlashback();
                    if (handler != null) eventBus.Unsubscribe(handler);
                };
                eventBus.Subscribe(handler);
            });

        etb = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: condition,
            effects: new[] { etbEffect },
            interveningIf: null,
            activeZones: new[] { ZoneType.Battlefield },
            targetRequests: new[]
            {
                new TargetRequest(
                    Description: "target instant or sorcery card in your graveyard",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: System.Array.Empty<object>()),
            });

        card.AddAbility(etb);

        return card;
    }
}
