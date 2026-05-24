using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.StateMachine;
using Majik.Core.Tokens;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Ragavan, Nimble Pilferer (Modern Horizons 2, {R}).
///
/// Legendary Creature — Monkey Pirate 2/1. Oracle text:
///   "Whenever Ragavan, Nimble Pilferer deals combat damage to a player,
///    create a Treasure token. Then exile the top card of that player's
///    library. Until end of turn, you may cast that card.
///    Dash {1}{R}."
///
/// ## Implemented (v1)
/// - 2/1 Legendary Creature — Monkey Pirate, mana cost {R}.
/// - Combat-damage-to-a-player triggered ability (CR 510, CR 603.1) wired
///   over <see cref="CombatDamageDealtEvent"/> filtered to the source card
///   and a non-null <see cref="CombatDamageDealtEvent.TargetPlayer"/>. The
///   resolved effect:
///     1. creates a Treasure token under the Ragavan controller via
///        <see cref="TokenFactory.CreateTreasure"/>;
///     2. exiles the top card of the damaged player's library (no-op when
///        the library is empty — empty-library state-loss is handled by
///        SBAs, not here);
///     3. stamps a runtime exile-cast grant on the exiled card via
///        <see cref="Card.GrantRuntimeExileCast"/> permitting the Ragavan
///        controller to cast that card from exile for its printed mana
///        cost until end of turn (CR 118.9). The matching alternative
///        cost is <see cref="Majik.Core.Costs.ExileCastAlternativeCost"/>
///        — unlike Suspend / Cascade, the allowed caster is NOT the
///        card's owner (Ragavan exiles from the opponent's library);
///     4. when an <see cref="IEventBus"/> is supplied, the effect
///        subscribes a one-shot <see cref="StepStartedEvent"/> handler
///        that clears the grant on the first Cleanup step (CR 514.2) and
///        unsubscribes itself.
///
/// ## How the granted card is cast
/// Pass <see cref="Majik.Core.Costs.ExileCastAlternativeCost"/> built from
/// the exiled card's <see cref="Card.RuntimeExileCastCost"/> to
/// <see cref="Majik.Core.Game.SpellCastFlow.CastAsync"/>. The alt cost
/// rejects all callers other than <see cref="Card.RuntimeExileCastAllowedCaster"/>;
/// once the EOT subscription clears the grant, every probe rejects.
///
/// ## Deferred (v1 gaps)
/// - <b>Dash {1}{R}</b>: CR 702.108 alternate cost + "return at end of turn"
///   delayed trigger + "deals combat damage as if it had haste" interaction
///   not yet surfaced — no DashAlternativeCost / DashReturnRegistry in the
///   codebase. The Dash keyword marker is intentionally NOT attached so the
///   card's printed-cost cast path stays clean; agents that want to bid
///   Dash will need the alt-cost infra first.
/// - <b>"You may" prompt before casting</b>: the grant + alt cost is the
///   permission layer; the actual decision to cast belongs to the agent's
///   priority loop (HeuristicBotAgent already iterates alt costs and
///   chooses whether to play them). No new prompt surface is introduced.
/// </summary>
[CardName("Ragavan, Nimble Pilferer")]
public static class RagavanNimblePilfererFactory
{
    /// <summary>
    /// Construct Ragavan with no live ZoneService / event-bus / TriggerManager
    /// wiring. The combat-damage trigger is attached for shape but not
    /// registered; the exile move uses raw zone moves; the runtime
    /// exile-cast grant remains until the test clears it manually
    /// (no EOT cleanup subscription). Suitable for shape / dispatcher tests.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, zoneService: null, eventBus: null, triggers: null);

    /// <summary>
    /// Construct Ragavan with optional runtime services. When
    /// <paramref name="zoneService"/> is supplied the exile move publishes
    /// a <see cref="CardMovedEvent"/>; when <paramref name="eventBus"/> is
    /// supplied the runtime exile-cast grant is cleared on the next Cleanup
    /// step; when <paramref name="triggers"/> is supplied the combat
    /// trigger is registered so a <see cref="CombatDamageDealtEvent"/>
    /// automatically queues the ability.
    /// </summary>
    public static Creature Create(
        Player owner,
        ZoneService? zoneService,
        IEventBus? eventBus,
        TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: "Ragavan, Nimble Pilferer",
            manaCost: "{R}",
            power: 2,
            toughness: 1,
            supertypes: new[] { CardSupertype.Legendary },
            subtypes: new[] { CardSubtype.Monkey, CardSubtype.Pirate });

        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // Combat-damage-to-a-player trigger — CR 510, CR 603.1.
        //   "Whenever Ragavan, Nimble Pilferer deals combat damage to a
        //    player, create a Treasure token. Then exile the top card of
        //    that player's library. Until end of turn, you may cast that
        //    card."
        // The predicate captures the damaged player off the event so the
        // resolved effect targets the correct library at fire time. The
        // capture lives in a closure shared with the effect — CR 603.3
        // evaluates the trigger condition before the ability hits the
        // stack, so the captured player is fresh by the time the effect
        // resolves.
        // ----------------------------------------------------------------
        Player? capturedDamaged = null;

        var effect = new Effect(
            "Ragavan: Treasure + exile top of damaged player's library + may-cast EOT grant",
            () =>
            {
                var victim = capturedDamaged;
                if (victim == null) return;

                // 1) Create a Treasure token under the Ragavan controller.
                //    Routes through TokenFactory so the standard 5x
                //    ManaAbility-per-colour shape is preserved.
                TokenFactory.CreateTreasure(owner, zoneService);

                // 2) Exile the top card of the damaged player's library.
                //    Empty-library is a no-op (CR 120.3 — state-based
                //    actions handle the loss-condition, not this effect).
                var top = victim.Zones.Library.GetCards().FirstOrDefault();
                if (top == null) return;

                if (zoneService != null)
                {
                    zoneService.MoveCard(top, ZoneType.Library, ZoneType.Exile);
                }
                else
                {
                    victim.Zones.Library.RemoveCard(top);
                    victim.Zones.Exile.AddCard(top);
                    top.SetZone(ZoneType.Exile);
                }

                // 3) Stamp the "you may cast that card" grant. The Ragavan
                //    controller (not the card's owner) is the allowed
                //    caster; cost is the card's printed mana cost.
                if (top is Card stampable)
                {
                    stampable.GrantRuntimeExileCast(owner, stampable.ManaCostValue);

                    // 4) EOT cleanup — CR 514.2 / CR 514.3. Schedule a
                    //    one-shot handler that clears the grant on the
                    //    first Cleanup step and unsubscribes. Skipped
                    //    when no bus is wired (callers manage EOT
                    //    manually in tests).
                    if (eventBus != null)
                    {
                        Action<StepStartedEvent>? handler = null;
                        handler = (e) =>
                        {
                            if (e.StepType != PhaseStateType.Cleanup) return;
                            stampable.ClearRuntimeExileCast();
                            if (handler != null) eventBus.Unsubscribe(handler);
                        };
                        eventBus.Subscribe(handler);
                    }
                }
            });

        var trigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: new EventTriggerCondition<CombatDamageDealtEvent>((e, _) =>
            {
                if (!ReferenceEquals(e.Source, card)) return false;
                if (e.TargetPlayer == null) return false;
                capturedDamaged = e.TargetPlayer;
                return true;
            }),
            effects: new IEffect[] { effect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(trigger);
        triggers?.RegisterTriggeredAbility(trigger);

        return card;
    }
}
