using System.Linq;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Mind Drill Assailant (Modern Horizons 3,
/// {2}{U/B}{U/B}).
///
/// Creature — Rat Warlock 2/5. Oracle text (verified against Scryfall
/// 2026-06-24):
///   "Threshold — As long as there are seven or more cards in your graveyard,
///    this creature gets +3/+0.
///    {2}{U/B}: Surveil 1. (Look at the top card of your library. You may put
///    it into your graveyard.)"
///
/// ## Shape source
///
/// Card identity (name, {2}{U/B}{U/B}, 2/5, Creature — Rat Warlock) and the
/// activated surveil ability are loaded from
/// <c>Majik.Core/CardData/Cards/mind-drill-assailant.json</c> via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> and built through
/// <see cref="CardDefinitionFactory"/>. The threshold (CR 702.85) static pump
/// is wired in code below.
///
/// - <b>{2}{U/B}: Surveil 1 (CR 701.42 / CR 117.0a hybrid mana)</b> — a fully
///   declarative <c>activated</c> ability with a <c>mana</c> cost
///   (<c>{2}{U/B}</c> — the {U/B} hybrid pip parses the same way Flame Javelin's
///   {2/R} hybrid does, via <see cref="Majik.Core.ValueObjects.ManaCost.Parse"/>)
///   and a <c>surveil_self</c> effect. Same JSON shape as
///   <see cref="SinisterStarfishFactory"/> (which uses a {T} cost instead of a
///   mana cost). At resolution the shared surveil builder consults the
///   controller's agent (may put the top card into the graveyard), falling back
///   to all-to-graveyard when no agent is registered.
///
/// - <b>Threshold static (CR 702.85 / CR 613.1f)</b>: a Layer-7c
///   <see cref="ThresholdPumpEffect"/> registered with the
///   <see cref="ContinuousEffectsService"/>. Active iff this creature is on the
///   battlefield AND the controller's graveyard holds 7+ cards (sampled live on
///   every Compute, so graveyard changes reflect immediately — same posture as
///   <see cref="GrimFlayerFactory"/>'s delirium pump). When active it adds
///   +3/+0 (power only). Threshold is the simple graveyard-card-COUNT analogue
///   of Grim Flayer's delirium (distinct-card-TYPE count).
///
/// ## Wiring overloads
///
/// - <see cref="Create(Player)"/> — card shape only (activated surveil ability
///   attached from JSON; threshold static not registered). Suitable for
///   dispatcher / shape tests.
/// - <see cref="Create(Player, IEventBus?, ContinuousEffectsService?)"/> — fully
///   wired. The +3/+0 pump registers / unregisters via a battlefield-zone
///   lifecycle handler when a continuous-effects service is supplied (mirrors
///   <see cref="GrimFlayerFactory"/>).
/// </summary>
[CardName("Mind Drill Assailant")]
public static class MindDrillAssailantFactory
{
    public const string CardName = "Mind Drill Assailant";
    public const string Slug = "mind-drill-assailant";

    /// <summary>CR 702.85 — threshold is satisfied at seven or more cards.</summary>
    public const int ThresholdCount = 7;

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource(Slug);

    /// <summary>
    /// Construct Mind Drill Assailant with no live continuous-effects wiring.
    /// The activated surveil ability is materialised from JSON; the threshold
    /// static is not registered. Suitable for dispatcher / shape tests. This is
    /// the overload <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, eventBus: null, effects: null);

    /// <summary>
    /// Construct Mind Drill Assailant with optional runtime services. When an
    /// <see cref="ContinuousEffectsService"/> is supplied the threshold +3/+0
    /// pump registers / unregisters across battlefield lifecycle.
    /// </summary>
    public static Creature Create(
        Player owner,
        IEventBus? eventBus,
        ContinuousEffectsService? effects)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = (Creature)CardDefinitionFactory.Build(Definition, owner);
        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // Threshold static — "As long as there are seven or more cards in
        // your graveyard, this creature gets +3/+0." (CR 702.85 / CR 613.1f).
        // One Layer-7c continuous effect, gated on this creature being on the
        // battlefield AND threshold being satisfied (sampled live from the
        // controller's graveyard on every Compute). When no
        // ContinuousEffectsService is supplied (shape-only path) the effect
        // isn't registered — the card still reflects the printed 2/5 with the
        // surveil ability attached.
        // ----------------------------------------------------------------
        if (effects != null)
        {
            var lifecycle = new ThresholdLifecycle(card, owner, effects, eventBus);
            lifecycle.Attach();
        }

        return card;
    }

    /// <summary>
    /// CR 702.85 — true iff <paramref name="controller"/>'s graveyard holds
    /// seven or more cards.
    /// </summary>
    public static bool IsThresholdActive(Player controller)
    {
        ArgumentNullException.ThrowIfNull(controller);
        return controller.Zones.Graveyard.GetCards().Count() >= ThresholdCount;
    }

    /// <summary>
    /// CR 613.1f — Layer-7c continuous effect that pumps Mind Drill Assailant's
    /// power by +3 (toughness unchanged), gated on threshold (CR 702.85) and on
    /// the creature being on the battlefield.
    /// </summary>
    private sealed class ThresholdPumpEffect : ContinuousEffect
    {
        private readonly Creature _source;
        private readonly Player _controller;

        public ThresholdPumpEffect(Creature source, Player controller)
        {
            _source = source ?? throw new ArgumentNullException(nameof(source));
            _controller = controller ?? throw new ArgumentNullException(nameof(controller));
        }

        public override Layer Layer => Layer.PT_Modify;

        public override Permanent? Source => _source;

        public override bool IsActive() =>
            _source.Zone == ZoneType.Battlefield
            && IsThresholdActive(_controller);

        public override bool AppliesTo(Creature c) => ReferenceEquals(c, _source);

        public override void Apply(CreatureCharacteristics chars)
        {
            chars.Power += 3;
            // +0 toughness — threshold pumps power only.
        }

        /// <summary>
        /// Sim-only: reconstruct an identical <see cref="ThresholdPumpEffect"/> bound to
        /// <paramref name="clonedSource"/> for the search-sandbox clone. The controller is
        /// obtained from clonedSource.Controller (remapped by RelinkReferences).
        /// preserves: nothing scalar beyond source/controller; source → clonedSource (as Creature).
        /// </summary>
        internal override ContinuousEffect? CloneForSim(
            Majik.Core.Cards.Permanent clonedSource,
            System.Func<System.Collections.Generic.IReadOnlyList<Majik.Core.Players.Player>>? clonedPlayers)
        {
            if (clonedSource is not Majik.Core.Cards.Creature clonedCreature) return null;
            var clonedController = clonedCreature.Controller;
            if (clonedController == null) return null;
            return new ThresholdPumpEffect(clonedCreature, clonedController);
        }
    }

    /// <summary>
    /// ETB/LTB lifecycle binder for the threshold static. Registers the +3/+0
    /// (Layer 7c) effect when Mind Drill Assailant enters the battlefield;
    /// unregisters when it leaves. Mirrors <see cref="GrimFlayerFactory"/>'s
    /// lifecycle shape.
    /// </summary>
    private sealed class ThresholdLifecycle
    {
        private readonly Creature _source;
        private readonly Player _controller;
        private readonly ContinuousEffectsService _effects;
        private readonly IEventBus? _eventBus;
        private readonly Action<CardMovedEvent> _handler;
        private ThresholdPumpEffect? _pumpRegistered;
        private bool _attached;

        public ThresholdLifecycle(
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
            var shouldBeActive = _source.Zone == ZoneType.Battlefield;
            if (shouldBeActive && _pumpRegistered == null)
            {
                _pumpRegistered = new ThresholdPumpEffect(_source, _controller);
                _effects.Register(_pumpRegistered);
            }
            else if (!shouldBeActive && _pumpRegistered != null)
            {
                _effects.Unregister(_pumpRegistered);
                _pumpRegistered = null;
            }
        }
    }
}
