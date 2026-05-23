using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Goblin Lackey (Urza's Destiny, {R}).
///
/// Creature — Goblin 1/1. Oracle text:
///   "Whenever Goblin Lackey deals combat damage to a player, you may put
///    a Goblin creature card from your hand onto the battlefield."
///
/// ## Implemented (v1)
/// - Creature {R} 1/1 — Goblin subtype, owner/controller wired.
/// - <b>Combat-damage-to-a-player trigger (CR 510, CR 603.1)</b> wired
///   over <see cref="CombatDamageDealtEvent"/> filtered to the source
///   card and a non-null <see cref="CombatDamageDealtEvent.TargetPlayer"/>
///   (mirrors the Ragavan, Nimble Pilferer shape). The resolved effect
///   scans the controller's hand for the first <see cref="Creature"/>
///   card with the <see cref="CardSubtype.Goblin"/> subtype and moves
///   it directly to the battlefield. Routes through
///   <see cref="ZoneService.MoveCard"/> when supplied so ETB triggers
///   on the cheated-in Goblin fire (CR 603.6a); falls back to raw zone
///   manipulation for the shape-only path.
/// - <b>"You may" prompt</b>: v1 auto-accepts when an eligible Goblin
///   creature card exists in hand (same approach as Aether Vial's tap
///   activated ability — declining the optional is deferred to the
///   agent-prompt MVP).
///
/// ## Deferred (v1 gaps)
/// - <b>Real "you may" decline path</b>: the agent cannot currently
///   choose to skip the cheat even when triggering Lackey isn't
///   advantageous (e.g. only a 1-mana Goblin is in hand and they'd
///   rather cast it for value). Same queue as Aether Vial.
/// - <b>Agent-driven Goblin selection</b>: v1 picks the first matching
///   creature card deterministically. Wire a selector callback when
///   the multi-candidate "choose a card to put onto the battlefield"
///   prompt ships (mirrors the same gap on Stoneforge Mystic's tutor).
/// </summary>
public static class GoblinLackeyFactory
{
    /// <summary>
    /// Construct Goblin Lackey with no live runtime wiring. The combat
    /// trigger is attached to the card shape but not registered with a
    /// <see cref="TriggerManager"/>; the hand → battlefield move uses
    /// raw zone manipulation. Suitable for shape / dispatcher tests.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, zoneService: null, eventBus: null, triggers: null);

    /// <summary>
    /// Construct Goblin Lackey with optional runtime services. When
    /// <paramref name="triggers"/> is supplied the combat-damage trigger
    /// is registered so a <see cref="CombatDamageDealtEvent"/>
    /// automatically queues the ability. When
    /// <paramref name="zoneService"/> is supplied the hand → battlefield
    /// move routes through <see cref="ZoneService.MoveCard"/> so ETB
    /// triggers on the cheated-in Goblin fire (CR 603.6a).
    /// </summary>
    public static Creature Create(
        Player owner,
        ZoneService? zoneService,
        IEventBus? eventBus,
        TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);
        _ = eventBus; // reserved for future EOT / cleanup wiring parity with Ragavan.

        var card = new Creature(
            name: "Goblin Lackey",
            manaCost: "{R}",
            power: 1,
            toughness: 1,
            subtypes: new[] { CardSubtype.Goblin });

        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // Combat-damage-to-a-player trigger — CR 510, CR 603.1.
        //   "Whenever Goblin Lackey deals combat damage to a player, you
        //    may put a Goblin creature card from your hand onto the
        //    battlefield."
        // Predicate gates on (source == this card) AND TargetPlayer not
        // null so damage to a creature/planeswalker does not fire.
        // Mirrors the Ragavan, Nimble Pilferer shape — same event, same
        // controller-side resolution context.
        // ----------------------------------------------------------------
        var effect = new Effect(
            "Goblin Lackey: put a Goblin creature card from hand to battlefield",
            () => PutGoblinFromHand(card, owner, zoneService));

        var trigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: new EventTriggerCondition<CombatDamageDealtEvent>((e, _) =>
            {
                if (!ReferenceEquals(e.Source, card)) return false;
                if (e.TargetPlayer == null) return false;
                return true;
            }),
            effects: new IEffect[] { effect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(trigger);
        triggers?.RegisterTriggeredAbility(trigger);

        return card;
    }

    /// <summary>
    /// Picks the first Goblin creature card in <paramref name="controller"/>'s
    /// hand and moves it to the battlefield. Routes through
    /// <see cref="ZoneService.MoveCard"/> when supplied so ETB triggers
    /// fire (CR 603.6a); falls back to raw zone manipulation otherwise
    /// (shape-only path). No-ops when no eligible Goblin creature card
    /// is in hand — the "you may" auto-accepts only when a candidate
    /// exists (CR 117.x — "you may" with no valid target).
    /// </summary>
    private static void PutGoblinFromHand(
        Creature lackey,
        Player controller,
        ZoneService? zoneService)
    {
        _ = lackey; // signature parity with other "put from hand" helpers.

        var pick = controller.Zones.Hand.GetCards()
            .OfType<Creature>()
            .FirstOrDefault(c => c.HasSubtype(CardSubtype.Goblin));

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
