using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Goblin Tomb Raider (Ixalan, {R}). Creature — Goblin
/// Pirate 1/2. Oracle text (verified against Scryfall 2026-06):
///   "As long as you control an artifact, this creature gets +1/+0 and has
///    haste."
///
/// Mechanically a two-layer sibling of <see cref="InventorsApprenticeFactory"/>
/// (Kird Ape / Loam Lion's "+X/+Y as long as you control a Forest"; Inventor's
/// Apprentice's "+1/+1 as long as you control an artifact"). It shares the same
/// artifact-control predicate but layers TWO effects keyed on that one
/// condition:
/// <list type="bullet">
///   <item><b>Layer 7c (CR 613.7c)</b> — the +1/+0 P/T self-pump, identical
///         posture to <see cref="InventorsApprenticeFactory.ArtifactSelfPumpStaticEffect"/>
///         but +1/+0 rather than +1/+1.</item>
///   <item><b>Layer 6 (CR 613.3, ability-adding)</b> — the conditional Haste
///         (CR 702.10) grant, identical posture to
///         <see cref="HexproofWhileUntappedEffect"/> (a self-applied Layer-6
///         keyword grant gated on a predicate evaluated each Compute pass) but
///         keyed on the artifact-control predicate rather than untapped state.
///         <see cref="Majik.Core.Combat.CombatAbilities.HasHaste"/> reads the
///         computed keyword set (<c>ActiveEffects.Compute(c).Keywords</c>), so
///         the grant suppresses summoning sickness for combat.</item>
/// </list>
///
/// Both effects share ONE predicate (control an artifact) re-evaluated inside
/// <c>Apply</c> on every <see cref="ContinuousEffectsService.Compute"/> pass —
/// an artifact ETBing flips both the +1/+0 and haste on; the last artifact
/// leaving flips both back off, with no trigger / re-register cycle.
///
/// Base shape (name, Creature, Goblin/Pirate subtypes, {R}, 1/2) is
/// materialised from the embedded JSON definition (<c>goblin-tomb-raider.json</c>)
/// via <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>. The conditional effects are
/// layered on top here — the JSON <c>AbilityDefinition</c> schema doesn't yet
/// express "as long as you control an artifact" statics.
///
/// ## Wiring overloads
///
/// - <see cref="Create(Player)"/> — shape only. No conditional effects; card is
///   structurally correct (1/2, Goblin Pirate, owner/controller) but the
///   artifact pump + haste don't fire without a continuous-effects service.
///   This is the overload <see cref="NamedCardFactory"/> dispatches to.
/// - <see cref="Create(Player, ContinuousEffectsService?, IEventBus?)"/> —
///   fully wired. Both effects register on ETB and unregister on LTB.
///
/// ## Artifact-control semantics (CR 109.5, CR 301.1)
///
/// "Control an artifact" reads true when the controller controls at least one
/// permanent with the <see cref="CardType.Artifact"/> card type — includes
/// artifact tokens, artifact creatures, and any artifact teammate. Goblin Tomb
/// Raider itself is a Goblin Pirate (not an artifact), so it does not satisfy
/// its own predicate.
/// </summary>
[CardName("Goblin Tomb Raider")]
public static class GoblinTombRaiderFactory
{
    public const string CardName = "Goblin Tomb Raider";
    public const string Slug = "goblin-tomb-raider";
    public const int Power = 1;
    public const int Toughness = 2;
    public const int ArtifactBonusPower = 1;
    public const int ArtifactBonusToughness = 0;

    /// <summary>
    /// Construct Goblin Tomb Raider with no live wiring. The artifact
    /// conditional pump + haste grant are NOT attached (no continuous-effects
    /// service). Card shape (name, type, subtypes, mana cost, P/T) is fully
    /// correct. This is the overload <see cref="NamedCardFactory"/> dispatches
    /// to.
    /// </summary>
    public static Creature Create(Player owner)
        => Create(owner, effects: null, eventBus: null);

    /// <summary>
    /// Construct Goblin Tomb Raider with optional runtime services. When
    /// <paramref name="effects"/> is supplied an
    /// <see cref="ArtifactSelfPumpStaticEffect"/> (Layer 7c, +1/+0) and an
    /// <see cref="ArtifactHasteStaticEffect"/> (Layer 6, Haste) register so
    /// both conditional behaviours are evaluated on every
    /// <see cref="ContinuousEffectsService.Compute"/> invocation. When
    /// <paramref name="eventBus"/> is also supplied the lifecycle binder
    /// subscribes to <see cref="CardMovedEvent"/> so the effects register on
    /// ETB and unregister on LTB.
    /// </summary>
    public static Creature Create(
        Player owner,
        ContinuousEffectsService? effects,
        IEventBus? eventBus)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Creature, Goblin/
        // Pirate subtypes, {R}, 1/2). The JSON carries no abilities — the
        // artifact pump + haste are layered on below.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Creature)CardDefinitionFactory.Build(definition, owner);

        if (effects != null)
        {
            var lifecycle = new ArtifactConditionLifecycle(card, effects, eventBus);
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
    // ArtifactSelfPumpStaticEffect — Layer 7c conditional self-pump (+1/+0).
    // -----------------------------------------------------------------------

    /// <summary>
    /// Layer 7c continuous effect for Goblin Tomb Raider's artifact pump. On
    /// every <see cref="ContinuousEffectsService.Compute"/> invocation the
    /// effect tests whether the controller controls an artifact and, if so,
    /// applies +1/+0 (CR 613.7c — an "as long as" continuous effect gates its
    /// contribution on the predicate; it stays registered but contributes 0
    /// when the predicate is false). Active only while the source is on the
    /// battlefield.
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

        /// <summary>Active while Goblin Tomb Raider is on the battlefield.</summary>
        public override bool IsActive() => _source.Zone == ZoneType.Battlefield;

        /// <summary>Applies only to Goblin Tomb Raider itself.</summary>
        public override bool AppliesTo(Creature creature) =>
            ReferenceEquals(creature, _source);

        /// <summary>
        /// Apply +1/+0 when the controller controls an artifact; otherwise no
        /// contribution. Reads <see cref="Permanent.Controller"/> live so a
        /// control-changing effect routes the artifact check through the new
        /// controller's battlefield (CR 109.5).
        /// </summary>
        public override void Apply(CreatureCharacteristics chars)
        {
            var controller = _source.Controller;
            if (controller == null) return;
            if (!ControlsArtifact(controller)) return;
            chars.Power += ArtifactBonusPower;
            chars.Toughness += ArtifactBonusToughness;
        }

        /// <summary>
        /// Sim-only: reconstruct an identical <see cref="ArtifactSelfPumpStaticEffect"/>
        /// bound to <paramref name="clonedSource"/> for the search-sandbox clone.
        /// The artifact-controls predicate reads clonedSource.Controller live.
        /// preserves: nothing scalar; source → clonedSource (as Creature).
        /// </summary>
        internal override ContinuousEffect? CloneForSim(
            Majik.Core.Cards.Permanent clonedSource,
            System.Func<System.Collections.Generic.IReadOnlyList<Majik.Core.Players.Player>>? clonedPlayers)
            => clonedSource is Majik.Core.Cards.Creature clonedCreature
                ? new ArtifactSelfPumpStaticEffect(clonedCreature)
                : null;
    }

    // -----------------------------------------------------------------------
    // ArtifactHasteStaticEffect — Layer 6 conditional Haste grant.
    // -----------------------------------------------------------------------

    /// <summary>
    /// Layer 6 continuous effect (CR 613.3, ability-adding) for Goblin Tomb
    /// Raider's conditional Haste (CR 702.10). On every
    /// <see cref="ContinuousEffectsService.Compute"/> invocation the effect
    /// tests whether the controller controls an artifact and, if so, adds the
    /// "Haste" keyword to the working-set. The condition is checked inside
    /// <see cref="Apply"/> (not <see cref="IsActive"/>) so the effect stays
    /// attached and is simply re-evaluated as artifacts come and go — mirroring
    /// <see cref="HexproofWhileUntappedEffect"/>'s posture.
    /// <see cref="Majik.Core.Combat.CombatAbilities.HasHaste"/> reads the
    /// computed keyword set, so the grant suppresses summoning sickness while
    /// the predicate holds.
    /// </summary>
    public sealed class ArtifactHasteStaticEffect : ContinuousEffect
    {
        private readonly Creature _source;

        public ArtifactHasteStaticEffect(Creature source)
        {
            _source = source ?? throw new ArgumentNullException(nameof(source));
        }

        /// <summary>CR 613.1g — the permanent generating this effect.</summary>
        public override Permanent? Source => _source;

        /// <inheritdoc/>
        public override Layer Layer => Layer.Abilities;

        /// <summary>Active while Goblin Tomb Raider is on the battlefield.</summary>
        public override bool IsActive() => _source.Zone == ZoneType.Battlefield;

        /// <summary>Applies only to Goblin Tomb Raider itself.</summary>
        public override bool AppliesTo(Creature creature) =>
            ReferenceEquals(creature, _source);

        /// <summary>
        /// Grant Haste (CR 702.10) only while the controller controls an
        /// artifact. Reads <see cref="Permanent.Controller"/> live (CR 109.5).
        /// </summary>
        public override void Apply(CreatureCharacteristics chars)
        {
            var controller = _source.Controller;
            if (controller == null) return;
            if (!ControlsArtifact(controller)) return;
            chars.Keywords.Add("Haste");
        }
    }

    // -----------------------------------------------------------------------
    // ArtifactConditionLifecycle — ETB/LTB wiring for BOTH conditional effects.
    // -----------------------------------------------------------------------

    /// <summary>
    /// ETB/LTB lifecycle binder for Goblin Tomb Raider's artifact-conditional
    /// behaviours. Subscribes to <see cref="CardMovedEvent"/>; registers BOTH
    /// the Layer-7c pump and the Layer-6 haste grant when the source enters the
    /// battlefield, unregisters both when it leaves. Mirrors
    /// <see cref="InventorsApprenticeFactory"/>'s <c>ArtifactPumpLifecycle</c>,
    /// extended to manage the second (haste) effect alongside the pump so they
    /// flip on/off together.
    /// </summary>
    private sealed class ArtifactConditionLifecycle
    {
        private readonly Creature _source;
        private readonly ContinuousEffectsService _effects;
        private readonly IEventBus? _eventBus;
        private readonly Action<CardMovedEvent> _handler;
        private ArtifactSelfPumpStaticEffect? _pump;
        private ArtifactHasteStaticEffect? _haste;
        private bool _attached;

        public ArtifactConditionLifecycle(
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
            if (!ReferenceEquals(e.Card, _source)) return;
            Sync();
        }

        private void Sync()
        {
            var shouldBeActive = _source.Zone == ZoneType.Battlefield;
            if (shouldBeActive && _pump == null)
            {
                _pump = new ArtifactSelfPumpStaticEffect(_source);
                _haste = new ArtifactHasteStaticEffect(_source);
                _effects.Register(_pump);
                _effects.Register(_haste);
            }
            else if (!shouldBeActive && _pump != null)
            {
                _effects.Unregister(_pump);
                if (_haste != null) _effects.Unregister(_haste);
                _pump = null;
                _haste = null;
            }
        }
    }
}
