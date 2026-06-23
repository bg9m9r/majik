using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Spineseeker Centipede (Outlaws of Thunder Junction,
/// {2}{G}).
///
/// Creature — Insect 2/1. Oracle text:
///   "When this creature enters, search your library for a basic land card,
///    reveal it, put it into your hand, then shuffle.
///    Delirium — This creature gets +1/+2 and has vigilance as long as there
///    are four or more card types among cards in your graveyard."
///
/// ## Shape source
/// Card identity (name, {2}{G}, 2/1, Creature — Insect) is loaded from
/// <c>Majik.Core/CardData/Cards/spineseeker-centipede.json</c> via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> and built through
/// <see cref="CardDefinitionFactory"/>. The ETB basic-land tutor and the
/// delirium static are wired in code below (the JSON ability schema does not
/// yet express either shape).
///
/// ## Implementation
///
/// - <b>ETB tutor (CR 603.6a)</b>: cribbed from
///   <see cref="BorderlandRangerFactory"/>, but Spineseeker's search is
///   MANDATORY (no "you may"). A <see cref="TriggeredAbility"/> over the ETB
///   condition searches the controller's library for ONE basic land card
///   (CR 305.6 — Basic supertype + Land card type), moves it Library → Hand,
///   and shuffles once (CR 701.20a — one shuffle whether or not a card was
///   found). The printed "reveal it" step is a no-op signal in v1 (same gap as
///   every tutor factory) — the card still reaches the hand, so the observable
///   game state is correct. The agent is consulted for which basic to take
///   (deterministic first-basic fallback when no agent is registered).
///
/// - <b>Delirium conditional static (CR 702.105 / 613.1f)</b>: cribbed from
///   <see cref="DragonsRageChannelerFactory"/>. Two continuous effects register
///   together — one Layer 7c (+1/+2) and one Layer 6 (Vigilance grant). Both
///   gate <c>IsActive()</c> on Spineseeker being on the battlefield AND
///   delirium being satisfied (4+ distinct <see cref="CardType"/> values in the
///   controller's graveyard, sampled live on every Compute via
///   <see cref="TarmogoyfFactory.CountDistinctCardTypes"/> — no event
///   subscriptions). The granted "Vigilance" keyword is read by
///   <see cref="Majik.Core.Combat.CombatAbilities.HasVigilance"/> (same path as
///   DRC's granted Flying).
///
/// ## Wiring overloads
///
/// - <see cref="Create(Player)"/> — card shape only. The ETB trigger is
///   attached for ability-shape observability but no TriggerManager is
///   registered; the delirium static is not wired to a
///   <see cref="ContinuousEffectsService"/>. Suitable for dispatcher /
///   structural tests.
/// - <see cref="Create(Player, IEventBus?, TriggerManager?, ContinuousEffectsService?)"/>
///   — fully wired. When a trigger manager is supplied, the ETB trigger fires
///   from the bus. When a continuous-effects service is supplied, the +1/+2
///   pump and Vigilance grant register / unregister via a battlefield-zone
///   lifecycle handler subscribed to the bus (mirrors
///   <see cref="DragonsRageChannelerFactory"/>).
/// </summary>
[CardName("Spineseeker Centipede")]
public static class SpineseekerCentipedeFactory
{
    public const string CardName = "Spineseeker Centipede";
    public const int DeliriumThreshold = 4;

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("spineseeker-centipede");

    /// <summary>
    /// Construct Spineseeker Centipede with no live wiring. The ETB trigger is
    /// attached to the card for shape observability; the delirium static is not
    /// registered with a continuous-effects service. Suitable for dispatcher /
    /// shape tests.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, eventBus: null, triggers: null, effects: null);

    /// <summary>
    /// Construct Spineseeker Centipede with optional runtime services.
    /// </summary>
    public static Creature Create(
        Player owner,
        IEventBus? eventBus,
        TriggerManager? triggers,
        ContinuousEffectsService? effects)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = (Creature)CardDefinitionFactory.Build(Definition, owner);
        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // ETB triggered ability — CR 603.6a.
        //   "When this creature enters, search your library for a basic
        //    land card, reveal it, put it into your hand, then shuffle."
        // MANDATORY (no "you may") — distinct from Borderland Ranger.
        // ----------------------------------------------------------------
        var etbEffect = new Effect(
            $"{CardName}: search a basic land -> hand, then shuffle",
            ctx =>
            {
                var controller = card.Controller ?? owner;
                return TutorOneBasicToHandAsync(controller, ctx);
            });

        var etbTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnEnterBattlefieldSelf(card),
            effects: new IEffect[] { etbEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(etbTrigger);
        triggers?.RegisterTriggeredAbility(etbTrigger);

        // ----------------------------------------------------------------
        // Delirium static — "This creature gets +1/+2 and has vigilance as
        // long as there are four or more card types among cards in your
        // graveyard." (CR 702.105 / CR 613.1f). Two continuous effects
        // register together — one Layer 7c (+1/+2) and one Layer 6
        // (Vigilance grant). Both gate IsActive() on Spineseeker being on
        // the battlefield AND delirium being satisfied (sampled live from
        // the controller's graveyard on every Compute).
        //
        // When no ContinuousEffectsService is supplied (shape-only path),
        // the effects aren't registered — card shape still reflects the
        // printed 2/1 with the trigger attached.
        // ----------------------------------------------------------------
        if (effects != null)
        {
            var lifecycle = new DeliriumLifecycle(card, owner, effects, eventBus);
            lifecycle.Attach();
        }

        return card;
    }

    /// <summary>
    /// Search <paramref name="player"/>'s library for ONE basic land card
    /// (CR 305.6 — Basic supertype + Land card type), consult the agent (which
    /// picks; deterministic first-basic fallback when no agent), move the pick
    /// Library → Hand, then shuffle once (CR 701.20a). The printed "reveal it"
    /// step is a no-op signal in v1 (same gap as every tutor factory) — the
    /// card still reaches the hand so the observable game state is correct.
    /// </summary>
    private static async ValueTask TutorOneBasicToHandAsync(Player player, ResolutionContext ctx)
    {
        bool IsBasicLand(ICard c) =>
            c.HasType(CardType.Land) && c.HasSupertype(CardSupertype.Basic);

        var agent = ctx.Agent ?? AgentRegistry.Get(player);

        var candidates = player.Zones.Library.GetCards().Where(IsBasicLand).ToList();
        ICard? pick = null;
        if (candidates.Count > 0)
        {
            pick = agent != null
                ? await agent.ChooseLibraryPickAsync(ctx.Game, candidates,
                        "basic land card to put into your hand")
                    .ConfigureAwait(false)
                : candidates[0];
        }

        if (pick != null)
        {
            var zones = ZoneServiceRegistry.Get(player);
            if (zones != null)
            {
                zones.MoveCard(pick, ZoneType.Library, ZoneType.Hand, player);
            }
            else
            {
                player.Zones.Library.RemoveCard(pick);
                player.Zones.Hand.AddCard(pick);
                pick.SetZone(ZoneType.Hand);
            }
        }

        // CR 701.20a — shuffle once after the search, even when zero cards
        // were found (the search still happened).
        LibraryShuffle.ShuffleLibrary(player, "spineseeker-centipede");
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
    /// CR 613.1f — continuous effect that pumps Spineseeker's P/T by +1/+2
    /// (Layer 7c) OR grants the Vigilance keyword (Layer 6), gated on delirium
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
            _source.Zone == ZoneType.Battlefield
            && IsDeliriumActive(_controller);

        public override bool AppliesTo(Creature c) => ReferenceEquals(c, _source);

        public override void Apply(CreatureCharacteristics chars)
        {
            if (_layer == Layer.PT_Modify)
            {
                chars.Power += 1;
                chars.Toughness += 2;
            }
            else if (_layer == Layer.Abilities)
            {
                chars.Keywords.Add("Vigilance");
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
    /// ETB/LTB lifecycle binder for Spineseeker's delirium static. Registers
    /// the +1/+2 (Layer 7c) and Vigilance (Layer 6) effects when Spineseeker
    /// enters the battlefield; unregisters when it leaves. Mirrors
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
        private DeliriumPumpEffect? _vigilanceRegistered;
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
            var shouldBeActive = _source.Zone == ZoneType.Battlefield;
            if (shouldBeActive && _pumpRegistered == null)
            {
                _pumpRegistered = new DeliriumPumpEffect(_source, _controller, Layer.PT_Modify);
                _vigilanceRegistered = new DeliriumPumpEffect(_source, _controller, Layer.Abilities);
                _effects.Register(_pumpRegistered);
                _effects.Register(_vigilanceRegistered);
            }
            else if (!shouldBeActive && _pumpRegistered != null)
            {
                _effects.Unregister(_pumpRegistered);
                if (_vigilanceRegistered != null) _effects.Unregister(_vigilanceRegistered);
                _pumpRegistered = null;
                _vigilanceRegistered = null;
            }
        }
    }
}
