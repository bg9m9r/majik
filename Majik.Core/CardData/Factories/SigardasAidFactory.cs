using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Sigarda's Aid (Eldritch Moon, {1}{W}).
///
/// Enchantment. Oracle text:
///   "Equipment and Auras you control have flash.
///    Whenever an Equipment enters under your control, you may attach it
///    to target creature you control."
///
/// ## Implemented (v1)
/// - Enchantment with mana cost {1}{W}.
/// - <b>Printed static</b> (CR 117.1 / 702.8): "Equipment and Auras you
///   control have flash." Wired via a <see cref="FlashGrantStaticEffect"/>:
///   while Sigarda's Aid is on the battlefield, an entry is registered in
///   <see cref="Majik.Core.Rules.FlashGrantRegistry"/> matching any card
///   owned (and so by default controlled at cast-time) by Sigarda's
///   controller whose subtypes include Equipment or Aura. Hammer Time
///   decks consume this — <see cref="Majik.Core.Rules.TimingRules.CanCastAtInstantSpeed"/>
///   consults the registry after the printed Instant/Flash check.
///   "You control" is evaluated at the moment of the cast-time speed
///   check — for cards in hand the owner is the cast-time controller per
///   CR 108.4, so an owner check suffices for v1 (multi-zone control
///   transfer of cards in hand is not modelled).
/// - <b>ETB-attach rider</b> (CR 603.2 / 603.6a / 701.3a): "Whenever an
///   Equipment enters under your control, you may attach it to target
///   creature you control." Wired as a <see cref="TriggeredAbility"/>
///   over <see cref="CardMovedEvent"/>: condition matches when the moved
///   card lands on the battlefield, is a <see cref="Permanent"/> with
///   the Equipment subtype, and its controller is Sigarda's controller.
///   The resolution effect attaches the captured permanent to the first
///   creature on the controller's battlefield (deterministic v1 pick).
///
/// ## Deferred (v1 gaps)
/// - <b>"You may" prompt</b>: the attach rider always fires when a creature
///   is available. The optional "you may" decline path requires an
///   IPlayerAgent boolean prompt the engine doesn't yet thread through
///   triggered-ability resolution.
/// - <b>Target creature prompt</b>: "target creature you control" auto-picks
///   the first controller-side creature (CR 701.3a target prompt is
///   deferred — same v1 simplification as Stoneforge Mystic's attach
///   step).
/// - <b>Controller transfers of cards in hand</b>: the flash-grant
///   predicate keys on owner (CR 108.4 — controller of a card outside the
///   battlefield is its owner). Control-changing effects targeting cards
///   in hand are not modelled by v1 and would need a separate hand-
///   controller-override hook to flow through this predicate.
/// </summary>
public static class SigardasAidFactory
{
    public const string CardName = "Sigarda's Aid";
    public const string Cost = "{1}{W}";

    /// <summary>
    /// Construct Sigarda's Aid with no live event-bus / trigger-manager
    /// wiring. The flash-grant lifecycle is created but never attached
    /// (so the registry isn't touched on the shape path), and the
    /// triggered ability is attached to the card's
    /// <see cref="Card.Abilities"/> collection without bus registration.
    /// Suitable for unit / shape tests.
    /// </summary>
    public static Enchantment Create(Player owner) =>
        Create(owner, eventBus: null, triggers: null);

    /// <summary>
    /// Construct Sigarda's Aid with optional runtime services. When
    /// <paramref name="eventBus"/> is supplied, the flash-grant lifecycle
    /// attaches (registering with <see cref="Majik.Core.Rules.FlashGrantRegistry"/>
    /// while Sigarda is on the battlefield, releasing on LTB). When
    /// <paramref name="triggers"/> is supplied, the ETB-attach rider is
    /// registered for bus-driven firing.
    /// </summary>
    public static Enchantment Create(
        Player owner,
        IEventBus? eventBus,
        TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Enchantment(CardName, Cost);
        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // Printed static — "Equipment and Auras you control have flash."
        // (CR 117.1 / 702.8.) FlashGrantStaticEffect handles ETB/LTB
        // lifecycle via CardMovedEvent. Predicate keys off owner: per
        // CR 108.4 the controller of a card outside the battlefield is
        // its owner, so for cards in hand this is the cast-time
        // controller. Cards already on the battlefield don't need flash
        // (they're already there), so the predicate is harmless when
        // those are queried too.
        // ----------------------------------------------------------------
        var flashGrant = new FlashGrantStaticEffect(
            source: card,
            eventBus: eventBus,
            predicate: c =>
            {
                if (c == null) return false;
                if (!ReferenceEquals(c.Owner, owner)) return false;
                return c.HasSubtype(CardSubtype.Equipment)
                    || c.HasSubtype(CardSubtype.Aura);
            });
        flashGrant.Attach();

        // ----------------------------------------------------------------
        // ETB-attach rider — "Whenever an Equipment enters under your
        // control, you may attach it to target creature you control."
        // (CR 603.2 + CR 603.6a + CR 701.3a.) Closure-captured payload
        // follows the Amulet of Vigor pattern.
        // ----------------------------------------------------------------
        Permanent? pendingEquipment = null;

        var condition = new EventTriggerCondition<CardMovedEvent>((e, _) =>
        {
            if (e.ToZone != ZoneType.Battlefield) return false;
            if (e.Card is not Permanent permanent) return false;
            if (!ReferenceEquals(permanent.Controller, owner)) return false;
            if (!permanent.HasSubtype(CardSubtype.Equipment)) return false;
            pendingEquipment = permanent;
            return true;
        });

        var attachEffect = new Effect(
            "Sigarda's Aid — attach Equipment to a creature you control",
            () =>
            {
                var equipment = pendingEquipment;
                pendingEquipment = null;
                if (equipment == null) return;
                // CR 603.7c — re-check legality at resolution. If the
                // Equipment has already left the battlefield (e.g. an
                // earlier resolution moved it elsewhere) skip the attach.
                if (equipment.Zone != ZoneType.Battlefield) return;

                // "target creature you control" — v1 auto-picks the first
                // creature on the controller's battlefield (CR 701.3a
                // prompt deferred — same simplification as Stoneforge
                // Mystic's attach step).
                var bearer = owner.Zones.Battlefield.GetCards()
                    .OfType<Creature>()
                    .FirstOrDefault(c => ReferenceEquals(c.Controller, owner));
                if (bearer == null) return; // No legal target → no-op.

                equipment.AttachTo(bearer);
            });

        var attachTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: condition,
            effects: new IEffect[] { attachEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(attachTrigger);
        triggers?.RegisterTriggeredAbility(attachTrigger);

        return card;
    }
}
