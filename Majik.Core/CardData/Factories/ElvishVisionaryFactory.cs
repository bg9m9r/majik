using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Primitives;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Elvish Visionary (Magic 2010 / various reprints,
/// {1}{G}).
///
/// Creature — Elf Shaman 1/1. Oracle text:
///   "When this creature enters, draw a card."
///
/// ## Implemented (v1)
/// - 1/1 Elf Shaman with mana cost {1}{G} (mana value 2).
/// - <b>ETB triggered ability (CR 603.6a)</b>: when Elvish Visionary enters
///   the battlefield, its controller draws a card. Routed through
///   <see cref="Fx.DrawCards"/> so the replacement bus and the empty-library
///   SBA loss flag (CR 704.5b / CR 704.5b) fire correctly per CR 121.1.
/// - Single-arg dispatcher path attaches the ETB trigger shape without live
///   bus/trigger-manager wiring (suitable for shape / dispatcher tests).
///   The (owner, eventBus, triggers) overload registers the trigger with a
///   live <see cref="TriggerManager"/> so a <see cref="CardMovedEvent"/> to
///   the battlefield places it on the stack automatically (CR 603.3).
/// </summary>
[CardName("Elvish Visionary")]
public static class ElvishVisionaryFactory
{
    public const string CardName = "Elvish Visionary";
    public const string ManaCost = "{1}{G}";
    public const int Power = 1;
    public const int Toughness = 1;

    /// <summary>
    /// Construct Elvish Visionary with no live bus / trigger-manager wiring.
    /// The ETB trigger is attached to the card but not registered with a
    /// <see cref="TriggerManager"/>. Suitable for shape / dispatcher tests.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, eventBus: null, triggers: null);

    /// <summary>
    /// Construct Elvish Visionary with optional event bus and trigger manager.
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
            power: Power,
            toughness: Toughness,
            subtypes: new[] { CardSubtype.Elf, CardSubtype.Shaman });

        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // ETB triggered ability — CR 603.6a.
        //   "When this creature enters, draw a card."
        // Routed through Fx.DrawCards so the replacement bus + empty-library
        // SBA loss flag fire per CR 121.1 + CR 704.5b. No targets.
        // ----------------------------------------------------------------
        var etbEffect = new Effect(
            $"{CardName}: controller draws a card on ETB",
            () => Fx.DrawCards(owner, 1));

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
