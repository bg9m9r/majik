using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.Primitives;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Merchant of Secrets (Onslaught, {2}{U}).
///
/// Creature — Human Wizard 1/1. Oracle text:
///   "When this creature enters, draw a card."
///
/// ## Implementation
///
/// - 1/1 Human Wizard with mana cost {2}{U}. Color identity blue (derived
///   from the {U} pip per CR 202.2c).
/// - <b>ETB triggered ability</b> (CR 603.1, CR 603.6a): "When this creature
///   enters, draw a card." Unconditional self-ETB via
///   <see cref="Triggers.OnEnterBattlefieldSelf"/> — no intervening-if
///   clause (CR 603.4 does not apply). Resolution calls
///   <see cref="Fx.DrawCards"/>(controller, 1) so the replacement bus
///   (Dredge / Alms Collector / future draw-replacement effects) fires
///   per-draw (CR 614), and an empty library stamps the SBA loss flag
///   (CR 704.5b) without crashing. Active only from the battlefield
///   (CR 603.6a — triggers require the source to be on the battlefield
///   at the time of trigger).
///
/// ## Wiring overloads
/// - <see cref="Create(Player)"/> — shape only. ETB trigger is attached to
///   the card for shape inspection; not registered with a
///   <see cref="TriggerManager"/>. Suitable for dispatcher / structural
///   tests.
/// - <see cref="Create(Player, Events.IEventBus?, TriggerManager?)"/> —
///   fully wired. ETB trigger registered with <paramref name="triggers"/>
///   so <see cref="Majik.Core.Domain.DomainEvents.CardMovedEvent"/>s on
///   the bus route it to the stack.
///
/// ## Design notes
/// Merchant of Secrets is structurally identical to Cloudkin Seer's draw
/// clause but is a 1/1 Human Wizard with no Flying. The controller draw
/// in the ETB closure captures <c>owner</c> as the initial controller and
/// re-resolves via <c>card.Controller ?? owner</c> at resolve time to
/// handle blink / control-change corner cases correctly.
/// </summary>
[CardName("Merchant of Secrets")]
public static class MerchantOfSecretsFactory
{
    public const string CardName = "Merchant of Secrets";
    public const string PrintedManaCost = "{2}{U}";
    public const int Power = 1;
    public const int Toughness = 1;
    public const int DrawAmount = 1;

    /// <summary>
    /// Construct Merchant of Secrets with no live wiring. The ETB trigger is
    /// attached to the card for shape inspection; not registered with any
    /// <see cref="TriggerManager"/>. Suitable for dispatcher / structural
    /// tests.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, eventBus: null, triggers: null);

    /// <summary>
    /// Construct Merchant of Secrets with optional runtime services. When
    /// <paramref name="triggers"/> is supplied, the ETB trigger is
    /// registered so <see cref="Majik.Core.Domain.DomainEvents.CardMovedEvent"/>s
    /// published on the bus route it to the stack.
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
            subtypes: new[] { CardSubtype.Human, CardSubtype.Wizard });

        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // ETB triggered ability (CR 603.1 / CR 603.6a).
        //   "When this creature enters, draw a card."
        // Unconditional self-ETB — no intervening-if (CR 603.4 does not
        // apply here). Routed through Fx.DrawCards so the replacement bus
        // + empty-library SBA flag fire correctly per CR 121.1 + CR 704.5b.
        // The controller closure re-resolves at execute time so blink /
        // control-change scenarios draw for the correct player.
        // ----------------------------------------------------------------
        var etbCondition = Triggers.OnEnterBattlefieldSelf(card);

        var etbEffect = new Effect(
            $"{CardName}: draw {DrawAmount} card (when this creature enters)",
            () =>
            {
                var controller = card.Controller ?? owner;
                Fx.DrawCards(controller, DrawAmount);
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
