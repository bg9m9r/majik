using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Essence Warden (Planar Chaos, {G}).
///
/// Creature — Elf Shaman 1/1. Oracle text (current Scryfall):
///   "Whenever another creature enters, you gain 1 life."
///
/// Functional reprint of Soul Warden (green-costed) — the ETB-gain-life-on-
/// another-creature trigger is identical. Mirrors
/// <see cref="SoulWardenFactory"/>.
///
/// ## Implemented (v1)
/// - 1/1 Creature — Elf Shaman at {G}. Owner / controller wired.
/// - <b>ETB-other-creature trigger (CR 603.6a / CR 119.3)</b>: any creature
///   other than Essence Warden entering the battlefield (under any controller)
///   triggers; on resolution Essence Warden's controller gains 1 life.
///   Predicate gates on:
///     1. <see cref="CardMovedEvent.ToZone"/> == Battlefield.
///     2. The entering card is a <see cref="CardType.Creature"/>.
///     3. The entering card is NOT Essence Warden itself ("another" — CR 603.1).
///   Controller is not constrained — Essence Warden triggers on opponent's
///   creatures entering too (the printed Soul Sisters interaction).
///   The trigger is active only while Essence Warden is on the battlefield.
///
/// ## Lifecycle
/// The single-arg <see cref="Create(Player)"/> path attaches the trigger for
/// shape tests without <see cref="TriggerManager"/> registration. Use the
/// (owner, triggers) overload for bus-driven, fully-wired behavior — the
/// trigger fires on every qualifying <see cref="CardMovedEvent"/> the bus
/// publishes (CR 603.2 — once-per-event posture).
/// </summary>
[CardName("Essence Warden")]
public static class EssenceWardenFactory
{
    public const string CardName = "Essence Warden";
    public const string PrintedManaCost = "{G}";
    public const int Power = 1;
    public const int Toughness = 1;
    public const int LifeGainAmount = 1;

    /// <summary>
    /// Construct Essence Warden with no live <see cref="TriggerManager"/> wiring.
    /// The ETB-other-creature trigger is attached to the card shape for
    /// structural / dispatch tests; bus-driven firing requires the
    /// (owner, triggers) overload.
    /// </summary>
    public static Creature Create(Player owner) => Create(owner, triggers: null);

    /// <summary>
    /// Construct Essence Warden, registering the lifegain trigger with
    /// <paramref name="triggers"/> when supplied so a qualifying
    /// <see cref="CardMovedEvent"/> automatically queues the ability.
    /// </summary>
    public static Creature Create(Player owner, TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            subtypes: new[] { CardSubtype.Elf, CardSubtype.Shaman });

        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // ETB-other-creature trigger — CR 603.6a / CR 119.3.
        //   "Whenever another creature enters, you gain 1 life."
        //
        // Condition: CardMovedEvent → Battlefield where:
        //   - The card is a Creature (any controller — printed Essence
        //     Warden cares about ANY creature, not just yours).
        //   - The card is NOT Essence Warden itself ("another", CR 603.1).
        //
        // Effect: Essence Warden's controller gains 1 life (CR 118.3).
        // Controller is resolved live (card.Controller ?? owner) so that
        // control-change effects (Mind Control / Threaten) route gains
        // to the new controller.
        // Active only while Essence Warden is on the Battlefield.
        // ----------------------------------------------------------------
        var lifegainEffect = new Effect(
            $"{CardName}: controller gains {LifeGainAmount} life",
            () =>
            {
                var controller = card.Controller ?? owner;
                controller.GainLife(LifeGainAmount);
            });

        var etbOtherCreatureTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: new EventTriggerCondition<CardMovedEvent>((e, _) =>
                e.ToZone == ZoneType.Battlefield
                && e.Card.HasType(CardType.Creature)
                && !ReferenceEquals(e.Card, card)),
            effects: new IEffect[] { lifegainEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(etbOtherCreatureTrigger);
        triggers?.RegisterTriggeredAbility(etbOtherCreatureTrigger);

        return card;
    }
}
