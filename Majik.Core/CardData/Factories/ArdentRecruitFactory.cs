using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Ardent Recruit (Scars of Mirrodin, {W}).
///
/// Creature — Human Soldier 1/1. Oracle text:
///   "Metalcraft — Ardent Recruit gets +2/+2 as long as you control three
///    or more artifacts."
///
/// ## Implemented (v1)
///
/// - 1/1 Creature — Human Soldier at {W}, owner/controller wired.
/// - <b>Metalcraft conditional pump (CR 613.7c — Layer 7c)</b>: a
///   <see cref="MetalcraftSelfPumpStaticEffect"/> registers against the
///   supplied <see cref="ContinuousEffectsService"/>. On every
///   <see cref="ContinuousEffectsService.Compute(Permanent)"/> for Ardent
///   Recruit the effect counts the controller's battlefield artifacts and
///   applies +2/+2 when the count is ≥3. The condition re-evaluates
///   dynamically: a third artifact ETBing flips the bonus on, an artifact
///   leaving (or being retyped off Artifact) flips it back off — no
///   trigger / re-register cycle required.
///
/// Mirrors <see cref="TerritorialKavuFactory.DomainPumpStaticEffect"/>'s
/// live-count posture (Layer 7c, AppliesTo == self, re-counts on every
/// Compute), but gated on a threshold predicate rather than a continuous
/// per-type scalar.
///
/// ## Lifecycle
///
/// ETB/LTB lifecycle mirrors
/// <see cref="TerritorialKavuFactory"/>: subscribe to
/// <see cref="CardMovedEvent"/>; register the static effect when Ardent
/// Recruit enters the battlefield, unregister when it leaves. The
/// <see cref="MetalcraftSelfPumpStaticEffect.IsActive"/> battlefield
/// gate is belt-and-braces redundancy if no event bus is supplied.
///
/// ## Wiring overloads
///
/// - <see cref="Create(Player)"/> — shape only. No layer-7c effect; card
///   is structurally correct (1/1, Human Soldier, owner/controller) but
///   the Metalcraft pump doesn't fire without a continuous-effects
///   service. Suitable for shape / dispatcher tests.
/// - <see cref="Create(Player, ContinuousEffectsService?, IEventBus?)"/>
///   — fully wired. The pump registers on ETB and unregisters on LTB.
///
/// ## Metalcraft semantics (CR 702.91 — historical / oracle)
///
/// The official oracle text shipped Ardent Recruit without a named
/// "Metalcraft" keyword ability (Metalcraft is an ability word — CR 207.2c
/// — that has no rules meaning beyond linking the condition to the rest
/// of the printed text). So this factory does NOT register a Metalcraft
/// keyword marker; the gameplay shape is entirely the conditional pump.
///
/// ## Artifact-count semantics (CR 109.5)
///
/// "Artifacts you control" tallies every permanent the controller controls
/// with <see cref="CardType.Artifact"/> in its type list — includes
/// artifact creatures, artifact lands, artifact enchantments, and token
/// artifacts. Mirrors <see cref="MasterOfEtheriumFactory.CountArtifactsControlled"/>'s
/// posture (which Ardent Recruit's count helper directly delegates to —
/// the count is identical and shared).
///
/// ## Deferred (v1 gaps)
///
/// - <b>Order-sensitive layer interactions</b>: the layer-7c bonus stacks
///   above the printed 1/1 base in the normal way; no order-of-operations
///   gaps versus CDAs (Layer 7a) or counters (Layer 7d). A +1/+1 counter
///   on Ardent Recruit combined with active Metalcraft puts it at 4/4
///   (1 + 2 + 1 / 1 + 2 + 1).
/// </summary>
[CardName("Ardent Recruit")]
public static class ArdentRecruitFactory
{
    public const string CardName = "Ardent Recruit";
    public const string PrintedManaCost = "{W}";
    public const int Power = 1;
    public const int Toughness = 1;
    public const int MetalcraftThreshold = 3;
    public const int MetalcraftBonus = 2;

    /// <summary>
    /// Construct Ardent Recruit with no live wiring. The Metalcraft
    /// conditional pump is NOT attached (no continuous-effects service).
    /// Card shape (name, type, subtype, mana cost, P/T) is fully correct.
    /// Suitable for shape / dispatcher tests.
    /// </summary>
    public static Creature Create(Player owner)
        => Create(owner, effects: null, eventBus: null);

    /// <summary>
    /// Construct Ardent Recruit with optional runtime services. When
    /// <paramref name="effects"/> is supplied a
    /// <see cref="MetalcraftSelfPumpStaticEffect"/> registers so the
    /// +2/+2 conditional pump is evaluated on every
    /// <see cref="ContinuousEffectsService.Compute"/> invocation. When
    /// <paramref name="eventBus"/> is also supplied the lifecycle binder
    /// subscribes to <see cref="CardMovedEvent"/> so the effect
    /// registers on ETB and unregisters on LTB (mirrors
    /// <see cref="TerritorialKavuFactory"/>'s lifecycle wiring).
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
            subtypes: new[] { CardSubtype.Human, CardSubtype.Soldier });

        card.SetOwner(owner);
        card.SetController(owner);

        if (effects != null)
        {
            var lifecycle = new MetalcraftLifecycle(card, effects, eventBus);
            lifecycle.Attach();
        }

        return card;
    }

    /// <summary>
    /// CR 109.5 — "you control" reads each permanent's current controller.
    /// Includes token artifacts and Ardent Recruit itself if Ardent
    /// Recruit is somehow an artifact (it isn't printed as one, but the
    /// gate stays robust against Liquimetal Coating-style retypes).
    /// Delegates to <see cref="MasterOfEtheriumFactory.CountArtifactsControlled"/>
    /// so a single count function services every "artifacts you control"
    /// gate in the engine.
    /// </summary>
    public static int CountArtifactsControlled(Player controller)
        => MasterOfEtheriumFactory.CountArtifactsControlled(controller);

    /// <summary>
    /// CR 207.2c — "Metalcraft — As long as you control three or more
    /// artifacts." Pure-helper predicate; mirrors the closure baked into
    /// the live <see cref="MetalcraftSelfPumpStaticEffect"/>.
    /// </summary>
    public static bool MetalcraftActive(Player controller)
        => CountArtifactsControlled(controller) >= MetalcraftThreshold;

    // -----------------------------------------------------------------------
    // MetalcraftSelfPumpStaticEffect — Layer 7c conditional self-pump.
    // -----------------------------------------------------------------------

    /// <summary>
    /// Layer 7c continuous effect for Ardent Recruit's Metalcraft pump.
    /// On every <see cref="ContinuousEffectsService.Compute"/> invocation
    /// the effect tests whether the controller controls ≥3 artifacts and,
    /// if so, applies +2/+2 to Ardent Recruit. Below the threshold the
    /// effect contributes nothing (CR 613.7c — a continuous effect that
    /// reads "as long as" gates its application on the predicate; it does
    /// not unregister, but its <see cref="AppliesTo"/> returns true and
    /// <see cref="Apply"/> contributes 0 when the predicate is false).
    ///
    /// Active only while Ardent Recruit is on the battlefield
    /// (<see cref="IsActive"/> gate — belt-and-braces alongside the
    /// ETB/LTB lifecycle wiring in <see cref="MetalcraftLifecycle"/>).
    /// </summary>
    public sealed class MetalcraftSelfPumpStaticEffect : ContinuousEffect
    {
        private readonly Creature _source;

        public MetalcraftSelfPumpStaticEffect(Creature source)
        {
            _source = source ?? throw new ArgumentNullException(nameof(source));
        }

        /// <summary>CR 613.1g — the permanent generating this effect.</summary>
        public override Permanent? Source => _source;

        /// <inheritdoc/>
        public override Layer Layer => Layer.PT_Modify;

        /// <summary>Active while Ardent Recruit is on the battlefield.</summary>
        public override bool IsActive() => _source.Zone == ZoneType.Battlefield;

        /// <summary>Applies only to Ardent Recruit itself.</summary>
        public override bool AppliesTo(Creature creature) =>
            ReferenceEquals(creature, _source);

        /// <summary>
        /// Apply +2/+2 when the controller controls ≥3 artifacts;
        /// otherwise no contribution. Reads <see cref="Permanent.Controller"/>
        /// live so a control-changing effect on Ardent Recruit routes the
        /// count through the new controller's battlefield (CR 109.5).
        /// </summary>
        public override void Apply(CreatureCharacteristics chars)
        {
            var controller = _source.Controller;
            if (controller == null) return;
            if (!MetalcraftActive(controller)) return;
            chars.Power += MetalcraftBonus;
            chars.Toughness += MetalcraftBonus;
        }
    }

    // -----------------------------------------------------------------------
    // MetalcraftLifecycle — ETB/LTB wiring for the Metalcraft pump effect.
    // -----------------------------------------------------------------------

    /// <summary>
    /// ETB/LTB lifecycle binder for Ardent Recruit's Metalcraft pump.
    /// Subscribes to <see cref="CardMovedEvent"/>; registers
    /// <see cref="MetalcraftSelfPumpStaticEffect"/> when Ardent Recruit
    /// enters the battlefield, unregisters when it leaves. Mirrors
    /// <see cref="TerritorialKavuFactory"/>'s
    /// <c>DomainPumpLifecycle</c>.
    /// </summary>
    private sealed class MetalcraftLifecycle
    {
        private readonly Creature _source;
        private readonly ContinuousEffectsService _effects;
        private readonly IEventBus? _eventBus;
        private readonly Action<GameEvent> _handler;
        private MetalcraftSelfPumpStaticEffect? _registered;
        private bool _attached;

        public MetalcraftLifecycle(
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
                _registered = new MetalcraftSelfPumpStaticEffect(_source);
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
