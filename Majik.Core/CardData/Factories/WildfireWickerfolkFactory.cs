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
/// Named-card factory for Wildfire Wickerfolk (Modern Horizons 3, {R}{G}).
///
/// Artifact Creature — Scarecrow 3/2. Oracle text (verified against Scryfall
/// 2026-06-23):
///   "Haste
///    Delirium — This creature gets +1/+1 and has trample as long as there
///    are four or more card types among cards in your graveyard."
///
/// ## Shape source
///
/// Card identity (name, {R}{G}, 3/2, Artifact Creature — Scarecrow, the printed
/// Haste keyword) is loaded from
/// <c>Majik.Core/CardData/Cards/wildfire-wickerfolk.json</c> via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> and built through
/// <see cref="CardDefinitionFactory"/>. The delirium-gated +1/+1-and-trample
/// static is wired in code below.
///
/// ## Implementation
///
/// Wildfire Wickerfolk is the +1/+1-and-keyword analogue of
/// <see cref="DragonsRageChannelerFactory"/> — it shares the same delirium
/// (CR 702.105) two-layer conditional-static primitive (one Layer-7c P/T pump
/// + one Layer-6 keyword grant), differing only in the pump magnitude (+1/+1
/// vs. +2/+2), the granted keyword (Trample vs. Flying), and the absence of any
/// triggered ability. Haste is printed (always on) rather than delirium-gated,
/// so it carried in the JSON <c>keywords</c> array as a
/// <see cref="KeywordAbility"/> marker read by
/// <see cref="Majik.Core.Combat.CombatAbilities.HasHaste"/>.
///
/// - <b>Haste (CR 702.10)</b> — printed keyword marker from the JSON, read by
///   <see cref="Majik.Core.Combat.CombatAbilities.HasHaste"/> so the creature
///   ignores summoning sickness for attacking / tap abilities.
///
/// - <b>Delirium conditional static (CR 702.105 / CR 613.1f)</b>: two
///   <see cref="DeliriumPumpEffect"/> instances registered with the
///   <see cref="ContinuousEffectsService"/> when the runtime overload is used —
///   one in Layer 7c (+1/+1) and one in Layer 6 (grants "Trample"). Both gate
///   IsActive() on the Wickerfolk being on the battlefield AND the controller's
///   graveyard holding 4+ distinct <see cref="CardType"/> values (sampled live
///   via <see cref="TarmogoyfFactory.CountDistinctCardTypes"/> on every
///   Compute, so graveyard changes reflect immediately — no event
///   subscriptions). The granted Trample (CR 702.19) flows through the layer
///   system because <see cref="Majik.Core.Combat.CombatAbilities.HasTrample"/>
///   reads the computed keyword set when an effects service is wired.
///
/// ## Wiring overloads
///
/// - <see cref="Create(Player)"/> — card shape only (Haste marker present from
///   the JSON; the delirium static is not registered with a continuous-effects
///   service). Suitable for dispatcher / structural tests.
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
    /// Construct Wildfire Wickerfolk with no live wiring. The Haste marker is
    /// present (from the JSON); the delirium static is not registered with a
    /// continuous-effects service. Suitable for dispatcher / shape tests.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, eventBus: null, effects: null);

    /// <summary>
    /// Construct Wildfire Wickerfolk with optional runtime services.
    /// </summary>
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
        // Delirium static — "This creature gets +1/+1 and has trample as long
        // as there are four or more card types among cards in your graveyard."
        // (CR 702.105 / CR 613.1f). Two continuous effects register together —
        // one Layer 7c (+1/+1) and one Layer 6 (Trample grant). Both gate
        // IsActive() on the Wickerfolk being on the battlefield AND delirium
        // being satisfied (sampled live from the controller's graveyard on
        // every Compute).
        //
        // When no ContinuousEffectsService is supplied (shape-only path) the
        // effects aren't registered — the card still reflects the printed 3/2
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
    /// CR 613.1f — continuous effect that pumps the Wickerfolk's P/T by +1/+1
    /// (Layer 7c) OR grants the Trample keyword (Layer 6), gated on delirium
    /// (CR 702.105). One instance per layer is registered by
    /// <see cref="DeliriumLifecycle"/>.
    /// </summary>
    private sealed class DeliriumPumpEffect : ContinuousEffect
    {
        private readonly Creature _source;
        private readonly Player _controller;
        private readonly Layer _layer;

        public DeliriumPumpEffect(Creature source, Player controller, Layer layer)
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
        /// Sim-only: reconstruct an identical <see cref="DeliriumPumpEffect"/> bound to
        /// <paramref name="clonedSource"/> for the search-sandbox clone.
        /// The controller is captured as a field; the cloned controller is obtained from
        /// clonedSource.Controller (remapped by RelinkReferences). Both the PT_Modify and
        /// Abilities layer instances are reconstructed independently by the cloner (one
        /// CloneForSim call per registered effect instance).
        /// preserves: _layer; source → clonedSource (as Creature); controller → clonedSource.Controller.
        /// </summary>
        internal override ContinuousEffect? CloneForSim(
            Majik.Core.Cards.Permanent clonedSource,
            System.Func<System.Collections.Generic.IReadOnlyList<Majik.Core.Players.Player>>? clonedPlayers)
        {
            if (clonedSource is not Majik.Core.Cards.Creature clonedCreature) return null;
            var clonedController = clonedCreature.Controller;
            if (clonedController == null) return null;
            return new DeliriumPumpEffect(clonedCreature, clonedController, _layer);
        }
    }

    /// <summary>
    /// ETB/LTB lifecycle binder for the Wickerfolk's delirium static. Registers
    /// the +1/+1 (Layer 7c) and Trample (Layer 6) effects when it enters the
    /// battlefield; unregisters when it leaves. Mirrors
    /// <see cref="DragonsRageChannelerFactory"/>'s lifecycle shape.
    /// </summary>
    private sealed class DeliriumLifecycle
    {
        private readonly Creature _source;
        private readonly Player _controller;
        private readonly ContinuousEffectsService _effects;
        private readonly IEventBus? _eventBus;
        private readonly Action<CardMovedEvent> _handler;
        private DeliriumPumpEffect? _pumpRegistered;
        private DeliriumPumpEffect? _trampleRegistered;
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
                _pumpRegistered = new DeliriumPumpEffect(_source, _controller, Layer.PT_Modify);
                _trampleRegistered = new DeliriumPumpEffect(_source, _controller, Layer.Abilities);
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
