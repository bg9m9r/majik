using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.StateMachine;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Robber of the Rich (Throne of Eldraine, {1}{R}).
///
/// Creature — Human Archer Rogue 2/2. Oracle text:
///   "Reach, haste
///    Whenever this creature attacks, if defending player has more cards in
///    hand than you, exile the top card of their library. During any turn you
///    attacked with a Rogue, you may cast that card and you may spend mana as
///    though it were mana of any color to cast that spell."
///
/// Base identity (name, Creature, Human/Archer/Rogue subtypes, {1}{R}, 2/2)
/// loads from the embedded JSON definition <c>robber-of-the-rich.json</c> via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>. Keywords + the attack trigger
/// are layered on here (the JSON ability schema does not yet cover keyword
/// markers or the attack/exile/grant trigger shape).
///
/// ## Implemented (v1)
/// - 2/2 Creature — Human Archer Rogue at {1}{R}.
/// - <see cref="KeywordAbility"/> Reach (CR 702.9b) + Haste (CR 702.10) —
///   markers consumed by the combat subsystem.
/// - <b>Attack trigger with intervening-if</b> (CR 508.1f attack trigger +
///   CR 603.4 intervening "if"). The trigger condition fires only when (a)
///   this card is the declared attacker AND (b) the defending player has
///   strictly more cards in hand than the Robber's controller — the
///   intervening-if is checked at trigger time; CR 603.4 also re-checks it
///   on resolution (this factory re-derives the defending player + caster
///   off the captured event, so an empty-defender / non-player attack is a
///   no-op). On resolution:
///     1. exile the top card of the defending player's library (no-op when
///        the library is empty — empty-library state-loss is the SBA's job,
///        CR 104.3a / 704, not this effect);
///     2. stamp a runtime exile-cast grant via
///        <see cref="Card.GrantRuntimeExileCast"/> permitting the Robber's
///        controller (NOT the card's owner — the card was exiled from the
///        defender's library) to cast that card from exile for its printed
///        mana cost (CR 118.9). The matching alternative cost is
///        <see cref="Majik.Core.Costs.ExileCastAlternativeCost"/> — the same
///        cast-from-exile probe surface as Ragavan, Nimble Pilferer;
///     3. when an <see cref="IEventBus"/> is supplied, a one-shot
///        <see cref="StepStartedEvent"/> handler clears the grant on the
///        first Cleanup step (CR 514.2) and unsubscribes itself. Robber's
///        printed window is "during any turn you attacked with a Rogue";
///        because the grant is stamped only when Robber (itself a Rogue)
///        attacks, that window is the remainder of the current turn, so the
///        EOT clear is the correct lifetime here.
///
/// ## Deferred (v1 gap — matches Agatha's Soul Cauldron posture)
/// - <b>"You may spend mana as though it were mana of any color to cast that
///   spell."</b> CR 609.4b / 118.x mana-payment substitution. There is no
///   mana-color-substitute / mana-payment replacement infrastructure in the
///   engine yet (the same gap Agatha's Soul Cauldron documents for its
///   "spend mana as though it were mana of any color to activate" clause).
///   The exile-cast grant carries the card's PRINTED mana cost; the caster
///   must currently pay that cost with correctly-colored mana. The
///   permission-to-cast layer (grant + alt cost) is fully wired — only the
///   color-substitution convenience is deferred.
/// - <b>"During any turn you attacked with a Rogue."</b> v1 scopes the grant
///   to the turn Robber itself attacked (Robber is a Rogue, so the
///   precondition is always met when this trigger fires). A separate Rogue
///   attacking on a later turn re-opening the window for previously-exiled
///   cards is not modelled — that would need a per-card "exiled by Robber"
///   ledger keyed to any-Rogue-attacked turns; deferred until a use case
///   needs it.
/// </summary>
[CardName("Robber of the Rich")]
public static class RobberOfTheRichFactory
{
    public const string Slug = "robber-of-the-rich";

    /// <summary>
    /// Construct Robber of the Rich with no live ZoneService / TriggerManager
    /// / EventBus wiring (shape / dispatcher path). Reach, Haste, and the
    /// attack trigger are attached to the card so structural assertions see
    /// them; nothing is registered with a manager and the exile move uses raw
    /// zone moves with no EOT cleanup.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, zoneService: null, triggers: null, eventBus: null);

    /// <summary>
    /// Construct Robber of the Rich with optional live wiring. When
    /// <paramref name="triggers"/> is supplied the attack trigger is
    /// registered so a <see cref="CreatureAttacksEvent"/> for this card (with
    /// the intervening-if satisfied) queues the ability. When
    /// <paramref name="zoneService"/> is supplied the exile move publishes a
    /// <see cref="CardMovedEvent"/>; when <paramref name="eventBus"/> is
    /// supplied the runtime exile-cast grant is cleared on the next Cleanup
    /// step.
    /// </summary>
    public static Creature Create(
        Player owner,
        ZoneService? zoneService,
        TriggerManager? triggers,
        IEventBus? eventBus)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Creature,
        // Human/Archer/Rogue, {1}{R}, 2/2). The JSON carries no abilities —
        // keywords + the attack trigger are layered on below.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Creature)CardDefinitionFactory.Build(definition, owner);

        // CR 702.9b — Reach. KeywordAbility marker so CombatAbilities allows
        // this creature to block fliers.
        card.AddAbility(new KeywordAbility("Reach", card, owner));

        // CR 702.10 — Haste. KeywordAbility marker so summoning-sickness
        // checks let this creature attack / tap the turn it enters.
        card.AddAbility(new KeywordAbility("Haste", card, owner));

        AttachAttackExileTrigger(card, owner, zoneService, triggers, eventBus);

        return card;
    }

    /// <summary>
    /// "Whenever this creature attacks, if defending player has more cards in
    /// hand than you, exile the top card of their library. … you may cast
    /// that card …" (CR 508.1f + CR 603.4 intervening-if + CR 118.9 grant.)
    /// </summary>
    private static void AttachAttackExileTrigger(
        Creature card,
        Player owner,
        ZoneService? zoneService,
        TriggerManager? triggers,
        IEventBus? eventBus)
    {
        // The defending player captured off the firing event so the resolved
        // effect targets the correct library. Shared via closure with the
        // effect; CR 603.3 evaluates the condition before the ability hits the
        // stack, so the capture is fresh by resolution time.
        Player? capturedDefender = null;

        var effect = new Effect(
            "Robber of the Rich: exile top of defending player's library + may-cast grant",
            () =>
            {
                var defender = capturedDefender;
                if (defender == null) return;

                var caster = card.Controller ?? owner;

                // CR 603.4 — re-check the intervening "if" on resolution. If
                // the defender no longer has more cards in hand than the
                // caster, the ability does nothing.
                if (defender.Zones.Hand.Count <= caster.Zones.Hand.Count) return;

                // Exile the top card of the defending player's library.
                // Empty-library is a no-op (CR 104.3a — the loss condition is
                // an SBA, not this effect's concern).
                var top = defender.Zones.Library.GetCards().FirstOrDefault();
                if (top == null) return;

                if (zoneService != null)
                {
                    zoneService.MoveCard(top, ZoneType.Library, ZoneType.Exile);
                }
                else
                {
                    defender.Zones.Library.RemoveCard(top);
                    defender.Zones.Exile.AddCard(top);
                    top.SetZone(ZoneType.Exile);
                }

                // CR 118.9 — stamp the "you may cast that card" grant. The
                // caster (Robber's controller, not the card's owner) is the
                // allowed caster; cost is the card's printed mana cost.
                if (top is Card stampable)
                {
                    stampable.GrantRuntimeExileCast(caster, stampable.ManaCostValue);

                    // CR 514.2 — clear the grant on the first Cleanup step and
                    // unsubscribe. Skipped when no bus is wired (tests manage
                    // EOT manually).
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
            condition: new EventTriggerCondition<CreatureAttacksEvent>((e, _) =>
            {
                if (!ReferenceEquals(e.Attacker, card)) return false;

                // Defender must be a player (the printed text reads
                // "defending player"; attacking a planeswalker doesn't supply
                // a "defending player" hand to compare against).
                if (e.DefendingPlayerOrPlaneswalker is not Player defender) return false;

                var caster = card.Controller ?? owner;

                // CR 603.4 intervening "if" — checked when the trigger would
                // be put on the stack: defending player must have strictly
                // more cards in hand than the Robber's controller.
                if (defender.Zones.Hand.Count <= caster.Zones.Hand.Count) return false;

                capturedDefender = defender;
                return true;
            }),
            effects: new IEffect[] { effect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(trigger);
        triggers?.RegisterTriggeredAbility(trigger);
    }
}
