using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Loam Lion (Worldwake / reprints, {W}). Creature —
/// Cat 1/1. Oracle text (verified against Scryfall 2026-05):
///   "This creature gets +1/+2 as long as you control a Forest."
///
/// Mechanically identical to <see cref="KirdApeFactory"/> (Kird Ape) — same
/// "+1/+2 as long as you control a Forest" conditional self-pump; the only
/// differences are name, color ({W} rather than {R}) and subtype (Cat rather
/// than Ape). This factory mirrors that implementation exactly.
///
/// Base shape (name, Creature, Cat subtype, {W}, 1/1) is materialised from
/// the embedded JSON definition (<c>loam-lion.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>. The conditional self-pump is
/// layered on top here — the JSON <c>AbilityDefinition</c> schema doesn't yet
/// express "as long as you control a land subtype" statics, so it lives in
/// the factory (same posture as <see cref="KirdApeFactory"/>).
///
/// ## Implemented (v1)
///
/// - 1/1 Creature — Cat at {W}, owner/controller wired (from JSON).
/// - <b>Forest-conditional pump (CR 613.7c — Layer 7c)</b>: a
///   <see cref="ForestSelfPumpStaticEffect"/> registers against the supplied
///   <see cref="ContinuousEffectsService"/>. On every
///   <see cref="ContinuousEffectsService.Compute(Permanent)"/> for Loam Lion
///   the effect tests whether the controller controls at least one Forest and
///   applies +1/+2 when so. The condition re-evaluates dynamically: a Forest
///   ETBing flips the bonus on, the last Forest leaving (or being retyped off
///   Forest) flips it back off — no trigger / re-register cycle required.
///
/// ## Lifecycle
///
/// ETB/LTB lifecycle mirrors <see cref="KirdApeFactory"/>: subscribe to
/// <see cref="CardMovedEvent"/>; register the static effect when Loam Lion
/// enters the battlefield, unregister when it leaves. The
/// <see cref="ForestSelfPumpStaticEffect.IsActive"/> battlefield gate is
/// belt-and-braces redundancy if no event bus is supplied.
///
/// ## Wiring overloads
///
/// - <see cref="Create(Player)"/> — shape only. No layer-7c effect; card is
///   structurally correct (1/1, Cat, owner/controller) but the Forest pump
///   doesn't fire without a continuous-effects service. This is the overload
///   <see cref="NamedCardFactory"/> dispatches to.
/// - <see cref="Create(Player, ContinuousEffectsService?, IEventBus?)"/> —
///   fully wired. The pump registers on ETB and unregisters on LTB.
///
/// ## Forest-count semantics (CR 109.5, CR 305.6)
///
/// "Control a Forest" reads true when the controller controls at least one
/// permanent with the <see cref="CardSubtype.Forest"/> land subtype —
/// includes basic Forests, dual lands typed Forest, and any non-land
/// permanent retyped to Forest (CR 305.6 — the predicate tests the subtype
/// directly so it stays robust).
/// </summary>
[CardName("Loam Lion")]
public static class LoamLionFactory
{
    public const string CardName = "Loam Lion";
    public const string Slug = "loam-lion";
    public const int Power = 1;
    public const int Toughness = 1;
    public const int ForestBonusPower = 1;
    public const int ForestBonusToughness = 2;

    /// <summary>
    /// Construct Loam Lion with no live wiring. The Forest conditional pump is
    /// NOT attached (no continuous-effects service). Card shape (name, type,
    /// subtype, mana cost, P/T) is fully correct. This is the overload
    /// <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Creature Create(Player owner)
        => Create(owner, effects: null, eventBus: null);

    /// <summary>
    /// Construct Loam Lion with optional runtime services. When
    /// <paramref name="effects"/> is supplied a
    /// <see cref="ForestSelfPumpStaticEffect"/> registers so the +1/+2
    /// conditional pump is evaluated on every
    /// <see cref="ContinuousEffectsService.Compute"/> invocation. When
    /// <paramref name="eventBus"/> is also supplied the lifecycle binder
    /// subscribes to <see cref="CardMovedEvent"/> so the effect registers on
    /// ETB and unregisters on LTB.
    /// </summary>
    public static Creature Create(
        Player owner,
        ContinuousEffectsService? effects,
        IEventBus? eventBus)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Creature, Cat
        // subtype, {W}, 1/1). The JSON carries no abilities — the Forest pump
        // is layered on below.
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
    /// Layer 7c continuous effect for Loam Lion's Forest pump. On every
    /// <see cref="ContinuousEffectsService.Compute"/> invocation the effect
    /// tests whether the controller controls a Forest and, if so, applies
    /// +1/+2 to Loam Lion. Without a Forest the effect contributes nothing
    /// (CR 613.7c — a continuous effect that reads "as long as" gates its
    /// application on the predicate; it does not unregister, but its
    /// <see cref="AppliesTo"/> returns true and <see cref="Apply"/>
    /// contributes 0 when the predicate is false).
    ///
    /// Active only while Loam Lion is on the battlefield
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

        /// <summary>Active while Loam Lion is on the battlefield.</summary>
        public override bool IsActive() => _source.Zone == ZoneType.Battlefield;

        /// <summary>Applies only to Loam Lion itself.</summary>
        public override bool AppliesTo(Creature creature) =>
            ReferenceEquals(creature, _source);

        /// <summary>
        /// Apply +1/+2 when the controller controls a Forest; otherwise no
        /// contribution. Reads <see cref="Permanent.Controller"/> live so a
        /// control-changing effect on Loam Lion routes the Forest check
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
    /// ETB/LTB lifecycle binder for Loam Lion's Forest pump. Subscribes to
    /// <see cref="CardMovedEvent"/>; registers
    /// <see cref="ForestSelfPumpStaticEffect"/> when Loam Lion enters the
    /// battlefield, unregisters when it leaves. Mirrors
    /// <see cref="KirdApeFactory"/>'s <c>ForestPumpLifecycle</c>.
    /// </summary>
    private sealed class ForestPumpLifecycle
    {
        private readonly Creature _source;
        private readonly ContinuousEffectsService _effects;
        private readonly IEventBus? _eventBus;
        private readonly Action<CardMovedEvent> _handler;
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
            _eventBus?.Subscribe(_handler);
            Sync();
        }

        private void OnEvent(CardMovedEvent e)
        {
            var moved = e;
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
