using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Wildfire Wickerfolk (Bloomburrow / Modern, {R}{G}).
///
/// Artifact Creature — Scarecrow 3/2. Oracle text (verified against Scryfall
/// 2026-06-24):
///   "Haste
///    Delirium — This creature gets +1/+1 and has trample as long as there
///    are four or more card types among cards in your graveyard."
///
/// ## Shape source
///
/// Card identity (name, {R}{G}, 3/2, Artifact Creature — Scarecrow, intrinsic
/// Haste) is materialised from the embedded JSON definition
/// (<c>wildfire-wickerfolk.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>. Haste (CR 702.10) is carried as
/// a <c>keywords</c> entry in the JSON — a printed <see cref="KeywordAbility"/>
/// marker read by <see cref="Majik.Core.Combat.CombatAbilities.HasHaste"/>.
/// The delirium grant is wired in code below.
///
/// ## Implementation
///
/// Wildfire Wickerfolk is the +1/+1 / trample analogue of
/// <see cref="DragonsRageChannelerFactory"/> (which grants +2/+2 / flying on
/// delirium). It reuses the same delirium (CR 702.105) static-grant primitive:
///
/// - <b>Delirium conditional static (CR 702.105 / CR 613.1f)</b>: two
///   <see cref="DeliriumGrantEffect"/> instances registered with the
///   <see cref="ContinuousEffectsService"/> when the runtime overload is used —
///   one Layer 7c (+1/+1) and one Layer 6 (Trample grant). Both gate
///   <see cref="ContinuousEffect.IsActive"/> on Wildfire Wickerfolk being on
///   the battlefield AND the controller's graveyard holding 4+ distinct
///   <see cref="CardType"/> values (sampled live via
///   <see cref="TarmogoyfFactory.CountDistinctCardTypes"/> on every Compute, so
///   graveyard changes reflect immediately — no event subscriptions). The
///   granted Trample is read by <see cref="Majik.Core.Combat.CombatAbilities.HasTrample"/>
///   off the layer-computed keyword set (CR 702.19, excess-combat-damage rule).
///
/// ## Wiring overloads
///
/// - <see cref="Create(Player)"/> — card shape only (Haste marker present; the
///   delirium static is not registered with a continuous-effects service).
///   Suitable for dispatcher / structural tests.
/// - <see cref="Create(Player, IEventBus?, ContinuousEffectsService?)"/> —
///   fully wired. The +1/+1 pump and Trample grant register / unregister via a
///   battlefield-zone lifecycle handler subscribed to the bus (mirrors
///   <see cref="DragonsRageChannelerFactory"/>).
/// </summary>
[CardName("Wildfire Wickerfolk")]
public static class WildfireWickerfolkFactory
{
    public const string CardName = "Wildfire Wickerfolk";
    public const int DeliriumThreshold = 4;

    /// <summary>JSON slug for the embedded base-shape definition.</summary>
    public const string Slug = "wildfire-wickerfolk";

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource(Slug);

    /// <summary>
    /// Construct Wildfire Wickerfolk with no live wiring. Haste is present as a
    /// printed marker; the delirium static is not registered with a
    /// continuous-effects service. Suitable for dispatcher / shape tests.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, eventBus: null, effects: null);

    /// <summary>
    /// Construct Wildfire Wickerfolk with optional runtime services.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="eventBus">Bus the delirium lifecycle subscribes to for
    /// ETB/LTB. May be null.</param>
    /// <param name="effects">Continuous-effects service the delirium +1/+1 and
    /// Trample grants register against. May be null — the grants are then
    /// skipped (shape only).</param>
    public static Creature Create(
        Player owner,
        IEventBus? eventBus,
        ContinuousEffectsService? effects)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var built = CardDefinitionFactory.Build(Definition, owner);
        if (built is not Creature card)
        {
            throw new InvalidOperationException(
                $"Expected '{CardName}' to materialise as a Creature but got "
                + $"'{built.GetType().Name}'.");
        }

        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // Delirium static — "This creature gets +1/+1 and has trample as
        // long as there are four or more card types among cards in your
        // graveyard." (CR 702.105 / CR 613.1f). Two continuous effects
        // register together — one Layer 7c (+1/+1) and one Layer 6 (Trample
        // grant). Both gate IsActive() on Wildfire Wickerfolk being on the
        // battlefield AND delirium being satisfied (sampled live from the
        // controller's graveyard on every Compute).
        //
        // When no ContinuousEffectsService is supplied (shape-only path) the
        // grants aren't registered — the card still reflects the printed 3/2
        // with Haste.
        // ----------------------------------------------------------------
        if (effects != null)
        {
            var lifecycle = new DeliriumLifecycle(card, owner, effects, eventBus);
            lifecycle.Attach();
        }

        return card;
    }

    /// <summary>
    /// Sample the controller's graveyard for delirium (CR 702.105): true iff
    /// there are 4+ distinct <see cref="CardType"/> values across cards in
    /// <paramref name="controller"/>'s graveyard. Reuses
    /// <see cref="TarmogoyfFactory.CountDistinctCardTypes"/>.
    /// </summary>
    public static bool IsDeliriumActive(Player controller)
    {
        ArgumentNullException.ThrowIfNull(controller);
        return TarmogoyfFactory.CountDistinctCardTypes(
            controller.Zones.Graveyard.GetCards()) >= DeliriumThreshold;
    }

    /// <summary>
    /// CR 613.1f — continuous effect that pumps Wildfire Wickerfolk's P/T by
    /// +1/+1 (Layer 7c) OR grants the Trample keyword (Layer 6), gated on
    /// delirium (CR 702.105) and on the creature being on the battlefield. One
    /// instance per layer is registered by <see cref="DeliriumLifecycle"/>.
    /// </summary>
    private sealed class DeliriumGrantEffect : ContinuousEffect
    {
        private readonly Creature _source;
        private readonly Player _controller;
        private readonly Layer _layer;

        public DeliriumGrantEffect(Creature source, Player controller, Layer layer)
        {
            _source = source ?? throw new ArgumentNullException(nameof(source));
            _controller = controller ?? throw new ArgumentNullException(nameof(controller));
            _layer = layer;
        }

        public override Layer Layer => _layer;

        public override Permanent? Source => _source;

        public override bool IsActive() =>
            _source.Zone == Majik.Core.Zones.ZoneType.Battlefield
            && IsDeliriumActive(_controller);

        public override bool AppliesTo(Creature c) => ReferenceEquals(c, _source);

        public override void Apply(CreatureCharacteristics chars)
        {
            if (_layer == Layer.PT_Modify)
            {
                chars.Power += 1;
                chars.Toughness += 1;
            }
            else if (_layer == Layer.Abilities)
            {
                chars.Keywords.Add("Trample");
            }
        }

        /// <summary>
        /// Sim-only: reconstruct an identical <see cref="DeliriumGrantEffect"/>
        /// bound to <paramref name="clonedSource"/> for the search-sandbox
        /// clone. The controller is captured as a field; the cloned controller
        /// is obtained from clonedSource.Controller (remapped by
        /// RelinkReferences). Both the PT_Modify and Abilities layer instances
        /// are reconstructed independently by the cloner (one CloneForSim call
        /// per registered effect instance).
        /// preserves: _layer; source → clonedSource (as Creature); controller →
        /// clonedSource.Controller.
        /// </summary>
        internal override ContinuousEffect? CloneForSim(
            Majik.Core.Cards.Permanent clonedSource,
            System.Func<System.Collections.Generic.IReadOnlyList<Majik.Core.Players.Player>>? clonedPlayers)
        {
            if (clonedSource is not Majik.Core.Cards.Creature clonedCreature) return null;
            var clonedController = clonedCreature.Controller;
            if (clonedController == null) return null;
            return new DeliriumGrantEffect(clonedCreature, clonedController, _layer);
        }
    }

    /// <summary>
    /// ETB/LTB lifecycle binder for Wildfire Wickerfolk's delirium static.
    /// Registers the +1/+1 (Layer 7c) and Trample (Layer 6) effects when the
    /// creature enters the battlefield; unregisters when it leaves. Mirrors
    /// <see cref="DragonsRageChannelerFactory"/>'s lifecycle shape.
    /// </summary>
    private sealed class DeliriumLifecycle
    {
        private readonly Creature _source;
        private readonly Player _controller;
        private readonly ContinuousEffectsService _effects;
        private readonly IEventBus? _eventBus;
        private readonly Action<CardMovedEvent> _handler;
        private DeliriumGrantEffect? _pumpRegistered;
        private DeliriumGrantEffect? _trampleRegistered;
        private bool _attached;

        public DeliriumLifecycle(
            Creature source,
            Player controller,
            ContinuousEffectsService effects,
            IEventBus? eventBus)
        {
            _source = source;
            _controller = controller;
            _effects = effects;
            _eventBus = eventBus;
            _handler = OnEvent;
        }

        public void Attach()
        {
            if (_attached) return;
            _attached = true;
            _source.ActiveEffects = _effects;
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
            var shouldBeActive = _source.Zone == Majik.Core.Zones.ZoneType.Battlefield;
            if (shouldBeActive && _pumpRegistered == null)
            {
                _pumpRegistered = new DeliriumGrantEffect(_source, _controller, Layer.PT_Modify);
                _trampleRegistered = new DeliriumGrantEffect(_source, _controller, Layer.Abilities);
                _effects.Register(_pumpRegistered);
                _effects.Register(_trampleRegistered);
            }
            else if (!shouldBeActive && _pumpRegistered != null)
            {
                _effects.Unregister(_pumpRegistered);
                if (_trampleRegistered != null) _effects.Unregister(_trampleRegistered);
                _pumpRegistered = null;
                _trampleRegistered = null;
            }
        }
    }
}
