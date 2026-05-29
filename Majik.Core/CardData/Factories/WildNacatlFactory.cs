using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Wild Nacatl (Zendikar, {G}).
///
/// Creature — Cat Warrior 1/1. Oracle text:
///   "This creature gets +1/+1 as long as you control a Mountain.
///    This creature gets +1/+1 as long as you control a Plains."
///
/// ## Implemented (v1)
///
/// - 1/1 Creature — Cat Warrior at {G}, owner/controller wired.
/// - <b>Two independent conditional self-pumps (CR 613.7c — Layer 7c)</b>:
///   a single <see cref="LandSubtypeSelfPumpStaticEffect"/> registers
///   against the supplied <see cref="ContinuousEffectsService"/>. On every
///   <see cref="ContinuousEffectsService.Compute(Permanent)"/> for Wild
///   Nacatl the effect tests, independently, whether the controller
///   controls a Mountain and whether they control a Plains, applying
///   +1/+1 for each clause that holds. Both conditions re-evaluate
///   dynamically: a Mountain/Plains ETBing flips its bonus on, one
///   leaving (or being retyped off the subtype — Blood Moon, Spreading
///   Seas) flips it back off — no trigger / re-register cycle required.
///
/// Mirrors <see cref="ArdentRecruitFactory.MetalcraftSelfPumpStaticEffect"/>'s
/// Layer-7c conditional live-count posture, but swaps the single
/// "control ≥3 artifacts" threshold predicate for two independent
/// land-subtype-control predicates (<see cref="CardSubtype.Mountain"/> /
/// <see cref="CardSubtype.Plains"/>), each granting +1/+1.
///
/// ## "Control a Mountain / Plains" semantics (CR 109.5 / CR 305.6)
///
/// A "Mountain" is any permanent with the Mountain land subtype — basic
/// Mountains, dual/shock/triome lands printing Mountain, and any
/// permanent retyped to Mountain (Blood Moon turns every nonbasic into a
/// Mountain). The subtype source is the CR 613 layer pipeline, so the
/// predicate reads each land's <i>effective</i> subtypes via
/// <see cref="ContinuousEffectsService.Compute(Permanent)"/> when a live
/// service is supplied (mirrors <see cref="Majik.Core.Rules.Domain"/>'s
/// layer-aware posture); printed subtypes otherwise.
///
/// ## Lifecycle
///
/// ETB/LTB lifecycle mirrors
/// <see cref="ArdentRecruitFactory"/>: subscribe to
/// <see cref="CardMovedEvent"/>; register the static effect when Wild
/// Nacatl enters the battlefield, unregister when it leaves. The
/// <see cref="LandSubtypeSelfPumpStaticEffect.IsActive"/> battlefield
/// gate is belt-and-braces redundancy if no event bus is supplied.
///
/// ## Wiring overloads
///
/// - <see cref="Create(Player)"/> — shape only. No layer-7c effect; card
///   is structurally correct (1/1, Cat Warrior, owner/controller) but
///   the pumps don't fire without a continuous-effects service. Suitable
///   for shape / dispatcher tests.
/// - <see cref="Create(Player, ContinuousEffectsService?, IEventBus?)"/>
///   — fully wired. The pump registers on ETB and unregisters on LTB.
///
/// ## Deferred (v1 gaps)
///
/// - None card-specific. The two layer-7c bonuses stack above the
///   printed 1/1 base in the normal way (CR 613.4 layer 7), with no
///   order-of-operations gaps versus counters (Layer 7d) — a +1/+1
///   counter plus both clauses active puts Wild Nacatl at 4/4
///   (1 + 1 + 1 + 1).
/// </summary>
[CardName("Wild Nacatl")]
public static class WildNacatlFactory
{
    public const string CardName = "Wild Nacatl";
    public const string PrintedManaCost = "{G}";
    public const int Power = 1;
    public const int Toughness = 1;
    public const int ClauseBonus = 1;

    /// <summary>
    /// Construct Wild Nacatl with no live wiring. The conditional pumps
    /// are NOT attached (no continuous-effects service). Card shape
    /// (name, type, subtype, mana cost, P/T) is fully correct. Suitable
    /// for shape / dispatcher tests.
    /// </summary>
    public static Creature Create(Player owner)
        => Create(owner, effects: null, eventBus: null);

    /// <summary>
    /// Construct Wild Nacatl with optional runtime services. When
    /// <paramref name="effects"/> is supplied a
    /// <see cref="LandSubtypeSelfPumpStaticEffect"/> registers so the two
    /// +1/+1 conditional pumps are evaluated on every
    /// <see cref="ContinuousEffectsService.Compute"/> invocation. When
    /// <paramref name="eventBus"/> is also supplied the lifecycle binder
    /// subscribes to <see cref="CardMovedEvent"/> so the effect registers
    /// on ETB and unregisters on LTB (mirrors
    /// <see cref="ArdentRecruitFactory"/>'s lifecycle wiring).
    /// </summary>
    public static Creature Create(
        Player owner,
        ContinuousEffectsService? effects,
        IEventBus? eventBus)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            subtypes: new[] { CardSubtype.Cat, CardSubtype.Warrior });

        card.SetOwner(owner);
        card.SetController(owner);

        if (effects != null)
        {
            var lifecycle = new LandSubtypeLifecycle(card, effects, eventBus);
            lifecycle.Attach();
        }

        return card;
    }

    /// <summary>
    /// CR 109.5 / CR 305.6 — does the controller control a permanent with
    /// the given land subtype? Reads each battlefield permanent's
    /// <i>effective</i> subtypes from the CR 613 layer pipeline when
    /// <paramref name="effects"/> is supplied (so Blood Moon / Spreading
    /// Seas retypes feed through); printed subtypes otherwise. Mirrors
    /// <see cref="Majik.Core.Rules.Domain.CountTypes(Player, ContinuousEffectsService?)"/>'s
    /// layer-aware enumeration.
    /// </summary>
    public static bool ControlsLandSubtype(
        Player controller,
        CardSubtype subtype,
        ContinuousEffectsService? effects)
    {
        ArgumentNullException.ThrowIfNull(controller);

        foreach (var card in controller.Zones.Battlefield.GetCards())
        {
            if (card is not Permanent permanent) continue;

            IEnumerable<CardSubtype> subtypes = effects is not null
                ? effects.Compute(permanent).Subtypes
                : permanent.Subtypes;

            if (subtypes.Contains(subtype)) return true;
        }
        return false;
    }

    // -----------------------------------------------------------------------
    // LandSubtypeSelfPumpStaticEffect — two Layer 7c conditional self-pumps.
    // -----------------------------------------------------------------------

    /// <summary>
    /// Layer 7c continuous effect for Wild Nacatl's two conditional pumps.
    /// On every <see cref="ContinuousEffectsService.Compute"/> invocation
    /// the effect independently tests whether the controller controls a
    /// Mountain and whether they control a Plains, applying +1/+1 for each
    /// clause that holds (CR 613.7c — an "as long as" continuous effect
    /// gates its application on its predicate; it does not unregister, it
    /// contributes 0 when the predicate is false).
    ///
    /// Active only while Wild Nacatl is on the battlefield
    /// (<see cref="IsActive"/> gate — belt-and-braces alongside the
    /// ETB/LTB lifecycle wiring in <see cref="LandSubtypeLifecycle"/>).
    /// </summary>
    public sealed class LandSubtypeSelfPumpStaticEffect : ContinuousEffect
    {
        private readonly Creature _source;
        private readonly ContinuousEffectsService _effects;

        public LandSubtypeSelfPumpStaticEffect(
            Creature source,
            ContinuousEffectsService effects)
        {
            _source = source ?? throw new ArgumentNullException(nameof(source));
            _effects = effects ?? throw new ArgumentNullException(nameof(effects));
        }

        /// <summary>CR 613.1g — the permanent generating this effect.</summary>
        public override Permanent? Source => _source;

        /// <inheritdoc/>
        public override Layer Layer => Layer.PT_Modify;

        /// <summary>Active while Wild Nacatl is on the battlefield.</summary>
        public override bool IsActive() => _source.Zone == ZoneType.Battlefield;

        /// <summary>Applies only to Wild Nacatl itself.</summary>
        public override bool AppliesTo(Creature creature) =>
            ReferenceEquals(creature, _source);

        /// <summary>
        /// Apply +1/+1 for each independent clause that holds. Reads
        /// <see cref="Permanent.Controller"/> live so a control-changing
        /// effect on Wild Nacatl routes the land check through the new
        /// controller's battlefield (CR 109.5).
        /// </summary>
        public override void Apply(CreatureCharacteristics chars)
        {
            var controller = _source.Controller;
            if (controller == null) return;

            if (ControlsLandSubtype(controller, CardSubtype.Mountain, _effects))
            {
                chars.Power += ClauseBonus;
                chars.Toughness += ClauseBonus;
            }

            if (ControlsLandSubtype(controller, CardSubtype.Plains, _effects))
            {
                chars.Power += ClauseBonus;
                chars.Toughness += ClauseBonus;
            }
        }
    }

    // -----------------------------------------------------------------------
    // LandSubtypeLifecycle — ETB/LTB wiring for the conditional pump effect.
    // -----------------------------------------------------------------------

    /// <summary>
    /// ETB/LTB lifecycle binder for Wild Nacatl's conditional pumps.
    /// Subscribes to <see cref="CardMovedEvent"/>; registers
    /// <see cref="LandSubtypeSelfPumpStaticEffect"/> when Wild Nacatl
    /// enters the battlefield, unregisters when it leaves. Mirrors
    /// <see cref="ArdentRecruitFactory"/>'s lifecycle binder.
    /// </summary>
    private sealed class LandSubtypeLifecycle
    {
        private readonly Creature _source;
        private readonly ContinuousEffectsService _effects;
        private readonly IEventBus? _eventBus;
        private readonly Action<GameEvent> _handler;
        private LandSubtypeSelfPumpStaticEffect? _registered;
        private bool _attached;

        public LandSubtypeLifecycle(
            Creature source,
            ContinuousEffectsService effects,
            IEventBus? eventBus)
        {
            _source = source;
            _effects = effects;
            _eventBus = eventBus;
            _handler = OnEvent;
        }

        public void Attach()
        {
            if (_attached) return;
            _attached = true;
            _eventBus?.SubscribeAll(_handler);
            Sync();
        }

        private void OnEvent(GameEvent e)
        {
            if (e is not CardMovedEvent moved) return;
            if (!ReferenceEquals(moved.Card, _source)) return;
            Sync();
        }

        private void Sync()
        {
            var shouldBeActive = _source.Zone == ZoneType.Battlefield;
            if (shouldBeActive && _registered == null)
            {
                _registered = new LandSubtypeSelfPumpStaticEffect(_source, _effects);
                _effects.Register(_registered);
            }
            else if (!shouldBeActive && _registered != null)
            {
                _effects.Unregister(_registered);
                _registered = null;
            }
        }
    }
}
