using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Healer of the Glade (Core Set 2020, {G}).
///
/// Creature — Elemental 1/2. Oracle text:
///   "When this creature enters, you gain 3 life."
///
/// ## Implementation
///
/// - 1/2 Creature — Elemental with mana cost {G}. Color identity green (derived
///   from the {G} pip per CR 202.2c). Mana value 1 (CR 202.3).
/// - <b>ETB triggered ability</b> (CR 603.1, CR 603.6a): "When this creature
///   enters, you gain 3 life." Unconditional self-ETB via
///   <see cref="Triggers.OnEnterBattlefieldSelf"/> — no intervening-if clause
///   (CR 603.4 does not apply). Resolution calls
///   <c>controller.GainLife(3)</c> (CR 119.3) so the replacement bus fires
///   per CR 614. Active only from the battlefield (CR 603.6a).
///
/// ## Wiring overloads
/// - <see cref="Create(Player)"/> — shape only. ETB trigger is attached to the
///   card for shape inspection; not registered with a
///   <see cref="TriggerManager"/>. Suitable for dispatcher / structural tests.
/// - <see cref="Create(Player, Events.IEventBus?, TriggerManager?)"/> — fully
///   wired. ETB trigger registered with <paramref name="triggers"/> so
///   <see cref="Majik.Core.Domain.DomainEvents.CardMovedEvent"/>s on the bus
///   route it to the stack.
/// </summary>
[CardName("Healer of the Glade")]
public static class HealerOfTheGladeFactory
{
    public const string CardName = "Healer of the Glade";
    public const string PrintedManaCost = "{G}";
    public const int Power = 1;
    public const int Toughness = 2;
    public const int LifeGainAmount = 3;

    /// <summary>
    /// Construct Healer of the Glade with no live wiring. The ETB trigger is
    /// attached to the card for shape inspection; not registered with any
    /// <see cref="TriggerManager"/>. Suitable for dispatcher / structural tests.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, eventBus: null, triggers: null);

    /// <summary>
    /// Construct Healer of the Glade with optional runtime services. When
    /// <paramref name="triggers"/> is supplied, the ETB trigger is registered
    /// so <see cref="Majik.Core.Domain.DomainEvents.CardMovedEvent"/>s published
    /// on the bus route it to the stack.
    /// </summary>
    public static Creature Create(
        Player owner,
        Events.IEventBus? eventBus,
        TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            subtypes: new[] { CardSubtype.Elemental });

        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // ETB triggered ability (CR 603.1 / CR 603.6a).
        //   "When this creature enters, you gain 3 life."
        // Unconditional self-ETB — no intervening-if (CR 603.4 does not
        // apply here). Controller gains life via CR 119.3; the closure
        // re-resolves controller at execute time for blink / control-change
        // correctness.
        // ----------------------------------------------------------------
        var etbCondition = Triggers.OnEnterBattlefieldSelf(card);

        var etbEffect = new Effect(
            $"{CardName}: you gain {LifeGainAmount} life (when this creature enters)",
            () =>
            {
                var controller = card.Controller ?? owner;
                controller.GainLife(LifeGainAmount);
            });

        var etbTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: etbCondition,
            effects: new IEffect[] { etbEffect },
            interveningIf: null,
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(etbTrigger);
        triggers?.RegisterTriggeredAbility(etbTrigger);

        return card;
    }
}
