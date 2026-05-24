using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Silvergill Adept (Lorwyn / various reprints,
/// {1}{U}).
///
/// Creature — Merfolk Wizard 2/1. Oracle text:
///   "As an additional cost to cast this spell, reveal a Merfolk card from
///    your hand or pay {3}.
///    When Silvergill Adept enters the battlefield, draw a card."
///
/// ## Implemented (v1)
/// - 2/1 Merfolk Wizard with mana cost {1}{U}.
/// - <b>Reveal-or-pay additional cost (CR 601.2b)</b>: represented as a
///   <see cref="KeywordAbility"/> marker ("RevealMerfolkOrPay3"). The actual
///   enforcement at cast-time (agent prompt: reveal a Merfolk card from hand
///   OR pay {3} as an additional cost) is deferred until the additional-cost
///   framework supports reveal-based alternatives. In production the cost
///   MUST be enforced before the spell resolves.
/// - <b>ETB triggered ability (CR 603.6a)</b>: when Silvergill Adept enters
///   the battlefield, its controller draws a card (top of library → hand).
///   If the library is empty, <see cref="Player.MarkTriedToDrawFromEmptyLibrary"/>
///   is called so SBAs can resolve loss on the next opportunity (CR 120.3).
/// - Single-arg dispatcher path attaches the ETB trigger for shape tests;
///   the (owner, eventBus, triggers) overload registers the trigger with a
///   live <see cref="TriggerManager"/> so a <see cref="CardMovedEvent"/>
///   to the battlefield places it on the stack automatically (CR 603.3).
///
/// ## Deferred (v1 gaps)
/// - <b>Reveal-or-pay enforcement</b>: the additional cast cost is a marker
///   only; the cast-time framework must prompt the controller to either
///   reveal a Merfolk card from hand or pay {3} before allowing the spell
///   to be placed on the stack (CR 601.2b).
/// - <b>Reveal event</b>: the production reveal-a-Merfolk path should emit
///   a <see cref="Majik.Core.Domain.DomainEvents.CardRevealedEvent"/>. No
///   reveal event is emitted in v1 (same gap as other reveal-cost cards).
/// </summary>
public static class SilvergillAdeptFactory
{
    public const string CardName = "Silvergill Adept";
    public const string ManaCost = "{1}{U}";

    /// <summary>
    /// Construct Silvergill Adept with no live bus / trigger-manager wiring.
    /// The ETB trigger is attached to the card but not registered with a
    /// <see cref="TriggerManager"/>. Suitable for shape / dispatcher tests.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, eventBus: null, triggers: null);

    /// <summary>
    /// Construct Silvergill Adept with optional event bus and trigger manager.
    /// When <paramref name="triggers"/> is supplied, the ETB trigger is
    /// registered so a <see cref="CardMovedEvent"/> to the battlefield
    /// automatically places it on the stack (CR 603.3).
    /// </summary>
    public static Creature Create(Player owner, IEventBus? eventBus, TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: ManaCost,
            power: 2,
            toughness: 1,
            subtypes: new[] { CardSubtype.Merfolk, CardSubtype.Wizard });

        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // Additional cast cost marker — CR 601.2b.
        //   "As an additional cost to cast this spell, reveal a Merfolk
        //    card from your hand or pay {3}."
        // v1: structural-only keyword marker; actual cost enforcement at
        // cast-time is deferred. Production callers MUST enforce this cost
        // before the spell is placed on the stack. See class xmldoc.
        // ----------------------------------------------------------------
        card.AddAbility(new KeywordAbility("RevealMerfolkOrPay3", card, owner));

        // ----------------------------------------------------------------
        // ETB triggered ability — CR 603.6a.
        //   "When Silvergill Adept enters the battlefield, draw a card."
        // ----------------------------------------------------------------
        var etbEffect = new Effect(
            "Silvergill Adept — controller draws a card on ETB",
            () =>
            {
                var top = owner.Zones.Library.GetCards().FirstOrDefault();
                if (top == null)
                {
                    // CR 120.3 — empty library; SBA resolves loss on next pass.
                    owner.MarkTriedToDrawFromEmptyLibrary();
                    return;
                }

                // Move Library → Hand (CR 121).
                owner.Zones.Library.RemoveCard(top);
                owner.Zones.Hand.AddCard(top);
                top.SetZone(ZoneType.Hand);
            });

        var etbTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnEnterBattlefieldSelf(card),
            effects: new IEffect[] { etbEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(etbTrigger);
        triggers?.RegisterTriggeredAbility(etbTrigger);

        return card;
    }
}
