using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Puresteel Paladin (New Phyrexia, {1}{W}).
///
/// Creature — Human Soldier 2/2. Oracle text:
///   "Whenever an Equipment enters under your control, you may draw a card.
///    As long as you control three or more artifacts, Equipment you control
///    have equip {0}."
///
/// ## Implemented (v1)
///
/// - 2/2 Human Soldier with mana cost {1}{W}.
/// - <b>ETB-draw trigger (CR 603.1)</b>: a <see cref="TriggeredAbility"/> over
///   <see cref="CardMovedEvent"/> matches when any <see cref="CardType.Artifact"/>
///   carrying the <see cref="CardSubtype.Equipment"/> subtype enters the
///   battlefield AND its controller is Puresteel's controller. Effect:
///   draw a card (top of controller's library → hand). The trigger fires
///   for the controller's own Equipment plays as well as for any other
///   move that lands an Equipment under their control (e.g. Stoneforge
///   Mystic's activated ability puts an Equipment from hand onto the
///   battlefield — the trigger sees that move and offers the draw).
///   The printed wording is "you may", which v1 models as an unconditional
///   draw at the resolution step; the "may" gate would normally be a
///   controller agent prompt — same simplification as Up the Beanstalk's
///   ETB draw (a forced draw is strictly stronger so this is observationally
///   correct against any rational agent).
/// - <b>Zero-equip static (CR 604.2 / 613.1f)</b>: a
///   <see cref="ZeroEquipCostEffect"/> lifecycle binder is attached when
///   the factory is constructed with an event bus. While Puresteel is on
///   the battlefield AND its controller has ≥3 artifacts on the
///   battlefield, <see cref="ZeroEquipCostEffect.IsZeroEquipActiveFor(Player)"/>
///   returns <c>true</c> for the controller. The threshold is read live
///   from the controller's battlefield zone (artifacts only — Puresteel
///   itself is a Creature, not an Artifact, so it never counts toward its
///   own threshold). Opponents' artifacts do not count.
///
/// ## Deferred (v1 gaps)
///
/// - <b>Equip-ability primitive</b>: the engine has no
///   <c>EquipActivatedAbility</c> primitive yet — Equipment cards
///   currently don't model their printed "Equip {N}" activated ability at
///   all (Stoneforge Mystic's activated ability is a separate
///   "put-an-Equipment-from-hand" effect, not an equip activation). The
///   zero-cost override is therefore wired as a query-side registry
///   (<see cref="ZeroEquipCostEffect.IsZeroEquipActiveFor(Player)"/>);
///   when an <c>EquipActivatedAbility</c> primitive lands, it should
///   consult that query at cost-resolution time. The factory and tests
///   don't change.
/// - <b>"You may" prompt</b>: the ETB-draw effect is unconditional. A
///   future agent prompt should gate the draw behind controller consent.
/// - <b>Static-ability attachment to <see cref="Card.Abilities"/></b>:
///   the zero-cost static isn't added as a printed
///   <see cref="StaticAbility"/> on the card object because there's no
///   layer-applied effect to register (no equip-cost primitive). The
///   lifecycle binder is the entire surface — the card carries the ETB
///   trigger only.
/// </summary>
public static class PuresteelPaladinFactory
{
    public const string CardName = "Puresteel Paladin";
    public const string Cost = "{1}{W}";

    /// <summary>
    /// Construct Puresteel Paladin with no live event-bus / trigger-manager
    /// wiring. The ETB-draw trigger is attached to the card so structural
    /// tests can observe it; the zero-equip-cost lifecycle is constructed
    /// but not auto-attached (callers building a fully-wired game should
    /// use the overload with an event bus). Suitable for shape / dispatcher
    /// tests.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, eventBus: null, triggers: null);

    /// <summary>
    /// Construct Puresteel Paladin with optional runtime services.
    /// When <paramref name="triggers"/> is supplied, the ETB-draw trigger
    /// is registered with the manager. When <paramref name="eventBus"/>
    /// is supplied, the zero-equip-cost lifecycle is attached so the
    /// registry tracks Puresteel's ETB / LTB automatically.
    /// </summary>
    public static Creature Create(
        Player owner,
        IEventBus? eventBus,
        TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: Cost,
            power: 2,
            toughness: 2,
            subtypes: new[] { CardSubtype.Human, CardSubtype.Soldier });

        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // Triggered ability — "Whenever an Equipment enters under your
        // control, you may draw a card." (CR 603.1)
        // ----------------------------------------------------------------
        var equipmentEtbCondition = new EventTriggerCondition<CardMovedEvent>((e, _) =>
            e.ToZone == ZoneType.Battlefield
            && e.Card.HasType(CardType.Artifact)
            && e.Card.HasSubtype(CardSubtype.Equipment)
            && ReferenceEquals(e.Card.Controller, owner));

        var drawEffect = new Effect(
            "Puresteel Paladin — may draw a card when an Equipment enters under your control",
            () =>
            {
                // v1: forced draw (no agent prompt — see class xmldoc).
                // Mirrors UpTheBeanstalkFactory.DrawOne.
                var top = owner.Zones.Library.GetCards().FirstOrDefault();
                if (top == null) return;
                owner.Zones.Library.RemoveCard(top);
                owner.Zones.Hand.AddCard(top);
                top.SetZone(ZoneType.Hand);
            });

        var drawTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: equipmentEtbCondition,
            effects: new IEffect[] { drawEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(drawTrigger);
        triggers?.RegisterTriggeredAbility(drawTrigger);

        // ----------------------------------------------------------------
        // Static ability — "As long as you control three or more artifacts,
        // Equipment you control have equip {0}." (CR 604.2 / 613.1f /
        // 702.6c)
        //
        // No EquipActivatedAbility primitive exists yet (see class xmldoc),
        // so the static is registered as a query-side lifecycle that any
        // future equip-cost consumer can consult via
        // ZeroEquipCostEffect.IsZeroEquipActiveFor(controller). Attached
        // here regardless of eventBus presence — without a bus it still
        // works for tests that manually move the card onto the battlefield
        // and call Attach() once (the Sync inside Attach() picks up the
        // current zone).
        // ----------------------------------------------------------------
        if (eventBus != null)
        {
            var zeroCost = new ZeroEquipCostEffect(
                source: card,
                controller: owner,
                eventBus: eventBus);
            zeroCost.Attach();
        }

        return card;
    }
}
