using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Events;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Sram, Senior Edificer (Aether Revolt, {1}{W}).
///
/// Legendary Creature — Dwarf Advisor 2/2. Oracle text:
///   "Whenever you cast an Aura, Equipment, or Vehicle spell, draw a card."
///
/// ## Implementation
///
/// - 2/2 Dwarf Advisor, Legendary, mana cost {1}{W}.
/// - <b>Spell-cast trigger</b> (CR 603.1, fires off
///   <see cref="SpellCastEvent"/>): predicate is
///   <c>spell.Controller == this card's controller</c> AND the spell's
///   card has at least one of the Aura / Equipment / Vehicle subtypes
///   (CR 205.3g/h/q). Effect draws one card via
///   <see cref="Majik.Core.Primitives.Fx.DrawCards"/> under the
///   controller — routes through the controller's replacement bus when
///   one is attached (CR 614, Dredge / etc.).
/// - Same shape as <see cref="MonasteryMentorFactory"/>'s token trigger:
///   <see cref="EventTriggerCondition{TEvent}"/> over
///   <see cref="SpellCastEvent"/>, wired through the supplied
///   <see cref="TriggerManager"/> when present.
///
/// ## Wiring overloads
/// - <see cref="Create(Player)"/> — shape only. Trigger is attached for
///   inspection; not registered (no live event bus).
/// - <see cref="Create(Player, IEventBus?, TriggerManager?)"/> — fully
///   wired. Trigger registered with <paramref name="triggers"/> so
///   <see cref="SpellCastEvent"/>s on the bus route it to the stack.
///
/// ## Deferred (v1 gaps)
/// - <b>Draw-replacement target</b>: the printed text says "draw a card"
///   unconditionally; replacement effects (e.g. Dredge) attached to the
///   controller can already intercept this via the
///   <see cref="Majik.Core.Effects.DrawCardIntent"/> bus inside
///   <see cref="Majik.Core.Primitives.Fx.DrawCards"/>.
/// </summary>
[CardName("Sram, Senior Edificer")]
public static class SramSeniorEdificerFactory
{
    public const string CardName = "Sram, Senior Edificer";
    public const string PrintedManaCost = "{1}{W}";
    public const int Power = 2;
    public const int Toughness = 2;

    /// <summary>
    /// Construct Sram with no live wiring. The cast-trigger is attached
    /// to the card shape; not registered (no trigger manager supplied).
    /// Suitable for dispatcher / shape tests.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, eventBus: null, triggers: null);

    /// <summary>
    /// Construct Sram with optional runtime services. When
    /// <paramref name="triggers"/> is supplied, the cast-trigger is
    /// registered so <see cref="SpellCastEvent"/>s published on the bus
    /// route through it.
    /// </summary>
    public static Creature Create(
        Player owner,
        IEventBus? eventBus,
        TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            supertypes: new[] { CardSupertype.Legendary },
            subtypes: new[] { CardSubtype.Dwarf, CardSubtype.Advisor });

        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // Cast trigger — CR 603.1.
        //   "Whenever you cast an Aura, Equipment, or Vehicle spell, draw
        //    a card."
        // "You cast" → the spell's controller is this card's controller.
        // Subtype gate: any one of Aura / Equipment / Vehicle (CR 205.3g/h/q)
        // — Sram fires once per qualifying spell (a single spell carrying
        // multiple of those subtypes still fires the ability exactly once
        // per CR 603.3: one event → one match → one stack object).
        // ----------------------------------------------------------------
        var castCondition = new EventTriggerCondition<SpellCastEvent>((e, _) =>
        {
            // CR 603.1 — controller match for the printed "you cast".
            // Compare to the card's current controller at evaluation time
            // (mirrors the Monastery Mentor pattern; the owner closure is
            // a safe fallback because Sram cannot change controller in
            // any printed effect).
            var liveController = card.Controller ?? owner;
            if (!ReferenceEquals(e.Spell.Controller, liveController))
            {
                return false;
            }

            var spellCard = e.Spell.Card;
            return spellCard.HasSubtype(CardSubtype.Aura)
                || spellCard.HasSubtype(CardSubtype.Equipment)
                || spellCard.HasSubtype(CardSubtype.Vehicle);
        });

        var drawEffect = new Effect(
            $"{CardName}: draw a card (whenever you cast an Aura/Equipment/Vehicle spell)",
            () =>
            {
                var controller = card.Controller ?? owner;
                Majik.Core.Primitives.Fx.DrawCards(controller, 1);
            });

        var castTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: castCondition,
            effects: new IEffect[] { drawEffect },
            activeZones: new[] { Zones.ZoneType.Battlefield });

        card.AddAbility(castTrigger);
        triggers?.RegisterTriggeredAbility(castTrigger);

        return card;
    }
}
