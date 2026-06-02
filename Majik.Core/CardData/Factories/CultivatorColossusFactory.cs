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
/// Named-card factory for Cultivator Colossus (The Brothers' War,
/// {4}{G}{G}{G}).
///
/// Creature — Plant Beast. Oracle text:
///   "Trample
///    Cultivator Colossus's power and toughness are each equal to the number
///    of lands you control.
///    When this creature enters, you may put a land card from your hand onto
///    the battlefield tapped. If you do, draw a card and repeat this process."
///
/// ## Shape source
/// Card identity (name, {4}{G}{G}{G}, Creature — Plant Beast, base 0/0) is
/// loaded from <c>Majik.Core/CardData/Cards/cultivator-colossus.json</c> via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> and built through
/// <see cref="CardDefinitionFactory"/> (same posture as
/// <see cref="GrowthSpiralFactory"/> / <see cref="BorderlandRangerFactory"/>).
/// The Trample marker, the Layer 7a CDA, and the ETB triggered ability are
/// layered on in code below — none of those ability shapes is expressed in the
/// JSON schema yet.
///
/// ## Implemented (v1)
/// - <b>Trample</b> (CR 702.19) as a <see cref="KeywordAbility"/> marker,
///   read by the combat-damage assignment path (same posture as
///   <see cref="AkoumWarriorFactory"/> / <see cref="AkromaAngelOfWrathFactory"/>).
/// - <b>Variable P/T — Layer 7a CDA (CR 604.3 / CR 613.2)</b>: power =
///   toughness = number of lands the controller controls at compute time.
///   Implemented via <see cref="CdaPowerToughnessEffect"/>; the evaluator reads
///   the controller's battlefield live on every
///   <see cref="ContinuousEffectsService.Compute(Permanent)"/>. Lifecycle
///   mirrors <see cref="AllosaurusRiderFactory"/> — an inner
///   <see cref="CdaLifecycle"/> registers the CDA when the Colossus enters the
///   battlefield and unregisters it when it leaves. (Allosaurus Rider adds 1;
///   the Colossus does NOT — its P/T is exactly the land count.)
/// - <b>ETB triggered ability — CR 603.1</b>: "When this creature enters, you
///   may put a land card from your hand onto the battlefield tapped. If you do,
///   draw a card and repeat this process." Modeled as a loop in
///   <see cref="BuildEtbEffect"/>: each iteration optionally puts ONE land card
///   from hand onto the battlefield tapped (CR 113.6c — alt-zone "play", NOT a
///   land drop, so <see cref="Majik.Core.Game.LandDropTracker"/> is untouched);
///   if a land was placed, the controller draws a card (CR 121.1) and the loop
///   repeats. The loop stops the first time the controller declines or has no
///   land in hand (CR 117.1a — the "you may" gates each iteration). The drawn
///   card is itself a candidate for the next iteration (the draw happens before
///   the next "put a land"), so a freshly-drawn land can keep the chain going.
///   Land moves route through a live <see cref="ZoneService"/> when supplied so
///   ETB-on-land triggers / replacements fire (CR 603.6a — bounce-land bounce,
///   Lotus Cobra landfall); the post-move <see cref="Permanent.Tap"/> is
///   idempotent so an ETB-tapped land double-tapping is a no-op.
///
/// ## Deferred (v1 gaps)
/// - <b>"You may" prompt without an agent</b>: the no-agent fallback always
///   plays the first land in hand each iteration (driving the chain as far as
///   the hand + draws allow). A registered agent gets the full opt-in +
///   land-pick prompts per iteration. Same simplification every "may" factory
///   carries (Uro / Growth Spiral / Sakura-Tribe Scout).
/// </summary>
[CardName("Cultivator Colossus")]
public static class CultivatorColossusFactory
{
    public const string CardName = "Cultivator Colossus";

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("cultivator-colossus");

    /// <summary>
    /// Construct Cultivator Colossus with correct identity + Trample + the ETB
    /// trigger attached to the card shape, but NO live CDA or
    /// <see cref="TriggerManager"/> wiring. Suitable for shape / dispatcher
    /// tests. The ETB body is exposed via <see cref="BuildEtbEffect"/>.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, effects: null, zoneService: null, eventBus: null, triggers: null);

    /// <summary>
    /// Construct a fully-wired Cultivator Colossus.
    /// <para>
    /// When <paramref name="effects"/> is supplied, a
    /// <see cref="CdaPowerToughnessEffect"/> is registered/unregistered as the
    /// Colossus enters/leaves the battlefield via <see cref="CardMovedEvent"/>
    /// on <paramref name="eventBus"/> (Layer 7a CDA, CR 604.3).
    /// </para>
    /// <para>
    /// When <paramref name="triggers"/> is supplied, the ETB trigger is
    /// registered so a dispatched <c>CardMovedEvent</c> places it on the stack
    /// automatically (CR 603.3). When <paramref name="zoneService"/> is supplied
    /// the ETB land-moves route through it so ETB-on-land triggers fire.
    /// </para>
    /// </summary>
    public static Creature Create(
        Player owner,
        ContinuousEffectsService? effects,
        ZoneService? zoneService,
        IEventBus? eventBus,
        TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base P/T = 0/0 (CR 208.2c — printed */* treated as 0; the Layer 7a
        // CDA overwrites on every Compute).
        var card = (Creature)CardDefinitionFactory.Build(Definition, owner);
        card.SetOwner(owner);
        card.SetController(owner);

        // CR 702.19 — Trample, as a KeywordAbility marker read by the
        // combat-damage assignment path.
        card.AddAbility(new KeywordAbility("Trample", card, owner));

        // ----------------------------------------------------------------
        // Layer 7a CDA — P/T = number of lands the controller controls
        // (CR 604.3 / CR 613.2). Lifecycle mirrors AllosaurusRiderFactory.
        // ----------------------------------------------------------------
        if (effects != null)
        {
            var lifecycle = new CdaLifecycle(card, effects, eventBus);
            lifecycle.Attach();
        }

        // ----------------------------------------------------------------
        // ETB triggered ability — CR 603.1.
        //   "When this creature enters, you may put a land card from your
        //    hand onto the battlefield tapped. If you do, draw a card and
        //    repeat this process."
        // ----------------------------------------------------------------
        var etbEffect = new Effect(
            $"{CardName}: ETB — loop (put a land from hand tapped; if you do, draw and repeat)",
            ctx =>
            {
                var controller = card.Controller ?? owner;
                return PutLandLoopAsync(controller, zoneService, ctx);
            });

        var etbTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnEnterBattlefieldSelf(card),
            effects: new IEffect[] { etbEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(etbTrigger);
        triggers?.RegisterTriggeredAbility(etbTrigger);

        return card;
    }

    /// <summary>
    /// Count the lands the controller controls at compute time. Pure helper
    /// exposed for tests (CR 109.5 — only the controller's lands count).
    /// </summary>
    public static int CountControllerLands(Creature colossus)
    {
        ArgumentNullException.ThrowIfNull(colossus);
        return colossus.Controller?.Zones.Battlefield.GetCards()
            .Count(c => c.HasType(CardType.Land)) ?? 0;
    }

    /// <summary>
    /// Build Cultivator Colossus's ETB effect as a standalone resolve body —
    /// the same closure the ETB trigger wraps. Exposed so tests / bots can
    /// exercise the loop directly. See <see cref="PutLandLoopAsync"/> for the
    /// rules detail.
    /// </summary>
    public static IReadOnlyList<IEffect> BuildEtbEffect(Player controller, ZoneService? zoneService)
    {
        ArgumentNullException.ThrowIfNull(controller);

        return new IEffect[]
        {
            new Effect(
                $"{CardName}: ETB — loop (put a land from hand tapped; if you do, draw and repeat)",
                ctx => PutLandLoopAsync(controller, zoneService, ctx)),
        };
    }

    /// <summary>
    /// CR 603.1 ETB loop. Each iteration:
    ///   1. CR 117.1a — optional "you may put a land card from your hand onto
    ///      the battlefield tapped". Agent-driven opt-in + land-pick when an
    ///      agent is registered (intent <see cref="BotIntent.Ramp"/>); no-agent
    ///      fallback auto-accepts and takes the first land in hand. No land in
    ///      hand → stop.
    ///   2. If a land was placed: the land enters tapped (CR 113.6c — alt-zone
    ///      "play", NOT a land drop), then CR 121.1 — "draw a card", then the
    ///      loop repeats. The draw happens BEFORE the next "put a land", so a
    ///      freshly-drawn land is a legal candidate for the next iteration.
    ///   3. If the controller declines / has no land → stop ("repeat this
    ///      process" only continues while a land keeps being placed).
    /// A safety bound caps the loop at the controller's current hand size + a
    /// margin so a pathological zone-service bug can never spin forever (each
    /// successful iteration consumes one land from hand and the net hand count
    /// is bounded by draws; the cap is generous and unreachable in normal play).
    /// </summary>
    private static async ValueTask PutLandLoopAsync(Player controller, ZoneService? zoneService, ResolutionContext ctx)
    {
        var agent = ctx.Agent ?? AgentRegistry.Get(controller);

        // CR 720 / 718 — guard against an unbounded loop from a faulty zone
        // service. Each successful iteration nets at most one extra card into
        // hand (draw 1, played 1), so the library size is a hard upper bound on
        // how many NEW lands can ever appear; cap defensively well above that.
        int safetyCap = controller.Zones.Hand.GetCards().Count()
            + controller.Zones.Library.GetCards().Count() + 1;

        for (int iteration = 0; iteration < safetyCap; iteration++)
        {
            var candidates = controller.Zones.Hand.GetCards()
                .Where(c => c.HasType(CardType.Land))
                .ToList();
            if (candidates.Count == 0) return; // No land in hand → stop.

            ICard? land;
            if (agent != null)
            {
                // CR 117.1a — optional "you may", resolved by the agent.
                var optIn = await agent.ChooseYesNoAsync(
                        "Put a land card from your hand onto the battlefield tapped?",
                        BotIntent.Ramp)
                    .ConfigureAwait(false);
                if (!optIn) return; // Declining stops the loop (CR 603.1).

                land = await agent.ChooseFromHandAsync(controller, candidates, BotIntent.Ramp)
                    .ConfigureAwait(false);
                // CR 608.2b — re-validate the agent's pick at resolution.
                if (land == null || !candidates.Contains(land)) return;
            }
            else
            {
                // No-agent fallback: auto-accept + first land (v1 posture shared
                // with Uro / Growth Spiral / Sakura-Tribe Scout).
                land = candidates[0];
            }

            // CR 113.6c — put the land onto the battlefield tapped. Route
            // through ZoneService.MoveCard when available so ETB-on-land
            // triggers / replacements fire (CR 603.6a); tap AFTER the move so
            // any ETB-tapped replacement already ran (double-tap is a no-op).
            PutLandTapped(controller, land, zoneService);

            // CR 121.1 — "If you do, draw a card." Empty library stamps the
            // CR 704.5b pending-loss flag (no throw) and the loop continues to
            // its next "you may" (which finds no new land unless one is in
            // hand), so this terminates naturally.
            Fx.DrawCards(controller, 1);

            // "...and repeat this process." — loop continues.
        }
    }

    /// <summary>
    /// Move <paramref name="land"/> from <paramref name="controller"/>'s hand to
    /// their battlefield tapped. Prefers <paramref name="zoneService"/>, then
    /// the <see cref="ZoneServiceRegistry"/>, then raw zone manipulation (the
    /// shape/test path). Mirrors <see cref="PrimevalTitanFactory"/>'s
    /// tapped-arrival helper.
    /// </summary>
    private static void PutLandTapped(Player controller, ICard land, ZoneService? zoneService)
    {
        var effectiveZones = zoneService ?? ZoneServiceRegistry.Get(controller);
        if (effectiveZones != null)
        {
            effectiveZones.MoveCard(land, ZoneType.Hand, ZoneType.Battlefield, controller);
            if (land is Permanent perm && !perm.IsTapped)
            {
                perm.Tap();
            }
        }
        else
        {
            controller.Zones.Hand.RemoveCard(land);
            controller.Zones.Battlefield.AddCard(land);
            land.SetZone(ZoneType.Battlefield);
            land.SetController(controller);
            if (land is Permanent perm)
            {
                perm.Tap();
            }
        }
    }

    /// <summary>
    /// ETB/LTB lifecycle binder for the Layer 7a CDA. Subscribes to
    /// <see cref="CardMovedEvent"/>; registers a
    /// <see cref="CdaPowerToughnessEffect"/> (P/T = land count) when the
    /// Colossus enters the battlefield, unregisters when it leaves. Mirrors
    /// <see cref="AllosaurusRiderFactory"/>'s inner lifecycle class.
    /// </summary>
    private sealed class CdaLifecycle
    {
        private readonly Creature _source;
        private readonly ContinuousEffectsService _effects;
        private readonly IEventBus? _eventBus;
        private readonly Action<CardMovedEvent> _handler;
        private CdaPowerToughnessEffect? _registered;
        private bool _attached;

        public CdaLifecycle(
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
            if (shouldBeActive && _registered == null)
            {
                _registered = new CdaPowerToughnessEffect(
                    _source,
                    powerOf: _ => CountControllerLands(_source),
                    toughnessOf: _ => CountControllerLands(_source));
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
