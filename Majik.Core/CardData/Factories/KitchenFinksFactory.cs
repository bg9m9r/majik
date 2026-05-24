using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Counters;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Kitchen Finks (Shadowmoor / Modern Horizons 2,
/// {1}{G/W}{G/W}).
///
/// Creature — Ouphe 3/2. Oracle text:
///   "When Kitchen Finks enters the battlefield, you gain 2 life.
///    Persist (When this creature dies, if it had no -1/-1 counters on it,
///    return it to the battlefield under its owner's control with a -1/-1
///    counter on it.)"
///
/// ## Implemented (v1)
/// - 3/2 Creature — Ouphe, mana cost {1}{G/W}{G/W} (CR 107.4e hybrid pips —
///   <see cref="Majik.Core.ValueObjects.ManaCost.Parse"/> accepts each pip
///   and decomposes into a <c>HybridPip</c>, same as Boros Reckoner {R/W}).
///
/// - <b>ETB triggered ability (CR 603.6a)</b>: wired over
///   <see cref="CardMovedEvent"/> where the moved card is this Finks and the
///   destination zone is Battlefield. On resolution the controller gains 2 life
///   (CR 119.3). The trigger fires every time Finks enters the battlefield,
///   including on a Persist return, so an already-countered Finks that returns
///   still yields 2 life (the ETB is independent of Persist).
///
/// - <b>Persist (CR 702.78)</b>: "When this creature dies, if it had no -1/-1
///   counters on it, return it to the battlefield under its owner's control
///   with a -1/-1 counter on it." Implemented via a <see cref="TriggeredAbility"/>
///   that:
///   1. Triggers on a <see cref="CardMovedEvent"/> from Battlefield to Graveyard
///      for this specific card.
///   2. Applies an <see cref="TriggeredAbility.InterveningIf"/> check (CR 603.4)
///      that verifies <em>no</em> <see cref="CounterType.MinusOneMinusOne"/>
///      counters are present at resolution time.
///   3. On resolution moves Finks from Graveyard → Battlefield (same raw zone-
///      move used by UndyingFactory), clears the counter bag (CR 121.2 — counters
///      do not persist across zone changes), then adds exactly one
///      <see cref="CounterType.MinusOneMinusOne"/> counter.
///
/// <c>activeZones</c> for the Persist trigger is {Battlefield, Graveyard}:
/// Graveyard must be included so that the trigger evaluates while Finks is in
/// the graveyard (ZoneService sets the card's zone before publishing the event).
///
/// ## Comparison with Undying (CR 702.93)
/// Persist is the mirror of Undying — returns on death <em>without the
/// corresponding counter type</em> and adds one of that counter. Undying uses
/// <see cref="CounterType.PlusOnePlusOne"/> and fires when the creature has
/// zero +1/+1 counters; Persist uses <see cref="CounterType.MinusOneMinusOne"/>
/// and fires when the creature has zero -1/-1 counters.
/// </summary>
[CardName("Kitchen Finks")]
public static class KitchenFinksFactory
{
    public const string CardName = "Kitchen Finks";
    public const string PrintedManaCost = "{1}{G/W}{G/W}";
    public const int LifeGainAmount = 2;

    /// <summary>
    /// Construct Kitchen Finks owned and controlled by <paramref name="owner"/>.
    /// Both the ETB lifegain trigger and the Persist trigger are wired directly
    /// on the card; call <see cref="Majik.Core.Services.TriggerManager.BindCard"/>
    /// on the returned creature to register them with the live trigger manager so
    /// they fire on bus events.
    /// </summary>
    public static Creature Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: 3,
            toughness: 2,
            supertypes: Array.Empty<CardSupertype>(),
            subtypes: new[] { CardSubtype.Ouphe });

        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // ETB triggered ability — CR 603.6a + CR 119.3.
        //   "When Kitchen Finks enters the battlefield, you gain 2 life."
        // Fires on every ETB, including returns via Persist.
        // ----------------------------------------------------------------
        var etbCondition = new EventTriggerCondition<CardMovedEvent>(
            (e, _) => ReferenceEquals(e.Card, card) && e.ToZone == ZoneType.Battlefield);

        var etbEffect = new Effect(
            $"{CardName}: you gain {LifeGainAmount} life",
            () =>
            {
                var controller = card.Controller ?? owner;
                controller.GainLife(LifeGainAmount);
            });

        var etbTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: etbCondition,
            effects: new[] { etbEffect },
            interveningIf: null,
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(etbTrigger);

        // ----------------------------------------------------------------
        // Persist trigger — CR 702.78.
        //   "When this creature dies, if it had no -1/-1 counters on it,
        //    return it to the battlefield under its owner's control with a
        //    -1/-1 counter on it."
        //
        // Implementation mirrors UndyingFactory (CR 702.93) with the counter
        // polarity inverted: PlusOnePlusOne → MinusOneMinusOne.
        // ----------------------------------------------------------------
        var persistCondition = new EventTriggerCondition<CardMovedEvent>((e, _) =>
            ReferenceEquals(e.Card, card)
            && e.FromZone == ZoneType.Battlefield
            && e.ToZone == ZoneType.Graveyard);

        var persistEffect = new Effect("Persist — return to battlefield with -1/-1 counter", () =>
        {
            // Guard: Finks must still be in the graveyard at resolution time.
            // A replacement effect could have moved it elsewhere (unusual but correct).
            if (card.Zone != ZoneType.Graveyard) return;

            var cardOwner = card.Owner;
            if (cardOwner == null) return;

            // Move from graveyard to battlefield (CR 702.78).
            cardOwner.Zones.Graveyard.RemoveCard(card);
            cardOwner.Zones.Battlefield.AddCard(card);
            card.SetZone(ZoneType.Battlefield);
            card.SetController(cardOwner);

            // CR 121.2 — counters do not persist when a permanent changes zones.
            // Clear the bag so subsequent deaths accurately reflect the card's
            // counter state (i.e. the returned Finks enters with exactly one
            // -1/-1 counter, and a third death will correctly not trigger again).
            foreach (var entry in card.Counters.All.ToList())
            {
                card.Counters.Remove(entry.Key, entry.Value);
            }

            // Persist grant: one -1/-1 counter (CR 702.78).
            card.Counters.Add(CounterType.MinusOneMinusOne, 1);

            // Bookkeeping: re-mark battlefield entry timestamp / summoning-sickness
            // reset (same as UndyingFactory).
            card.MarkEnteredBattlefield();
        });

        // InterveningIf — CR 603.4 / CR 702.78: "if it had no -1/-1 counters."
        // Checked when the trigger would be put on the stack; counters survive
        // on the graveyard card object so this accurately reflects the state at
        // the moment of death.
        var persistTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: persistCondition,
            effects: new[] { persistEffect },
            interveningIf: () => card.Counters.Count(CounterType.MinusOneMinusOne) == 0,
            activeZones: new[] { ZoneType.Battlefield, ZoneType.Graveyard });

        card.AddAbility(persistTrigger);

        return card;
    }
}
