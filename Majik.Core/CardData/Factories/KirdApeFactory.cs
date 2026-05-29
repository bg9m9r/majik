using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Kird Ape (Arabian Nights / Alpha-era reprints,
/// {R}). Creature — Ape 1/1. Oracle text (verified against Scryfall):
///   "This creature gets +1/+2 as long as you control a Forest."
///
/// Base shape (name, Creature, Ape subtype, {R}, 1/1) is materialised from
/// the embedded JSON definition (<c>kird-ape.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>. The conditional self-pump is
/// layered on top here — the JSON <c>AbilityDefinition</c> schema doesn't
/// yet express "as long as you control a land subtype" statics, so it lives
/// in the factory (same posture as <see cref="ArdentRecruitFactory"/>'s
/// Metalcraft self-pump and <see cref="StormscaleScionFactory"/>'s layered
/// behaviours).
///
/// ## Implemented (v1)
///
/// - 1/1 Creature — Ape at {R}, owner/controller wired (from JSON).
/// - <b>Forest-conditional pump (CR 613.7c — Layer 7c)</b>: a
///   <see cref="ForestSelfPumpStaticEffect"/> registers against the
///   supplied <see cref="ContinuousEffectsService"/>. On every
///   <see cref="ContinuousEffectsService.Compute(Permanent)"/> for Kird Ape
///   the effect tests whether the controller controls at least one Forest
///   and applies +1/+2 when so. The condition re-evaluates dynamically: a
///   Forest ETBing flips the bonus on, the last Forest leaving (or being
///   retyped off Forest) flips it back off — no trigger / re-register cycle
///   required. Directly mirrors
///   <see cref="ArdentRecruitFactory.MetalcraftSelfPumpStaticEffect"/>'s
///   live-count posture; the only differences are the predicate
///   ("control a Forest" rather than "control ≥3 artifacts") and the bonus
///   (+1/+2 rather than +2/+2).
///
/// ## Lifecycle
///
/// ETB/LTB lifecycle mirrors <see cref="ArdentRecruitFactory"/>: subscribe
/// to <see cref="CardMovedEvent"/>; register the static effect when Kird
/// Ape enters the battlefield, unregister when it leaves. The
/// <see cref="ForestSelfPumpStaticEffect.IsActive"/> battlefield gate is
/// belt-and-braces redundancy if no event bus is supplied.
///
/// ## Wiring overloads
///
/// - <see cref="Create(Player)"/> — shape only. No layer-7c effect; card is
///   structurally correct (1/1, Ape, owner/controller) but the Forest pump
///   doesn't fire without a continuous-effects service. This is the
///   overload <see cref="NamedCardFactory"/> dispatches to.
/// - <see cref="Create(Player, ContinuousEffectsService?, IEventBus?)"/> —
///   fully wired. The pump registers on ETB and unregisters on LTB.
///
/// ## Forest-count semantics (CR 109.5, CR 305.6)
///
/// "Control a Forest" reads true when the controller controls at least one
/// permanent with the <see cref="CardSubtype.Forest"/> land subtype —
/// includes basic Forests, dual lands typed Forest, and any non-land
/// permanent retyped to Forest (CR 305.6 — a subtype only has meaning on a
/// land; the predicate stays robust by testing the subtype directly).
/// </summary>
[CardName("Kird Ape")]
public static class KirdApeFactory
{
    public const string CardName = "Kird Ape";
    public const string Slug = "kird-ape";
    public const int Power = 1;
    public const int Toughness = 1;
    public const int ForestBonusPower = 1;
    public const int ForestBonusToughness = 2;

    /// <summary>
    /// Construct Kird Ape with no live wiring. The Forest conditional pump
    /// is NOT attached (no continuous-effects service). Card shape (name,
    /// type, subtype, mana cost, P/T) is fully correct. This is the overload
    /// <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Creature Create(Player owner)
        => Create(owner, effects: null, eventBus: null);

    /// <summary>
    /// Construct Kird Ape with optional runtime services. When
    /// <paramref name="effects"/> is supplied a
    /// <see cref="ForestSelfPumpStaticEffect"/> registers so the +1/+2
    /// conditional pump is evaluated on every
    /// <see cref="ContinuousEffectsService.Compute"/> invocation. When
    /// <paramref name="eventBus"/> is also supplied the lifecycle binder
    /// subscribes to <see cref="CardMovedEvent"/> so the effect registers on
    /// ETB and unregisters on LTB (mirrors
    /// <see cref="ArdentRecruitFactory"/>'s lifecycle wiring).
    /// </summary>
    public static Creature Create(
        Player owner,
        ContinuousEffectsService? effects,
        IEventBus? eventBus)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Creature, Ape
        // subtype, {R}, 1/1). The JSON carries no abilities — the Forest
        // pump is layered on below.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Creature)CardDefinitionFactory.Build(definition, owner);

        if (effects != null)
        {
            var lifecycle = new ForestPumpLifecycle(card, effects, eventBus);
            lifecycle.Attach();
        }

        return card;
    }

    /// <summary>
    /// CR 109.5 — "you control" reads each permanent's current controller.
    /// True when the controller controls at least one permanent with the
    /// Forest land subtype.
    /// </summary>
    public static bool ControlsForest(Player controller)
    {
        ArgumentNullException.ThrowIfNull(controller);
        foreach (var c in controller.Zones.Battlefield.GetCards())
        {
            if (c.HasSubtype(CardSubtype.Forest)) return true;
        }
        return false;
    }

    // -----------------------------------------------------------------------
    // ForestSelfPumpStaticEffect — Layer 7c conditional self-pump.
    // -----------------------------------------------------------------------

    /// <summary>
    /// Layer 7c continuous effect for Kird Ape's Forest pump. On every
    /// <see cref="ContinuousEffectsService.Compute"/> invocation the effect
    /// tests whether the controller controls a Forest and, if so, applies
    /// +1/+2 to Kird Ape. Without a Forest the effect contributes nothing
    /// (CR 613.7c — a continuous effect that reads "as long as" gates its
    /// application on the predicate; it does not unregister, but its
    /// <see cref="AppliesTo"/> returns true and <see cref="Apply"/>
    /// contributes 0 when the predicate is false).
    ///
    /// Active only while Kird Ape is on the battlefield
    /// (<see cref="IsActive"/> gate — belt-and-braces alongside the ETB/LTB
    /// lifecycle wiring in <see cref="ForestPumpLifecycle"/>).
    /// </summary>
    public sealed class ForestSelfPumpStaticEffect : ContinuousEffect
    {
        private readonly Creature _source;

        public ForestSelfPumpStaticEffect(Creature source)
        {
            _source = source ?? throw new ArgumentNullException(nameof(source));
        }

        /// <summary>CR 613.1g — the permanent generating this effect.</summary>
        public override Permanent? Source => _source;

        /// <inheritdoc/>
        public override Layer Layer => Layer.PT_Modify;

        /// <summary>Active while Kird Ape is on the battlefield.</summary>
        public override bool IsActive() => _source.Zone == ZoneType.Battlefield;

        /// <summary>Applies only to Kird Ape itself.</summary>
        public override bool AppliesTo(Creature creature) =>
            ReferenceEquals(creature, _source);

        /// <summary>
        /// Apply +1/+2 when the controller controls a Forest; otherwise no
        /// contribution. Reads <see cref="Permanent.Controller"/> live so a
        /// control-changing effect on Kird Ape routes the Forest check
        /// through the new controller's battlefield (CR 109.5).
        /// </summary>
        public override void Apply(CreatureCharacteristics chars)
        {
            var controller = _source.Controller;
            if (controller == null) return;
            if (!ControlsForest(controller)) return;
            chars.Power += ForestBonusPower;
            chars.Toughness += ForestBonusToughness;
        }
    }

    // -----------------------------------------------------------------------
    // ForestPumpLifecycle — ETB/LTB wiring for the Forest pump effect.
    // -----------------------------------------------------------------------

    /// <summary>
    /// ETB/LTB lifecycle binder for Kird Ape's Forest pump. Subscribes to
    /// <see cref="CardMovedEvent"/>; registers
    /// <see cref="ForestSelfPumpStaticEffect"/> when Kird Ape enters the
    /// battlefield, unregisters when it leaves. Mirrors
    /// <see cref="ArdentRecruitFactory"/>'s <c>MetalcraftLifecycle</c>.
    /// </summary>
    private sealed class ForestPumpLifecycle
    {
        private readonly Creature _source;
        private readonly ContinuousEffectsService _effects;
        private readonly IEventBus? _eventBus;
        private readonly Action<GameEvent> _handler;
        private ForestSelfPumpStaticEffect? _registered;
        private bool _attached;

        public ForestPumpLifecycle(
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
                _registered = new ForestSelfPumpStaticEffect(_source);
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
