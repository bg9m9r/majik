using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Primitives;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Helpful Hunter ({1}{W}).
///
/// Creature — Cat 1/1. Oracle text:
///   "When this creature enters, draw a card."
///
/// ## Implemented (v1)
/// - 1/1 Creature — Cat at {1}{W}, mana value 2, white.
/// - <b>ETB triggered ability (CR 603.6a)</b> via
///   <see cref="Triggers.OnEnterBattlefieldSelf"/>: on resolve the
///   controller draws 1 card via <see cref="Fx.DrawCards"/>.
///
/// ## Wiring
/// - Single-arg <c>Create(Player)</c>: trigger attached for shape inspection;
///   tests fire the effect directly or inspect the trigger shape. Suitable
///   for dispatcher / shape tests.
/// - Three-arg overload: <paramref name="triggers"/> registers the ETB
///   trigger so live bus-driven firing works end-to-end.
/// </summary>
[CardName("Helpful Hunter")]
public static class HelpfulHunterFactory
{
    public const string CardName = "Helpful Hunter";
    public const string PrintedManaCost = "{1}{W}";

    /// <summary>
    /// Construct Helpful Hunter with no live bus / trigger-manager wiring.
    /// Trigger is attached for shape inspection. Suitable for dispatcher /
    /// shape tests.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, eventBus: null, triggers: null);

    /// <summary>
    /// Construct Helpful Hunter with optional event bus + trigger manager.
    /// When <paramref name="triggers"/> is supplied, the ETB trigger is
    /// registered so bus events queue it automatically.
    /// </summary>
    public static Creature Create(Player owner, IEventBus? eventBus, TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: 1,
            toughness: 1,
            subtypes: new[] { CardSubtype.Cat });

        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // ETB — "When this creature enters, draw a card." CR 603.6a.
        // ----------------------------------------------------------------
        var etbEffect = new Effect(
            $"{CardName}: ETB — draw a card",
            () => Fx.DrawCards(card.Controller ?? owner, 1));

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
