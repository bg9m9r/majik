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
/// Creature — Human Knight 2/2. Oracle text:
///   "Whenever an Equipment enters under your control, you may draw a card.
///    As long as you control three or more artifacts, Equipment you control
///    have equip {0}."
///
/// ## Implemented (v1)
///
/// - 2/2 Human Knight with mana cost {1}{W}.
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
/// ## Equip-cost integration
///
/// The zero-cost override is consumed by
/// <see cref="EquipActivatedAbility.CostProvider"/> at pay time —
/// <see cref="ZeroEquipCostProvider"/> is wired as the default
/// `costProvider` on every equipment factory's Equip ability. Activation
/// re-reads the registry each pay, so the override turns on and off live
/// as artifacts enter and leave the controller's battlefield.
///
/// ## Deferred (v1 gaps)
///
/// - <b>"You may" prompt</b>: the ETB-draw effect is unconditional. A
///   future agent prompt should gate the draw behind controller consent.
/// - <b>Static-ability attachment to <see cref="Card.Abilities"/></b>:
///   the zero-cost static isn't added as a printed
///   <see cref="StaticAbility"/> on the card object because there's no
///   layer-applied effect to register (no equip-cost primitive). The
///   lifecycle binder is the entire surface — the card carries the ETB
///   trigger only.
/// </summary>
[CardName("Puresteel Paladin")]
public static class PuresteelPaladinFactory
{
    public const string CardName = "Puresteel Paladin";
    public const string Cost = "{1}{W}";

    /// <summary>
    /// Cost-provider hook for <see cref="EquipActivatedAbility"/>: the shared
    /// equip-cost-modification seam consulted at activation / pay time. Layers
    /// two static cost effects (CR 117.7 / 702.6c) over the printed equip cost:
    ///
    /// <list type="number">
    ///   <item><description><b>Zero-equip override</b> — when any active
    ///   Puresteel-Paladin-style <see cref="ZeroEquipCostEffect"/> lifecycle
    ///   owns the equipment's CURRENT controller, the cost is floored to
    ///   <see cref="ManaCost.Zero"/> regardless of the printed amount
    ///   ("Equipment you control have equip {0}").</description></item>
    ///   <item><description><b>Per-target reduction</b> — otherwise, the
    ///   summed <see cref="EquipCostReductionEffect.ReductionForTarget"/> for
    ///   the equip ability's chosen target creature ("Equip abilities you
    ///   activate that target this creature cost {N} less" — Fervent Champion)
    ///   is subtracted from the printed <i>generic</i> portion (CR 117.7c —
    ///   coloured pips untouched, floor at zero).</description></item>
    /// </list>
    ///
    /// <para>
    /// Wired as the default <c>costProvider</c> on every retrofitted
    /// equipment factory; live game state alone gates whether either effect
    /// applies, so unequipped / Puresteel-less / Fervent-less boards see the
    /// printed cost.
    /// </para>
    /// </summary>
    public static Majik.Core.ValueObjects.ManaCost ZeroEquipCostProvider(Permanent source)
    {
        var ctrl = source.Controller ?? source.Owner;
        var equip = source.Abilities
            .OfType<EquipActivatedAbility>()
            .FirstOrDefault();
        var printed = equip?.EquipCost
            ?? Majik.Core.ValueObjects.ManaCost.Zero;

        // (1) Puresteel-style zero override takes precedence — equip {0}.
        if (ctrl != null && ZeroEquipCostEffect.IsZeroEquipActiveFor(ctrl))
            return Majik.Core.ValueObjects.ManaCost.Zero;

        // (2) Fervent-Champion-style per-target reduction (CR 117.7). The
        // reduction is keyed on the creature the equip ability targets, so we
        // read the chosen target off the equip ability hung on the source. No
        // chosen target (shape-only path / deterministic picker) → no
        // reduction (the registry can't know which creature is being equipped
        // until a target is chosen).
        var reduction = ChosenTargetReduction(equip);
        if (reduction <= 0) return printed;

        var newGeneric = Math.Max(0, printed.Generic - reduction);
        return printed.WithGeneric(newGeneric);
    }

    /// <summary>
    /// Sum the <see cref="EquipCostReductionEffect"/> reductions registered for
    /// the creature the equip ability currently targets. Returns 0 when no
    /// equip ability, no chosen target, or no registered reducer applies.
    /// </summary>
    private static int ChosenTargetReduction(EquipActivatedAbility? equip)
    {
        if (equip == null) return 0;
        if (equip.ChosenTargets.Count == 0) return 0;
        if (equip.ChosenTargets[0].Count == 0) return 0;
        if (equip.ChosenTargets[0][0] is not Creature target) return 0;
        return EquipCostReductionEffect.ReductionForTarget(target);
    }

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
            subtypes: new[] { CardSubtype.Human, CardSubtype.Knight });

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
