using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.Primitives;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Cloudkin Seer (Core Set 2020, {2}{U}).
///
/// Creature — Elemental Wizard 2/1. Oracle text:
///   "Flying.
///    When this creature enters, draw a card."
///
/// ## Implementation
///
/// - 2/1 Elemental Wizard with mana cost {2}{U}. Color identity blue (derived
///   from the {U} pip per CR 202.2c).
/// - <b>Flying</b> (CR 702.9): <see cref="KeywordAbility"/> marker read by
///   <see cref="Majik.Core.Combat.CombatAbilities.HasFlying"/> for evasion
///   in the combat validator. Same wiring shape as Mulldrifter / Slickshot
///   Show-Off.
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
/// Cloudkin Seer is structurally identical to Mulldrifter's draw clause
/// but draws 1 instead of 2 and has no Evoke rider. The controller draw
/// in the ETB closure captures <c>owner</c> as the initial controller and
/// re-resolves via <c>card.Controller ?? owner</c> at resolve time to
/// handle blink / control-change corner cases correctly.
/// </summary>
[CardName("Cloudkin Seer")]
public static class CloudkinSeerFactory
{
    public const string CardName = "Cloudkin Seer";
    public const string PrintedManaCost = "{2}{U}";
    public const int Power = 2;
    public const int Toughness = 1;
    public const int DrawAmount = 1;

    /// <summary>
    /// Construct Cloudkin Seer with no live wiring. The ETB trigger is
    /// attached to the card for shape inspection; not registered with any
    /// <see cref="TriggerManager"/>. Suitable for dispatcher / structural
    /// tests.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, eventBus: null, triggers: null);

    /// <summary>
    /// Construct Cloudkin Seer with optional runtime services. When
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
            subtypes: new[] { CardSubtype.Elemental, CardSubtype.Wizard });

        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // Flying (CR 702.9). Keyword marker — CombatAbilities.HasFlying
        // reads this for evasion in the combat validator. Same wire-up
        // shape as Mulldrifter and Slickshot Show-Off.
        // ----------------------------------------------------------------
        card.AddAbility(new KeywordAbility("Flying", card, owner));

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
