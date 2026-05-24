using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Counters;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Thalia's Lieutenant (Shadows over Innistrad,
/// {1}{W}).
///
/// Creature — Human Soldier 1/1. Oracle text:
///   "When Thalia's Lieutenant enters the battlefield, put a +1/+1 counter
///    on each other Human you control.
///    Whenever another Human enters the battlefield under your control, put
///    a +1/+1 counter on Thalia's Lieutenant."
///
/// ## Implemented (v1)
/// - 1/1 Creature — Human Soldier at {1}{W}. Both
///   <see cref="CardSubtype.Human"/> and <see cref="CardSubtype.Soldier"/>
///   assigned.
/// - <b>ETB-self trigger (CR 603.1)</b>: "When Thalia's Lieutenant enters
///   the battlefield, put a +1/+1 counter on each other Human you control."
///   Wired via <see cref="Triggers.OnEnterBattlefieldSelf"/>. On resolution:
///   iterate the controller's battlefield, find all Creature permanents with
///   <see cref="CardSubtype.Human"/> except Lieutenant itself, and add 1
///   <see cref="CounterType.PlusOnePlusOne"/> counter to each.
/// - <b>ETB-other-Human trigger (CR 603.1)</b>: "Whenever another Human
///   enters the battlefield under your control, put a +1/+1 counter on
///   Thalia's Lieutenant." Wired via <see cref="EventTriggerCondition{T}"/>
///   over <see cref="CardMovedEvent"/>. Same predicate as Champion of the
///   Parish: Creature + Human + same controller + not self. Active only while
///   Lieutenant is on the battlefield.
///
/// ## Cross-card interactions (correct by oracle)
/// - When Champion of the Parish enters, it is a Human, so Thalia's
///   Lieutenant's second trigger fires and gives Lieutenant a counter.
/// - When Thalia's Lieutenant enters, Champion of the Parish's trigger fires
///   (if Champion is already on the battlefield) and gives Champion a counter.
///   Lieutenant's own ETB-self trigger also runs and gives Champion a counter
///   (and any other Humans already in play). Both are correct per printed oracle.
///
/// ## Lifecycle
/// The single-arg <see cref="Create(Player)"/> path attaches both triggers for
/// shape tests without <see cref="TriggerManager"/> registration. Use the
/// (owner, triggers) overload for bus-driven, fully-wired behavior.
///
/// ## Deferred (v1 gaps)
/// - The ETB-self effect iterates via <c>owner.Zones.Battlefield.GetCards()</c>
///   directly (raw zone access). The full production path uses a battlefield-
///   resolver passed as a Func to allow cross-zone service interop; v1 defers
///   this in favour of the same direct-zone pattern used by Agatha's Soul
///   Cauldron, Goblin Chieftain, and Kraul Harpooner.
/// </summary>
[CardName("Thalia's Lieutenant")]
public static class ThaliaLieutenantFactory
{
    /// <summary>
    /// Construct Thalia's Lieutenant with no live <see cref="TriggerManager"/>
    /// wiring. Both triggered abilities are attached to the card shape for
    /// structural / dispatch tests.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, triggers: null);

    /// <summary>
    /// Construct Thalia's Lieutenant with optional trigger manager. When
    /// <paramref name="triggers"/> is supplied, both triggered abilities are
    /// registered so qualifying events automatically queue the abilities.
    /// </summary>
    public static Creature Create(Player owner, TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: "Thalia's Lieutenant",
            manaCost: "{1}{W}",
            power: 1,
            toughness: 1,
            subtypes: new[] { CardSubtype.Human, CardSubtype.Soldier });

        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // ETB-self trigger — CR 603.1.
        //   "When Thalia's Lieutenant enters the battlefield, put a +1/+1
        //    counter on each other Human you control."
        //
        // Fires when Lieutenant itself moves to the Battlefield.
        // Effect: iterate the controller's battlefield; for each Creature
        // that has the Human subtype and is NOT Lieutenant, add 1 +1/+1
        // counter.
        // ----------------------------------------------------------------
        var etbSelfEffect = new Effect(
            "Thalia's Lieutenant: put a +1/+1 counter on each other Human you control (ETB)",
            () =>
            {
                var humans = owner.Zones.Battlefield.GetCards()
                    .OfType<Creature>()
                    .Where(c => c.HasSubtype(CardSubtype.Human) && !ReferenceEquals(c, card))
                    .ToList();

                foreach (var human in humans)
                {
                    human.Counters.Add(CounterType.PlusOnePlusOne, 1);
                }
            });

        var etbSelfTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnEnterBattlefieldSelf(card),
            effects: new IEffect[] { etbSelfEffect });

        card.AddAbility(etbSelfTrigger);
        triggers?.RegisterTriggeredAbility(etbSelfTrigger);

        // ----------------------------------------------------------------
        // ETB-other-Human trigger — CR 603.1.
        //   "Whenever another Human enters the battlefield under your
        //    control, put a +1/+1 counter on Thalia's Lieutenant."
        //
        // Same predicate shape as Champion of the Parish:
        //   - Card is a Creature with Human subtype.
        //   - Card's controller equals Lieutenant's controller (owner).
        //   - Card is NOT Lieutenant itself ("another").
        // Active only while Lieutenant is on the Battlefield.
        // Effect: add 1 +1/+1 counter to Lieutenant (CR 122.1c).
        // ----------------------------------------------------------------
        var humanEtbCounterEffect = new Effect(
            "Thalia's Lieutenant: put a +1/+1 counter on it (another Human entered)",
            () => card.Counters.Add(CounterType.PlusOnePlusOne, 1));

        var humanEtbTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: new EventTriggerCondition<CardMovedEvent>((e, _) =>
                e.ToZone == ZoneType.Battlefield
                && e.Card.HasType(CardType.Creature)
                && e.Card.HasSubtype(CardSubtype.Human)
                && ReferenceEquals(e.Card.Controller, owner)
                && !ReferenceEquals(e.Card, card)),
            effects: new IEffect[] { humanEtbCounterEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(humanEtbTrigger);
        triggers?.RegisterTriggeredAbility(humanEtbTrigger);

        return card;
    }
}
