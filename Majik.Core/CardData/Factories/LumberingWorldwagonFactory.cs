using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Primitives;
using Majik.Core.Services;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Lumbering Worldwagon (Edge of Eternities, {2}{G}).
///
/// Artifact — Vehicle, printed P/T */4. Oracle text (Scryfall, verified):
///   "This Vehicle's power is equal to the number of lands you control.
///    Whenever this Vehicle enters or attacks, you may search your library for
///    a basic land card, put it onto the battlefield tapped, then shuffle.
///    Crew 4"
///
/// ## Shape source
/// Card identity (name, {2}{G}, */4, Artifact + Creature shell, Vehicle
/// subtype) is loaded from <c>Majik.Core/CardData/Cards/lumbering-worldwagon.json</c>
/// via <see cref="CardDefinitionLoader.FromEmbeddedResource"/> and built
/// through <see cref="CardDefinitionFactory"/> — the same JSON-driven Vehicle
/// shape as <see cref="CryptcallerChariotFactory"/> / Cultivator's Caravan.
/// The CDA P/T + the enter/attack tutor are layered on in code (the JSON
/// ability schema expresses neither a CDA nor a "search → battlefield tapped"
/// effect).
///
/// ## Implemented (v1)
/// - <b>Artifact — Vehicle */4 at {2}{G}.</b> The Vehicle shell is a
///   <see cref="Creature"/> with <see cref="CardType.Artifact"/> stamped
///   (CR 301.1 / 302.1 — the "Artifact Vehicle" multi-type pattern; the
///   printed power is <c>*</c>, seeded 0 per CR 208.2c and overwritten by the
///   Layer-7a CDA, toughness is the fixed printed 4). <see cref="CrewCost"/>
///   surfaces Crew 4 so callers route through
///   <see cref="CardData.Vehicles.CrewAction.Crew"/> — the same structural
///   crew posture as <see cref="EsikasChariotFactory"/> /
///   <see cref="CryptcallerChariotFactory"/> (no activated-ability surface
///   yet; the engine's <c>CrewAction</c> is invoked directly by tests / bots,
///   and once crewed the 4 base toughness ships through
///   <see cref="VehicleCrewEffect"/>).
///
/// - <b>Characteristic-defining power (CR 604.3 / 613.2 Layer 7a).</b>
///   "This Vehicle's power is equal to the number of lands you control." Power
///   is driven by <see cref="CdaPowerToughnessEffect"/> whose evaluator counts
///   every <see cref="CardType.Land"/> in the caller-supplied "lands you
///   control" source (typically <c>() =&gt; controller.Zones.Battlefield.GetCards()</c>),
///   mirroring <see cref="LumraBellowOfTheWoodsFactory"/>. ONLY power is a CDA
///   — toughness stays the fixed printed 4, so the toughness evaluator returns
///   4 (we still drive it through Layer 7a so later 7c pumps / counters stack
///   uniformly per CR 613.7). The CDA registers on ETB / unregisters on LTB
///   via a <see cref="CardMovedEvent"/> lifecycle (mirrors Lumra / Nighthawk
///   Scavenger).
///
/// - <b>Enter-or-attack tutor (CR 603.1 / 603.6a / 508.1f).</b> "Whenever this
///   Vehicle enters or attacks, you may search your library for a basic land
///   card, put it onto the battlefield tapped, then shuffle." Modelled as TWO
///   triggered abilities sharing one resolve body — an ETB trigger
///   (<see cref="Triggers.OnEnterBattlefieldSelf"/>) and an attack trigger
///   (<see cref="Triggers.OnAttackSelf"/>) — exactly the
///   <see cref="SolemnSimulacrumFactory"/> tutor body: search for ONE basic
///   land (CR 305.6 — Basic supertype + Land card type), consult the
///   registered <see cref="IPlayerAgent"/> (CR 701.19a — "you may" + the
///   search may fail to find, both legal), move the pick Library →
///   Battlefield through <see cref="ZoneServiceRegistry"/> so ETB-tapped
///   replacements + <c>CardMovedEvent</c> subscribers fire, apply the printed
///   "tapped" rider (CR 701.18), then shuffle ONCE (CR 701.20a). Deterministic
///   first-basic fallback when no agent is registered.
///
/// ## Wiring overloads
/// - <see cref="Create(Player)"/> — card shape only. CDA + both triggers
///   attached as markers but no <see cref="ContinuousEffectsService"/> /
///   <see cref="TriggerManager"/> wiring, so power falls back to the printed-0
///   seed and triggers don't auto-stack. Suitable for shape / dispatcher /
///   crew tests.
/// - <see cref="Create(Player, ContinuousEffectsService?, IEventBus?, Func{IEnumerable{ICard}}?, TriggerManager?)"/>
///   — fully wired: the CDA registers/unregisters across the battlefield and
///   both triggers register with the <see cref="TriggerManager"/>.
///
/// ## Deferred (v1)
/// - "You may" auto-accepts (the search consults the agent, which may decline
///   the pick) — consistent with the tutor factory family.
/// - Tutored basic moves Library → Battlefield without a reveal event — same
///   gap as every tutor factory.
/// - Vehicle-as-non-creature off the battlefield: the shell is a
///   <see cref="Creature"/>, the standard v1 Vehicle simplification.
/// </summary>
[CardName("Lumbering Worldwagon")]
public static class LumberingWorldwagonFactory
{
    public const string CardName = "Lumbering Worldwagon";

    /// <summary>JSON slug for the embedded card definition.</summary>
    public const string Slug = "lumbering-worldwagon";

    /// <summary>Crew cost (CR 702.122) — total tapped power ≥ 4 crews it.</summary>
    public const int CrewCost = 4;

    /// <summary>Vehicle base toughness, shipped through VehicleCrewEffect once crewed.</summary>
    public const int VehicleToughness = 4;

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource(Slug);

    /// <summary>
    /// Construct Lumbering Worldwagon with no live continuous-effects /
    /// trigger-manager wiring. The CDA + both enter/attack triggers are
    /// attached as markers; power falls back to the printed-0 seed and the
    /// triggers don't auto-stack. Suitable for shape / dispatcher / crew tests.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, effects: null, eventBus: null, landsYouControlSource: null, triggers: null);

    /// <summary>
    /// Construct Lumbering Worldwagon with optional runtime services.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="effects">Continuous-effects service the CDA power
    /// (<see cref="CdaPowerToughnessEffect"/>) registers against. May be null —
    /// power then falls back to the printed-0 seed.</param>
    /// <param name="eventBus">Event bus for the CDA's ETB/LTB lifecycle
    /// (<see cref="CardMovedEvent"/>). May be null — the CDA's battlefield gate
    /// still covers correctness, but no explicit unregister fires.</param>
    /// <param name="landsYouControlSource">Closure returning the cards to count
    /// for "lands you control" — typically
    /// <c>() =&gt; controller.Zones.Battlefield.GetCards()</c>. The CDA filters
    /// to <see cref="CardType.Land"/>. Read fresh on every Compute. May be null
    /// (CDA not wired).</param>
    /// <param name="triggers">TriggerManager the enter + attack triggers are
    /// registered with so the relevant events land them on the stack
    /// (CR 603.3). May be null.</param>
    public static Creature Create(
        Player owner,
        ContinuousEffectsService? effects,
        IEventBus? eventBus,
        Func<IEnumerable<ICard>>? landsYouControlSource,
        TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = (Creature)CardDefinitionFactory.Build(Definition, owner);
        card.SetOwner(owner);
        card.SetController(owner);

        // CR 604.3 / 613.2 Layer 7a — "This Vehicle's power is equal to the
        // number of lands you control." ONLY power is the CDA; toughness is
        // the fixed printed 4.
        if (effects != null && landsYouControlSource != null)
        {
            var lifecycle = new WorldwagonCdaLifecycle(card, effects, eventBus, landsYouControlSource);
            lifecycle.Attach();
        }

        // ----------------------------------------------------------------
        // Enter-or-attack tutor — CR 603.1 / 603.6a (enters) + 508.1f
        // (attacks). "Whenever this Vehicle enters or attacks, you may search
        // your library for a basic land card, put it onto the battlefield
        // tapped, then shuffle." Two triggered abilities share one resolve
        // body (the Solemn Simulacrum tutor body).
        // ----------------------------------------------------------------
        IEffect MakeTutorEffect(string trigger) => new Effect(
            $"{CardName} {trigger}: search a basic land -> battlefield tapped, then shuffle",
            async ctx =>
            {
                var controller = card.Controller ?? owner;
                await TutorOneBasicToBattlefieldTappedAsync(controller, ctx).ConfigureAwait(false);
            });

        var enterTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnEnterBattlefieldSelf(card),
            effects: new[] { MakeTutorEffect("enters") },
            // CR 113.6 — functions only from the battlefield.
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(enterTrigger);
        triggers?.RegisterTriggeredAbility(enterTrigger);

        var attackTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnAttackSelf(card),
            effects: new[] { MakeTutorEffect("attacks") },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(attackTrigger);
        triggers?.RegisterTriggeredAbility(attackTrigger);

        return card;
    }

    /// <summary>
    /// Count "lands you control" among the supplied cards (CR 604.3). Pure
    /// helper exposed for tests; mirrors the closure baked into the live CDA.
    /// </summary>
    public static int CountLands(IEnumerable<ICard> cards)
    {
        ArgumentNullException.ThrowIfNull(cards);
        return cards.Count(c => c.HasType(CardType.Land));
    }

    /// <summary>
    /// Search <paramref name="player"/>'s library for ONE basic land card
    /// (CR 305.6 — Basic supertype + Land card type), consult the agent (which
    /// may decline; deterministic first-basic fallback when no agent), move the
    /// pick to the battlefield with the printed "tapped" rider applied after
    /// the move (CR 701.18), then shuffle once (CR 701.20a). Mirrors
    /// <see cref="SolemnSimulacrumFactory"/>.
    /// </summary>
    private static async ValueTask TutorOneBasicToBattlefieldTappedAsync(Player player, ResolutionContext ctx)
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
                        "basic land card to put onto the battlefield tapped")
                    .ConfigureAwait(false)
                : candidates[0];
        }

        if (pick != null)
        {
            var zones = ZoneServiceRegistry.Get(player);
            if (zones != null)
            {
                zones.MoveCard(pick, ZoneType.Library, ZoneType.Battlefield, player);
                if (pick is Permanent perm && !perm.IsTapped) perm.Tap();
            }
            else
            {
                player.Zones.Library.RemoveCard(pick);
                player.Zones.Battlefield.AddCard(pick);
                pick.SetZone(ZoneType.Battlefield);
                pick.SetController(player);
                if (pick is Permanent perm) perm.Tap();
            }
        }

        // CR 701.20a — shuffle once after the search, even when zero cards
        // were found (the search still happened).
        LibraryShuffle.ShuffleLibrary(player, Slug);
    }

    /// <summary>
    /// ETB/LTB lifecycle binder for the CDA power. Subscribes to
    /// <see cref="CardMovedEvent"/>; registers a
    /// <see cref="CdaPowerToughnessEffect"/> when the Vehicle enters the
    /// battlefield, unregisters when it leaves. Mirrors
    /// <see cref="LumraBellowOfTheWoodsFactory"/>. Power = lands you control;
    /// toughness is the fixed printed 4.
    /// </summary>
    private sealed class WorldwagonCdaLifecycle
    {
        private readonly Creature _source;
        private readonly ContinuousEffectsService _effects;
        private readonly IEventBus? _eventBus;
        private readonly Func<IEnumerable<ICard>> _landsSource;
        private readonly Action<CardMovedEvent> _handler;
        private CdaPowerToughnessEffect? _registered;
        private bool _attached;

        public WorldwagonCdaLifecycle(
            Creature source,
            ContinuousEffectsService effects,
            IEventBus? eventBus,
            Func<IEnumerable<ICard>> landsSource)
        {
            _source = source;
            _effects = effects;
            _eventBus = eventBus;
            _landsSource = landsSource;
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
                _registered = new CdaPowerToughnessEffect(
                    _source,
                    // CR 604.3 — power = number of lands you control.
                    powerOf: _ => CountLands(_landsSource()),
                    // Toughness is the fixed printed 4 (not a CDA).
                    toughnessOf: _ => VehicleToughness);
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
