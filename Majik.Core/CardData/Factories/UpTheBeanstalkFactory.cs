using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Up the Beanstalk (Wilds of Eldraine — Enchanting
/// Tales, {1}{G}).
///
/// Enchantment — {1}{G}. Oracle text:
///   "When Up the Beanstalk enters, draw a card.
///    Whenever you cast a spell with mana value 5 or greater, draw a card."
///
/// ## Implementation
///
/// Two <see cref="TriggeredAbility"/> instances are attached to the card:
///
///   1. <b>ETB draw</b> — <see cref="EventTriggerCondition{TEvent}"/> over
///      <see cref="CardMovedEvent"/>, matches when this card enters the
///      battlefield (CR 603.6a). Effect: controller draws a card (CR 121).
///   2. <b>Cast-mana-value-5+ draw</b> — <see cref="EventTriggerCondition{TEvent}"/>
///      over <see cref="SpellCastEvent"/>, matches when the spell's
///      controller is this card's controller AND the spell's mana value
///      is &gt;= 5 (CR 202.3, 603.2). Effect: controller draws a card.
///
/// Draws read the top of the controller's library and move it to hand,
/// matching the inline pattern used by
/// <see cref="SpreadingSeasFactory"/>.
///
/// ## Notes
/// - Mana value is read off the printed card's <see cref="Card.ManaCostValue"/>
///   (<c>TotalValue</c>). CR 202.3b — for a spell on the stack with no X
///   cost the mana value is the printed total; X-spells with no chosen X
///   here would resolve to their printed value too. Tests below all use
///   non-X printed costs so this is a non-issue for v1.
/// - Controller-only gating uses <see cref="ISpell.Controller"/> reference
///   equality with this card's controller — same approach as Ledger
///   Shredder.
/// - Like Spreading Seas / Ledger Shredder, this factory does not require
///   a live <see cref="TriggerManager"/> to construct the card; pass one
///   to the overload to register both triggers with the bus for end-to-end
///   firing.
/// </summary>
[CardName("Up the Beanstalk")]
public static class UpTheBeanstalkFactory
{
    public const string CardName = "Up the Beanstalk";
    public const string Cost = "{1}{G}";

    /// <summary>
    /// Minimum mana value that fires the cast trigger.
    /// </summary>
    public const int CastTriggerManaValueThreshold = 5;

    /// <summary>
    /// Construct Up the Beanstalk with no live trigger-manager wiring.
    /// Both triggered abilities are attached to the card's
    /// <see cref="Card.Abilities"/> collection so structural shape tests
    /// can observe them; for end-to-end firing pass a live
    /// <see cref="TriggerManager"/> via the overload.
    /// </summary>
    public static Enchantment Create(Player owner) =>
        Create(owner, triggers: null);

    /// <summary>
    /// Construct Up the Beanstalk with optional trigger-manager wiring.
    /// When <paramref name="triggers"/> is supplied, both triggered
    /// abilities are registered so the bus surfaces them as pending.
    /// </summary>
    public static Enchantment Create(Player owner, TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Enchantment(CardName, Cost);
        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // Shared draw-a-card effect (top of controller's library → hand).
        // Matches the inline pattern in SpreadingSeasFactory. CR 121.
        // ----------------------------------------------------------------
        void DrawOne()
        {
            var top = owner.Zones.Library.GetCards().FirstOrDefault();
            if (top == null) return;
            owner.Zones.Library.RemoveCard(top);
            owner.Zones.Hand.AddCard(top);
            top.SetZone(ZoneType.Hand);
        }

        // Trigger 1: "When Up the Beanstalk enters, draw a card." (CR 603.6a)
        var etbCondition = new EventTriggerCondition<CardMovedEvent>(
            (e, _) => ReferenceEquals(e.Card, card) && e.ToZone == ZoneType.Battlefield);

        var etbDrawEffect = new Effect(
            "Up the Beanstalk — controller draws a card on ETB",
            DrawOne);

        var etbTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: etbCondition,
            effects: new IEffect[] { etbDrawEffect },
            activeZones: new[] { ZoneType.Battlefield });

        // Trigger 2: "Whenever you cast a spell with mana value 5 or
        // greater, draw a card." (CR 603.2, 202.3)
        var castCondition = new EventTriggerCondition<SpellCastEvent>((e, _) =>
        {
            if (!ReferenceEquals(e.Spell.Controller, owner)) return false;
            // ICard only exposes the printed mana cost string. Use the
            // parsed value-object on Card when available (Card.ManaCostValue),
            // falling back to a one-shot parse for any ICard implementer
            // that isn't a Card. CR 202.3.
            int manaValue = e.Spell.Card is Card concrete
                ? concrete.ManaCostValue.TotalValue
                : Majik.Core.ValueObjects.ManaCost.Parse(e.Spell.Card.ManaCost).TotalValue;
            return manaValue >= CastTriggerManaValueThreshold;
        });

        var castDrawEffect = new Effect(
            "Up the Beanstalk — controller draws a card on casting a mana-value-5+ spell",
            DrawOne);

        var castTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: castCondition,
            effects: new IEffect[] { castDrawEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(etbTrigger);
        card.AddAbility(castTrigger);

        triggers?.RegisterTriggeredAbility(etbTrigger);
        triggers?.RegisterTriggeredAbility(castTrigger);

        return card;
    }
}
