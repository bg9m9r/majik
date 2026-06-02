using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Inventor's Apprentice (Kaladesh, {R}). Creature —
/// Human Artificer 1/2. Oracle text (verified against Scryfall 2026-06):
///   "This creature gets +1/+1 as long as you control an artifact."
///
/// Mechanically a sibling of <see cref="LoamLionFactory"/> (Kird Ape / Loam
/// Lion's "+X/+Y as long as you control a Forest") — the same conditional
/// self-pump shape, differing only in the predicate (here "control an
/// <em>artifact</em>", a card-TYPE test, rather than the Forest land-subtype
/// test) and the bonus (+1/+1 rather than +1/+2). This factory mirrors that
/// implementation, swapping the predicate and pump amounts.
///
/// Base shape (name, Creature, Human/Artificer subtypes, {R}, 1/2) is
/// materialised from the embedded JSON definition (<c>inventors-apprentice.json</c>)
/// via <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>. The conditional self-pump is
/// layered on top here — the JSON <c>AbilityDefinition</c> schema doesn't yet
/// express "as long as you control an artifact" statics, so it lives in the
/// factory (same posture as <see cref="LoamLionFactory"/>).
///
/// ## Implemented (v1)
///
/// - 1/2 Creature — Human Artificer at {R}, owner/controller wired (from JSON).
/// - <b>Artifact-conditional pump (CR 613.7c — Layer 7c)</b>: an
///   <see cref="ArtifactSelfPumpStaticEffect"/> registers against the supplied
///   <see cref="ContinuousEffectsService"/>. On every
///   <see cref="ContinuousEffectsService.Compute(Permanent)"/> for Inventor's
///   Apprentice the effect tests whether the controller controls at least one
///   artifact and applies +1/+1 when so. The condition re-evaluates
///   dynamically: an artifact ETBing flips the bonus on, the last artifact
///   leaving flips it back off — no trigger / re-register cycle required.
///
/// ## Lifecycle
///
/// ETB/LTB lifecycle mirrors <see cref="LoamLionFactory"/>: subscribe to
/// <see cref="CardMovedEvent"/>; register the static effect when Inventor's
/// Apprentice enters the battlefield, unregister when it leaves. The
/// <see cref="ArtifactSelfPumpStaticEffect.IsActive"/> battlefield gate is
/// belt-and-braces redundancy if no event bus is supplied.
///
/// ## Wiring overloads
///
/// - <see cref="Create(Player)"/> — shape only. No layer-7c effect; card is
///   structurally correct (1/2, Human Artificer, owner/controller) but the
///   artifact pump doesn't fire without a continuous-effects service. This is
///   the overload <see cref="NamedCardFactory"/> dispatches to.
/// - <see cref="Create(Player, ContinuousEffectsService?, IEventBus?)"/> —
///   fully wired. The pump registers on ETB and unregisters on LTB.
///
/// ## Artifact-control semantics (CR 109.5, CR 301.1)
///
/// "Control an artifact" reads true when the controller controls at least one
/// permanent with the <see cref="CardType.Artifact"/> card type — includes
/// artifact tokens, artifact creatures, and Inventor's Apprentice's own
/// artifact-typed teammates. Inventor's Apprentice itself is NOT an artifact
/// (Human Artificer), so it does not satisfy its own predicate.
/// </summary>
[CardName("Inventor's Apprentice")]
public static class InventorsApprenticeFactory
{
    public const string CardName = "Inventor's Apprentice";
    public const string Slug = "inventors-apprentice";
    public const int Power = 1;
    public const int Toughness = 2;
    public const int ArtifactBonusPower = 1;
    public const int ArtifactBonusToughness = 1;

    /// <summary>
    /// Construct Inventor's Apprentice with no live wiring. The artifact
    /// conditional pump is NOT attached (no continuous-effects service). Card
    /// shape (name, type, subtypes, mana cost, P/T) is fully correct. This is
    /// the overload <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Creature Create(Player owner)
        => Create(owner, effects: null, eventBus: null);

    /// <summary>
    /// Construct Inventor's Apprentice with optional runtime services. When
    /// <paramref name="effects"/> is supplied an
    /// <see cref="ArtifactSelfPumpStaticEffect"/> registers so the +1/+1
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

        // Base shape from the embedded JSON definition (name, Creature, Human/
        // Artificer subtypes, {R}, 1/2). The JSON carries no abilities — the
        // artifact pump is layered on below.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Creature)CardDefinitionFactory.Build(definition, owner);

        if (effects != null)
        {
            var lifecycle = new ArtifactPumpLifecycle(card, effects, eventBus);
            lifecycle.Attach();
        }

        return card;
    }

    /// <summary>
    /// CR 109.5 — "you control" reads each permanent's current controller.
    /// True when the controller controls at least one permanent with the
    /// <see cref="CardType.Artifact"/> card type (CR 301.1).
    /// </summary>
    public static bool ControlsArtifact(Player controller)
    {
        ArgumentNullException.ThrowIfNull(controller);
        foreach (var c in controller.Zones.Battlefield.GetCards())
        {
            if (c.HasType(CardType.Artifact)) return true;
        }
        return false;
    }

    // -----------------------------------------------------------------------
    // ArtifactSelfPumpStaticEffect — Layer 7c conditional self-pump.
    // -----------------------------------------------------------------------

    /// <summary>
    /// Layer 7c continuous effect for Inventor's Apprentice's artifact pump. On
    /// every <see cref="ContinuousEffectsService.Compute"/> invocation the
    /// effect tests whether the controller controls an artifact and, if so,
    /// applies +1/+1 to Inventor's Apprentice. Without an artifact the effect
    /// contributes nothing (CR 613.7c — a continuous effect that reads "as long
    /// as" gates its application on the predicate; it does not unregister, but
    /// its <see cref="AppliesTo"/> returns true and <see cref="Apply"/>
    /// contributes 0 when the predicate is false).
    ///
    /// Active only while Inventor's Apprentice is on the battlefield
    /// (<see cref="IsActive"/> gate — belt-and-braces alongside the ETB/LTB
    /// lifecycle wiring in <see cref="ArtifactPumpLifecycle"/>).
    /// </summary>
    public sealed class ArtifactSelfPumpStaticEffect : ContinuousEffect
    {
        private readonly Creature _source;

        public ArtifactSelfPumpStaticEffect(Creature source)
        {
            _source = source ?? throw new ArgumentNullException(nameof(source));
        }

        /// <summary>CR 613.1g — the permanent generating this effect.</summary>
        public override Permanent? Source => _source;

        /// <inheritdoc/>
        public override Layer Layer => Layer.PT_Modify;

        /// <summary>Active while Inventor's Apprentice is on the battlefield.</summary>
        public override bool IsActive() => _source.Zone == ZoneType.Battlefield;

        /// <summary>Applies only to Inventor's Apprentice itself.</summary>
        public override bool AppliesTo(Creature creature) =>
            ReferenceEquals(creature, _source);

        /// <summary>
        /// Apply +1/+1 when the controller controls an artifact; otherwise no
        /// contribution. Reads <see cref="Permanent.Controller"/> live so a
        /// control-changing effect on Inventor's Apprentice routes the artifact
        /// check through the new controller's battlefield (CR 109.5).
        /// </summary>
        public override void Apply(CreatureCharacteristics chars)
        {
            var controller = _source.Controller;
            if (controller == null) return;
            if (!ControlsArtifact(controller)) return;
            chars.Power += ArtifactBonusPower;
            chars.Toughness += ArtifactBonusToughness;
        }
    }

    // -----------------------------------------------------------------------
    // ArtifactPumpLifecycle — ETB/LTB wiring for the artifact pump effect.
    // -----------------------------------------------------------------------

    /// <summary>
    /// ETB/LTB lifecycle binder for Inventor's Apprentice's artifact pump.
    /// Subscribes to <see cref="CardMovedEvent"/>; registers
    /// <see cref="ArtifactSelfPumpStaticEffect"/> when Inventor's Apprentice
    /// enters the battlefield, unregisters when it leaves. Mirrors
    /// <see cref="LoamLionFactory"/>'s <c>ForestPumpLifecycle</c>.
    /// </summary>
    private sealed class ArtifactPumpLifecycle
    {
        private readonly Creature _source;
        private readonly ContinuousEffectsService _effects;
        private readonly IEventBus? _eventBus;
        private readonly Action<CardMovedEvent> _handler;
        private ArtifactSelfPumpStaticEffect? _registered;
        private bool _attached;

        public ArtifactPumpLifecycle(
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
                _registered = new ArtifactSelfPumpStaticEffect(_source);
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
