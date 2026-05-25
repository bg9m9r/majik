using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Keywords;
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
/// - <b>Persist (CR 702.79)</b>: wired via the shared
///   <see cref="PersistFactory.Build(Creature)"/> primitive (promoted out of
///   this factory once Murderous Redcap + Glen Elendra Archmage joined the
///   roadmap). The primitive attaches the keyword marker + the
///   Battlefield → Graveyard death trigger with the "no -1/-1 counter"
///   interveningIf gate.
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
        // Fires on every ETB, including returns via Persist (when the
        // return is routed through ZoneService — the PersistFactory raw
        // zone-move does NOT republish CardMovedEvent, so this trigger
        // does not auto-fire on the in-effect return).
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
        // Persist (CR 702.79) — keyword marker + death trigger, all from
        // the shared primitive.
        // ----------------------------------------------------------------
        PersistFactory.Build(card);

        return card;
    }
}
