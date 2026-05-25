using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Counters;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Champion of the Parish (Innistrad, {W}).
///
/// Creature — Human Soldier 1/1. Oracle text:
///   "Whenever another Human enters the battlefield under your control,
///    put a +1/+1 counter on Champion of the Parish."
///
/// ## Implemented (v1)
/// - 1/1 Creature — Human Soldier at {W}. Both <see cref="CardSubtype.Human"/>
///   and <see cref="CardSubtype.Soldier"/> assigned.
/// - <b>ETB-other-Human trigger (CR 603.1)</b>: "Whenever another Human
///   enters the battlefield under your control, put a +1/+1 counter on
///   Champion of the Parish." Wired via <see cref="EventTriggerCondition{T}"/>
///   over <see cref="CardMovedEvent"/> to the Battlefield. Predicate gates on:
///     1. The entering card is a <see cref="CardType.Creature"/> with
///        <see cref="CardSubtype.Human"/>.
///     2. The entering card's controller is the same as Champion's controller.
///     3. The entering card is NOT Champion itself (CR 603.1 — "another").
///   On resolution: Champion gains 1 <see cref="CounterType.PlusOnePlusOne"/>
///   counter (CR 122.1c).
/// - The trigger is active only while Champion is on the battlefield
///   (activeZones gate).
///
/// ## Lifecycle
/// The single-arg <see cref="Create(Player)"/> path attaches the trigger for
/// shape tests without <see cref="TriggerManager"/> registration. Use the
/// (owner, triggers) overload for bus-driven, fully-wired behavior.
///
/// ## Deferred (v1 gaps)
/// - Champion itself is a Human Soldier (CR 109.2), so it should trigger
///   Thalia's Lieutenant's ETB ability when it enters. That cross-card
///   interaction fires automatically when both factories are wired to the same
///   <see cref="TriggerManager"/> via their respective (owner, triggers)
///   overloads — no special-casing needed here.
/// </summary>
[CardName("Champion of the Parish")]
public static class ChampionOfTheParishFactory
{
    /// <summary>
    /// Construct Champion of the Parish with no live <see cref="TriggerManager"/>
    /// wiring. The ETB-other-Human trigger is attached to the card shape for
    /// structural / dispatch tests.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, triggers: null, replacements: null);

    /// <summary>
    /// Construct Champion of the Parish with optional trigger manager. When
    /// <paramref name="triggers"/> is supplied, the ETB-other-Human trigger is
    /// registered so a qualifying <see cref="CardMovedEvent"/> automatically
    /// queues the ability. When <paramref name="replacements"/> is supplied,
    /// the +1/+1 counter placement is routed through
    /// <see cref="CountersService.Add"/> so Hardened Scales / Doubling Season
    /// style replacements can rewrite the count (CR 614).
    /// </summary>
    public static Creature Create(Player owner, TriggerManager? triggers, ReplacementBus? replacements = null)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: "Champion of the Parish",
            manaCost: "{W}",
            power: 1,
            toughness: 1,
            subtypes: new[] { CardSubtype.Human, CardSubtype.Soldier });

        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // ETB-other-Human trigger — CR 603.1.
        //   "Whenever another Human enters the battlefield under your
        //    control, put a +1/+1 counter on Champion of the Parish."
        //
        // Condition: CardMovedEvent → Battlefield where:
        //   - The card is a Creature with the Human subtype.
        //   - The card's controller equals Champion's controller (owner).
        //   - The card is NOT Champion itself ("another").
        //
        // Effect: add 1 CounterType.PlusOnePlusOne to Champion (CR 122.1c).
        // Active only while Champion is on the Battlefield.
        // ----------------------------------------------------------------
        var counterEffect = new Effect(
            "Champion of the Parish: put a +1/+1 counter on it (another Human entered)",
            () => CountersService.Add(card, CounterType.PlusOnePlusOne, 1, replacements));

        var humanEtbTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: new EventTriggerCondition<CardMovedEvent>((e, _) =>
                e.ToZone == ZoneType.Battlefield
                && e.Card.HasType(CardType.Creature)
                && e.Card.HasSubtype(CardSubtype.Human)
                && ReferenceEquals(e.Card.Controller, owner)
                && !ReferenceEquals(e.Card, card)),
            effects: new IEffect[] { counterEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(humanEtbTrigger);
        triggers?.RegisterTriggeredAbility(humanEtbTrigger);

        return card;
    }
}
