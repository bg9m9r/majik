using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Outcaster Greenblade (Outlaws of Thunder Junction,
/// {2}{G}).
///
/// Creature — Human Mercenary 1/2. Oracle text (verified against Scryfall
/// 2026-06-24):
///   "When this creature enters, search your library for a basic land card or
///    a Desert card, reveal it, put it into your hand, then shuffle.
///    This creature gets +1/+1 for each Desert you control."
///
/// ## Shape source
/// Card identity (name, {2}{G}, 1/2, Creature — Human Mercenary) is loaded from
/// <c>Majik.Core/CardData/Cards/outcaster-greenblade.json</c> via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> and built through
/// <see cref="CardDefinitionFactory"/>. The two non-keyword abilities are
/// layered on in code below.
///
/// ## Implemented (v1)
///
/// ### ETB tutor (CR 603.6a)
/// "When this creature enters, search your library for a basic land card or a
/// Desert card, reveal it, put it into your hand, then shuffle." Same posture as
/// <see cref="BorderlandRangerFactory"/> (basic-land tutor-to-hand) but the
/// search predicate is widened to ALSO match any card with the Desert subtype
/// (CR 305.6 — basic land = Basic supertype + Land card type; Desert = the land
/// subtype, basic or nonbasic). Consults the registered
/// <see cref="IPlayerAgent"/> via <see cref="IPlayerAgent.ChooseLibraryPickAsync"/>
/// (deterministic first-match fallback when no agent is registered), moves the
/// pick Library → Hand, then shuffles ONCE (CR 701.20a — a single search effect
/// performs one shuffle, whether or not a card was found). The printed "reveal
/// it" step is a no-op signal in v1 — the card still reaches the hand so the
/// observable game state is correct (same gap as every tutor factory).
///
/// ### Dynamic self-pump (CR 613.1g — Layer 7c)
/// "This creature gets +1/+1 for each Desert you control." Implemented via
/// <see cref="DesertPumpStaticEffect"/>, a <see cref="ContinuousEffect"/>
/// subclass that re-counts the controller's Deserts on every
/// <see cref="ContinuousEffectsService.Compute"/> invocation and applies +N/+N
/// to Greenblade's characteristics only (printed base 1/2 stands as the Layer 7c
/// foundation). Same shape as <see cref="TerritorialKavuFactory"/>'s Domain
/// pump. Lifecycle: ETB registers the effect, LTB unregisters it (mirrors
/// Territorial Kavu / Tarmogoyf / Blood Moon — subscribe to
/// <see cref="CardMovedEvent"/>, sync on each relevant move).
///
/// ## Wiring overloads
/// - <see cref="Create(Player)"/> — shape + ETB trigger attached (not
///   registered), self-pump NOT registered (no continuous-effects service).
///   Suitable for shape / dispatcher / ETB-resolve tests. This is the overload
///   <see cref="Majik.Core.CardData.NamedCardFactory"/> dispatches to.
/// - <see cref="Create(Player, ContinuousEffectsService, IEventBus)"/> — fully
///   wired: the Desert self-pump registers against the layers service; when an
///   event bus is supplied, the lifecycle binder registers on ETB / unregisters
///   on LTB.
///
/// ## Deferred (v1 gaps)
/// - <b>"Reveal" UI signal</b>: the tutored card moves Library → Hand without
///   publishing a reveal event — same gap as every tutor factory
///   (<see cref="BorderlandRangerFactory"/>, Cultivate, Solemn Simulacrum).
/// - <b>Layer 4 feed-through in the count</b>: the Desert tally reads printed
///   subtypes; Layer 4 retype effects that grant/remove the Desert subtype are
///   not reflected (same gap as Territorial Kavu's Domain count).
/// </summary>
[CardName("Outcaster Greenblade")]
public static class OutcasterGreenbladeFactory
{
    public const string CardName = "Outcaster Greenblade";
    public const string Slug = "outcaster-greenblade";

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource(Slug);

    /// <summary>
    /// Construct Outcaster Greenblade with the ETB trigger attached to the card
    /// shape but NOT registered with a <see cref="TriggerManager"/>, and no
    /// continuous-effects wiring for the Desert self-pump. Suitable for shape /
    /// dispatcher / ETB-resolve tests.
    /// </summary>
    public static Creature Create(Player owner)
        => Create(owner, effects: null, eventBus: null);

    /// <summary>
    /// Construct Outcaster Greenblade with optional runtime services.
    /// When <paramref name="effects"/> is supplied, a
    /// <see cref="DesertPumpStaticEffect"/> is wired so the +1/+1-per-Desert pump
    /// is evaluated on every <see cref="ContinuousEffectsService.Compute"/> call;
    /// when <paramref name="eventBus"/> is also supplied, the lifecycle binder
    /// subscribes to <see cref="CardMovedEvent"/> so the effect registers on ETB
    /// and unregisters on LTB.
    /// </summary>
    public static Creature Create(
        Player owner,
        ContinuousEffectsService? effects,
        IEventBus? eventBus)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = (Creature)CardDefinitionFactory.Build(Definition, owner);
        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // ETB triggered ability — CR 603.6a.
        //   "When this creature enters, search your library for a basic land
        //    card or a Desert card, reveal it, put it into your hand, then
        //    shuffle."
        // ----------------------------------------------------------------
        var etbEffect = new Effect(
            $"{CardName}: search a basic land or Desert -> hand, then shuffle",
            ctx =>
            {
                var controller = card.Controller ?? owner;
                return TutorBasicOrDesertToHandAsync(controller, ctx);
            });

        var etbTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnEnterBattlefieldSelf(card),
            effects: new IEffect[] { etbEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(etbTrigger);

        // ----------------------------------------------------------------
        // Static "+1/+1 for each Desert you control." Layer 7c
        // (CR 613.1g). Register on ETB, unregister on LTB — mirrors
        // TerritorialKavuFactory's Domain pump lifecycle.
        // ----------------------------------------------------------------
        if (effects != null)
        {
            var lifecycle = new DesertPumpLifecycle(card, owner, effects, eventBus);
            lifecycle.Attach();
        }

        return card;
    }

    /// <summary>
    /// Search <paramref name="player"/>'s library for ONE basic land card
    /// (CR 305.6 — Basic supertype + Land card type) OR one Desert card (the
    /// Desert land subtype, basic or nonbasic), consult the agent (which may
    /// decline; deterministic first-match fallback when no agent), move the pick
    /// Library → Hand, then shuffle once (CR 701.20a). The printed "reveal it"
    /// step is a no-op signal in v1.
    /// </summary>
    private static async ValueTask TutorBasicOrDesertToHandAsync(Player player, ResolutionContext ctx)
    {
        // "basic land card OR a Desert card." A basic land is the Basic
        // supertype + Land card type (CR 305.6); a Desert card is any card with
        // the Desert land subtype (basic or nonbasic).
        static bool IsBasicLandOrDesert(ICard c) =>
            (c.HasType(CardType.Land) && c.HasSupertype(CardSupertype.Basic))
            || c.HasSubtype(CardSubtype.Desert);

        var agent = ctx.Agent ?? AgentRegistry.Get(player);

        var candidates = player.Zones.Library.GetCards().Where(IsBasicLandOrDesert).ToList();
        ICard? pick = null;
        if (candidates.Count > 0)
        {
            pick = agent != null
                ? await agent.ChooseLibraryPickAsync(ctx.Game, candidates,
                        "basic land card or Desert card to put into your hand")
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

        // CR 701.20a — shuffle once after the search, even when zero cards were
        // found (the search still happened).
        LibraryShuffle.ShuffleLibrary(player, Slug);
    }

    /// <summary>
    /// Count the Deserts <paramref name="controller"/> controls (CR 700 — "you
    /// control" is a control filter; the Desert land subtype, basic or nonbasic).
    /// Reads the controller's battlefield live. Pure helper exposed for tests;
    /// mirrors the tally baked into <see cref="DesertPumpStaticEffect"/>.
    /// </summary>
    public static int CountDeserts(Player controller)
    {
        ArgumentNullException.ThrowIfNull(controller);

        var count = 0;
        foreach (var c in controller.Zones.Battlefield.GetCards())
        {
            if (c is Permanent p && p.HasSubtype(CardSubtype.Desert)) count++;
        }
        return count;
    }

    // -----------------------------------------------------------------------
    // DesertPumpStaticEffect — Layer 7c live-count self-pump.
    // -----------------------------------------------------------------------

    /// <summary>
    /// Layer 7c continuous effect for Outcaster Greenblade's
    /// "+1/+1 for each Desert you control." On every
    /// <see cref="ContinuousEffectsService.Compute"/> invocation this re-counts
    /// the controller's Deserts and applies +N/+N to Greenblade only (printed
    /// base 1/2 stands as the foundation). Same shape as
    /// <see cref="TerritorialKavuFactory.DomainPumpStaticEffect"/>.
    /// </summary>
    public sealed class DesertPumpStaticEffect : ContinuousEffect
    {
        private readonly Creature _source;
        private readonly Player _controller;

        public DesertPumpStaticEffect(Creature source, Player controller)
        {
            _source = source ?? throw new ArgumentNullException(nameof(source));
            _controller = controller ?? throw new ArgumentNullException(nameof(controller));
        }

        /// <summary>CR 613.1g — the permanent generating this effect.</summary>
        public override Permanent? Source => _source;

        /// <inheritdoc/>
        public override Layer Layer => Layer.PT_Modify;

        /// <summary>Active while Greenblade is on the battlefield.</summary>
        public override bool IsActive() => _source.Zone == ZoneType.Battlefield;

        /// <summary>Applies only to Greenblade itself.</summary>
        public override bool AppliesTo(Creature creature) =>
            ReferenceEquals(creature, _source);

        /// <summary>
        /// Apply +N/+N where N = Deserts the controller controls (CR 700).
        /// </summary>
        public override void Apply(CreatureCharacteristics chars)
        {
            var n = CountDeserts(_controller);
            chars.Power += n;
            chars.Toughness += n;
        }

        /// <summary>
        /// Sim-only: reconstruct an identical <see cref="DesertPumpStaticEffect"/>
        /// bound to <paramref name="clonedSource"/> for the search-sandbox clone.
        /// The Desert count reads clonedSource.Controller live.
        /// preserves: nothing scalar; source → clonedSource (as Creature); controller → clonedSource.Controller.
        /// </summary>
        internal override ContinuousEffect? CloneForSim(
            Permanent clonedSource,
            Func<IReadOnlyList<Player>>? clonedPlayers)
        {
            if (clonedSource is not Creature clonedCreature) return null;
            var clonedController = clonedCreature.Controller;
            if (clonedController == null) return null;
            return new DesertPumpStaticEffect(clonedCreature, clonedController);
        }
    }

    // -----------------------------------------------------------------------
    // DesertPumpLifecycle — ETB/LTB wiring for the Desert pump effect.
    // -----------------------------------------------------------------------

    /// <summary>
    /// ETB/LTB lifecycle binder for the Desert self-pump. Subscribes to
    /// <see cref="CardMovedEvent"/>; registers
    /// <see cref="DesertPumpStaticEffect"/> when Greenblade enters the
    /// battlefield, unregisters when it leaves. Mirrors
    /// <see cref="TerritorialKavuFactory"/>'s <c>DomainPumpLifecycle</c>.
    /// </summary>
    private sealed class DesertPumpLifecycle
    {
        private readonly Creature _source;
        private readonly Player _controller;
        private readonly ContinuousEffectsService _effects;
        private readonly IEventBus? _eventBus;
        private readonly Action<CardMovedEvent> _handler;
        private DesertPumpStaticEffect? _registered;
        private bool _attached;

        public DesertPumpLifecycle(
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
            if (shouldBeActive && _registered == null)
            {
                _registered = new DesertPumpStaticEffect(_source, _controller);
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
