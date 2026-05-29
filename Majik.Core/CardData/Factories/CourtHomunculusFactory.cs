using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Court Homunculus (Conflux, {W}).
///
/// Artifact Creature — Homunculus 1/1. Oracle text:
///   "This creature gets +1/+1 as long as you control another artifact."
///
/// ## Implemented (v1)
///
/// - 1/1 Artifact Creature — Homunculus at {W}, owner/controller wired.
/// - <b>Conditional self-pump (CR 613.7c — Layer 7c)</b>: a
///   <see cref="ConditionalSelfPumpStaticEffect"/> registers against the
///   supplied <see cref="ContinuousEffectsService"/>. On every
///   <see cref="ContinuousEffectsService.Compute(Permanent)"/> for Court
///   Homunculus the effect tests whether the controller controls another
///   artifact and, if so, applies +1/+1. The condition re-evaluates
///   dynamically: another artifact ETBing flips the bonus on, that
///   artifact leaving (or being retyped off Artifact) flips it back off —
///   no trigger / re-register cycle required.
///
/// Mirrors <see cref="ArdentRecruitFactory"/>'s Metalcraft self-pump
/// (Layer 7c, AppliesTo == self, re-counts on every Compute), but gated on
/// a simpler "one OTHER artifact" predicate rather than a ≥3 threshold.
///
/// ## "Another artifact" semantics (CR 109.5 / CR 201.4)
///
/// Court Homunculus is itself an artifact. "another artifact" excludes the
/// object itself, so the predicate counts artifacts the controller
/// controls OTHER than Court Homunculus and requires ≥1. Reusing
/// <see cref="MasterOfEtheriumFactory.CountArtifactsControlled"/> (which
/// includes self) and subtracting the source when the source is an
/// on-battlefield artifact keeps a single count function servicing every
/// "artifacts you control" gate in the engine.
///
/// ## Lifecycle
///
/// ETB/LTB lifecycle mirrors <see cref="ArdentRecruitFactory"/>: subscribe
/// to <see cref="CardMovedEvent"/>; register the static effect when Court
/// Homunculus enters the battlefield, unregister when it leaves.
///
/// ## Wiring overloads
///
/// - <see cref="Create(Player)"/> — shape only. No Layer-7c effect; card is
///   structurally correct (1/1 Artifact Creature — Homunculus,
///   owner/controller) but the pump doesn't fire without a continuous-
///   effects service. Suitable for shape / dispatcher tests.
/// - <see cref="Create(Player, ContinuousEffectsService?, IEventBus?)"/> —
///   fully wired. The pump registers on ETB and unregisters on LTB.
///
/// ## Deferred (v1 gaps)
///
/// - <b>Order-sensitive layer interactions</b>: the Layer-7c bonus stacks
///   above the printed 1/1 base normally; no order-of-operations gaps
///   versus CDAs (Layer 7a) or counters (Layer 7d).
/// </summary>
[CardName("Court Homunculus")]
public static class CourtHomunculusFactory
{
    public const string CardName = "Court Homunculus";
    public const string PrintedManaCost = "{W}";
    public const int Power = 1;
    public const int Toughness = 1;
    public const int Bonus = 1;

    /// <summary>
    /// Construct Court Homunculus with no live wiring. The conditional
    /// pump is NOT attached (no continuous-effects service). Card shape
    /// (name, types, subtype, mana cost, P/T) is fully correct. Suitable
    /// for shape / dispatcher tests.
    /// </summary>
    public static Creature Create(Player owner)
        => Create(owner, effects: null, eventBus: null);

    /// <summary>
    /// Construct Court Homunculus with optional runtime services. When
    /// <paramref name="effects"/> is supplied a
    /// <see cref="ConditionalSelfPumpStaticEffect"/> registers so the
    /// +1/+1 conditional pump is evaluated on every
    /// <see cref="ContinuousEffectsService.Compute"/> invocation. When
    /// <paramref name="eventBus"/> is also supplied the lifecycle binder
    /// subscribes to <see cref="CardMovedEvent"/> so the effect registers
    /// on ETB and unregisters on LTB.
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
            subtypes: new[] { CardSubtype.Homunculus });

        // Artifact Creature — the printed type line carries both Artifact
        // and Creature (CR 301 / 302). Creature is the primary runtime
        // class; layer the Artifact type on so "artifacts you control"
        // gates (including this card's own) see it.
        card.AddCardType(CardType.Artifact);

        card.SetOwner(owner);
        card.SetController(owner);

        if (effects != null)
        {
            var lifecycle = new ConditionalPumpLifecycle(card, effects, eventBus);
            lifecycle.Attach();
        }

        return card;
    }

    /// <summary>
    /// CR 109.5 / CR 201.4 — "another artifact" reads each permanent's
    /// current controller and excludes <paramref name="source"/> itself.
    /// Scans the controller's battlefield for any artifact permanent that
    /// is NOT the source (reference inequality), so the predicate is true
    /// the moment a second artifact is under the controller's control.
    /// Identity-based exclusion (rather than "count − 1") is robust whether
    /// or not the source is materialised in the controller's battlefield
    /// collection — only its identity matters.
    /// </summary>
    public static bool ControlsAnotherArtifact(Player controller, Permanent source)
    {
        ArgumentNullException.ThrowIfNull(controller);
        ArgumentNullException.ThrowIfNull(source);

        foreach (var c in controller.Zones.Battlefield.GetCards())
        {
            if (ReferenceEquals(c, source)) continue;
            if (c.HasType(CardType.Artifact)) return true;
        }
        return false;
    }

    // -----------------------------------------------------------------------
    // ConditionalSelfPumpStaticEffect — Layer 7c conditional self-pump.
    // -----------------------------------------------------------------------

    /// <summary>
    /// Layer 7c continuous effect for Court Homunculus's conditional pump.
    /// On every <see cref="ContinuousEffectsService.Compute"/> invocation
    /// the effect tests whether the controller controls another artifact
    /// and, if so, applies +1/+1 (CR 613.7c — a continuous "as long as"
    /// effect gates application on the predicate; it does not unregister,
    /// it contributes 0 when the predicate is false).
    ///
    /// Active only while Court Homunculus is on the battlefield
    /// (<see cref="IsActive"/> gate — belt-and-braces alongside the ETB/LTB
    /// lifecycle wiring in <see cref="ConditionalPumpLifecycle"/>).
    /// </summary>
    public sealed class ConditionalSelfPumpStaticEffect : ContinuousEffect
    {
        private readonly Creature _source;

        public ConditionalSelfPumpStaticEffect(Creature source)
        {
            _source = source ?? throw new ArgumentNullException(nameof(source));
        }

        /// <summary>CR 613.1g — the permanent generating this effect.</summary>
        public override Permanent? Source => _source;

        /// <inheritdoc/>
        public override Layer Layer => Layer.PT_Modify;

        /// <summary>Active while Court Homunculus is on the battlefield.</summary>
        public override bool IsActive() => _source.Zone == ZoneType.Battlefield;

        /// <summary>Applies only to Court Homunculus itself.</summary>
        public override bool AppliesTo(Creature creature) =>
            ReferenceEquals(creature, _source);

        /// <summary>
        /// Apply +1/+1 when the controller controls another artifact;
        /// otherwise no contribution. Reads <see cref="Permanent.Controller"/>
        /// live so a control-changing effect routes the count through the
        /// new controller's battlefield (CR 109.5).
        /// </summary>
        public override void Apply(CreatureCharacteristics chars)
        {
            var controller = _source.Controller;
            if (controller == null) return;
            if (!ControlsAnotherArtifact(controller, _source)) return;
            chars.Power += Bonus;
            chars.Toughness += Bonus;
        }
    }

    // -----------------------------------------------------------------------
    // ConditionalPumpLifecycle — ETB/LTB wiring for the pump effect.
    // -----------------------------------------------------------------------

    /// <summary>
    /// ETB/LTB lifecycle binder for Court Homunculus's conditional pump.
    /// Subscribes to <see cref="CardMovedEvent"/>; registers
    /// <see cref="ConditionalSelfPumpStaticEffect"/> when Court Homunculus
    /// enters the battlefield, unregisters when it leaves. Mirrors
    /// <see cref="ArdentRecruitFactory"/>'s lifecycle binder.
    /// </summary>
    private sealed class ConditionalPumpLifecycle
    {
        private readonly Creature _source;
        private readonly ContinuousEffectsService _effects;
        private readonly IEventBus? _eventBus;
        private readonly Action<GameEvent> _handler;
        private ConditionalSelfPumpStaticEffect? _registered;
        private bool _attached;

        public ConditionalPumpLifecycle(
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
                _registered = new ConditionalSelfPumpStaticEffect(_source);
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
