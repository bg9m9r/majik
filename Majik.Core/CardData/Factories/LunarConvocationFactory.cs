using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Primitives;
using Majik.Core.Services;
using Majik.Core.StateMachine;
using Majik.Core.Tokens;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Lunar Convocation (Murders at Karlov Manor
/// Commander, {W}{B}). Enchantment. Oracle text (verified against Scryfall
/// 2026-06-23, scryfallId 4396e4c7-660d-4055-bc94-4ccea95223b7):
///   "At the beginning of your end step, if you gained life this turn, each
///    opponent loses 1 life.
///    At the beginning of your end step, if you gained and lost life this
///    turn, create a 1/1 black Bat creature token with flying.
///    {1}{B}, Pay 2 life: Draw a card."
///
/// The base shape (name, single Enchantment card type, {W}{B}) is
/// materialised from the embedded JSON definition
/// (<c>lunar-convocation.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/> — same posture as
/// <see cref="WeddingAnnouncementFactory"/> / <see cref="SanguineBondFactory"/>.
/// The two end-step triggers and the pay-life draw activated ability are
/// layered on here because the JSON <c>AbilityDefinition</c> schema expresses
/// none of them.
///
/// ## Implemented (v1)
/// - <b>"At the beginning of your end step, if you gained life this turn, each
///   opponent loses 1 life." (CR 603.1 + CR 603.4 intervening-if + CR 119.3)</b>:
///   a <see cref="TriggeredAbility"/> scoped to the controller's own End step
///   via <see cref="Triggers.OnStepBegin"/> (= "your end step", CR 500.7 —
///   active-player-filtered, unlike Resplendent Angel's "each end step"). The
///   intervening-if "if you gained life this turn" (CR 603.4) is evaluated at
///   resolution against a per-turn life-gained latch — the engine has no
///   built-in <c>LifeGainedThisTurn</c> accumulator (only
///   <see cref="Player.LifeLostThisTurn"/>), so the factory maintains its own
///   exactly as <see cref="ResplendentAngelFactory"/> does: a
///   <see cref="LifeChangedEvent"/> subscription sums positive deltas
///   (NewLife &gt; PreviousLife) for the controller, reset on
///   <see cref="TurnStartedEvent"/>. On resolution (latch &gt; 0) each opponent
///   loses 1 life, read LIVE from the resolution context via
///   <see cref="ContextOpponents"/> (resolver-null bug class — same posture as
///   <see cref="CruelCelebrantFactory"/> / <see cref="EnduringTenacityFactory"/>).
/// - <b>"At the beginning of your end step, if you gained and lost life this
///   turn, create a 1/1 black Bat creature token with flying."
///   (CR 603.1 + CR 603.4 + CR 111.4 token)</b>: a second
///   <see cref="TriggeredAbility"/> on the controller's End step. The
///   intervening-if requires BOTH gained life (the latch &gt; 0) AND lost life
///   this turn (<see cref="Player.LifeLostThisTurn"/> &gt; 0 — note paying 2
///   life for the draw ability counts as losing life, CR 118.8 / 119.4, so the
///   activated ability can feed this trigger). On resolution a single 1/1 black
///   Bat token with Flying is minted under the controller via
///   <see cref="TokenFactory.CreateOnBattlefield"/> (count-1 overload, routed
///   through <see cref="ZoneService.Replacements"/> so token doublers can
///   rewrite the count, CR 614 / CR 616.1c).
/// - <b>"{1}{B}, Pay 2 life: Draw a card." (CR 602 activated ability)</b>: an
///   <see cref="ActivatedAbility"/> whose costs are
///   <see cref="ManaCostCost"/>("{1}{B}") + <see cref="PayLifeCost"/>(2) and
///   which targets nothing. On resolution the controller draws one card via
///   <see cref="Fx.DrawCards"/> (CR 120 — routes a DrawCardIntent through the
///   ReplacementBus). No sorcery-speed gate is printed (CR 602.5a instant
///   speed); repeatable.
///
/// ## Wiring overloads
/// - <see cref="Create(Player)"/> — shape only. Both end-step triggers + the
///   activated ability are attached for shape observability; without an event
///   bus the life-gained latch is never updated, without a
///   <see cref="TriggerManager"/> the triggers aren't bus-driven. This is the
///   overload <see cref="NamedCardFactory"/> dispatches to.
/// - <see cref="Create(Player, ZoneService?, IEventBus?, TriggerManager?)"/> —
///   fully wired.
///
/// ## Deferred (v1 gaps)
/// - None — the token is mandatory and fully specified, "each opponent" is the
///   live opponent set (unambiguous), and the pay-life draw has no choices.
/// </summary>
[CardName("Lunar Convocation")]
public static class LunarConvocationFactory
{
    public const string CardName = "Lunar Convocation";
    public const string Slug = "lunar-convocation";

    /// <summary>Mana portion of the pay-life draw activated ability.</summary>
    public const string DrawManaCost = "{1}{B}";

    /// <summary>Life paid for the draw activated ability (CR 118.8 / 119.4).</summary>
    public const int DrawLifeCost = 2;

    /// <summary>Life each opponent loses on the first end-step trigger.</summary>
    public const int DrainAmount = 1;

    /// <summary>
    /// Construct Lunar Convocation with no live wiring. Both end-step triggers
    /// + the pay-life draw activated ability are attached structurally; the
    /// life-gained latch is never updated (no event bus) and the triggers are
    /// not bus-registered (no <see cref="TriggerManager"/>). Suitable for shape
    /// / dispatcher tests. This is the overload <see cref="NamedCardFactory"/>
    /// dispatches to.
    /// </summary>
    public static Enchantment Create(Player owner)
        => Create(owner, zoneService: null, eventBus: null, triggers: null);

    /// <summary>
    /// Construct Lunar Convocation with optional runtime services.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="zoneService">Routes the minted Bat token through
    /// <see cref="ZoneService"/> (so <see cref="CardMovedEvent"/> + token
    /// doublers fire). Null → token minted directly with no event/doubler
    /// plumbing.</param>
    /// <param name="eventBus">Subscribes the per-turn "gained life this turn"
    /// latch (positive <see cref="LifeChangedEvent"/> deltas for the
    /// controller, reset on <see cref="TurnStartedEvent"/>). Null → latch stays
    /// at 0 so the intervening-ifs never fire.</param>
    /// <param name="triggers">Registers both end-step triggers for bus-driven
    /// firing. Null → triggers are attached to the card but not bus-driven.</param>
    public static Enchantment Create(
        Player owner,
        ZoneService? zoneService,
        IEventBus? eventBus,
        TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, single
        // Enchantment card type, {W}{B}). The JSON carries no abilities — all
        // three printed behaviours are layered on below.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Enchantment)CardDefinitionFactory.Build(definition, owner);

        // ----------------------------------------------------------------
        // Per-turn "gained life this turn" latch — subscribed when an event
        // bus is supplied. The engine has no built-in LifeGainedThisTurn
        // accumulator (only Player.LifeLostThisTurn), so we maintain our own
        // running total. Closure-captured so both end-step trigger bodies
        // inspect it at resolution (CR 603.4 intervening-if). Same idiom as
        // ResplendentAngelFactory.
        // ----------------------------------------------------------------
        var lifeGainedThisTurn = new int[] { 0 };

        if (eventBus != null)
        {
            eventBus.Subscribe<LifeChangedEvent>(e =>
            {
                // "you gained life" — the controller. CR 119.3 — only a strict
                // increase counts as life gain. Read controller live so a
                // control change routes the latch to the current controller.
                var controller = card.Controller ?? owner;
                if (!ReferenceEquals(e.Player, controller)) return;
                if (e.NewLife <= e.PreviousLife) return;
                lifeGainedThisTurn[0] += e.NewLife - e.PreviousLife;
            });

            // CR 500.7 — the running total is per-turn; reset at each turn
            // start (mirrors ResplendentAngel's latch reset). The "lost life
            // this turn" side reads Player.LifeLostThisTurn, which the engine
            // already resets per turn via Player.ResetTurnTrackers.
            eventBus.Subscribe<TurnStartedEvent>(_ => lifeGainedThisTurn[0] = 0);
        }

        // ----------------------------------------------------------------
        // "At the beginning of your end step, if you gained life this turn,
        //  each opponent loses 1 life." CR 603.1 + CR 603.4 (intervening-if)
        //  + CR 119.3.
        // "your end step" → controller-filtered via Triggers.OnStepBegin.
        // ----------------------------------------------------------------
        var drainEffect = new Effect(
            $"{CardName}: each opponent loses {DrainAmount} life if you gained life this turn",
            ctx =>
            {
                if (card.Zone != ZoneType.Battlefield) return ValueTask.CompletedTask;
                // CR 603.4 — intervening-if checked at resolution.
                if (lifeGainedThisTurn[0] <= 0) return ValueTask.CompletedTask;

                var controller = card.Controller ?? owner;
                foreach (var opp in ContextOpponents.Of(ctx, controller))
                {
                    opp.LoseLife(DrainAmount);
                }
                return ValueTask.CompletedTask;
            });

        var drainTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnStepBegin(owner, StepStateType.End),
            effects: new IEffect[] { drainEffect },
            activeZones: new[] { ZoneType.Battlefield });
        card.AddAbility(drainTrigger);
        triggers?.RegisterTriggeredAbility(drainTrigger);

        // ----------------------------------------------------------------
        // "At the beginning of your end step, if you gained and lost life this
        //  turn, create a 1/1 black Bat creature token with flying."
        //  CR 603.1 + CR 603.4 + CR 111.4.
        // Intervening-if requires BOTH gained life (latch > 0) AND lost life
        // (Player.LifeLostThisTurn > 0) this turn.
        // ----------------------------------------------------------------
        var batEffect = new Effect(
            $"{CardName}: create a 1/1 black Bat token with flying " +
            "if you gained and lost life this turn",
            () =>
            {
                if (card.Zone != ZoneType.Battlefield) return;
                var controller = card.Controller ?? owner;
                // CR 603.4 — intervening-if checked at resolution: gained AND
                // lost life this turn.
                if (lifeGainedThisTurn[0] <= 0) return;
                if (controller.LifeLostThisTurn <= 0) return;

                CreateBatToken(controller, zoneService);
            });

        var batTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnStepBegin(owner, StepStateType.End),
            effects: new IEffect[] { batEffect },
            activeZones: new[] { ZoneType.Battlefield });
        card.AddAbility(batTrigger);
        triggers?.RegisterTriggeredAbility(batTrigger);

        // ----------------------------------------------------------------
        // "{1}{B}, Pay 2 life: Draw a card." CR 602 activated ability.
        // Costs = ManaCostCost("{1}{B}") + PayLifeCost(2); no target. On
        // resolve the controller draws one card (CR 120) via Fx.DrawCards so a
        // DrawCardIntent routes through the ReplacementBus.
        // ----------------------------------------------------------------
        var drawEffect = new Effect(
            $"{CardName}: draw a card",
            () =>
            {
                var controller = card.Controller ?? owner;
                Fx.DrawCards(controller, 1);
            });

        card.AddAbility(new ActivatedAbility(
            source: card,
            controller: owner,
            costs: new ICost[] { new ManaCostCost(DrawManaCost), new PayLifeCost(DrawLifeCost) },
            effects: new IEffect[] { drawEffect }));

        return card;
    }

    /// <summary>
    /// CR 111.4 — create a single 1/1 black Bat creature token with Flying
    /// under <paramref name="controller"/>'s control. Routed through the
    /// count-1 token overload so token doublers can rewrite the count
    /// (CR 614 / CR 616.1c) when a <see cref="ZoneService"/> with a
    /// <see cref="ReplacementBus"/> is wired.
    /// </summary>
    private static void CreateBatToken(Player controller, ZoneService? zones)
    {
        var spec = new TokenFactory.TokenSpec(
            Name: "Bat",
            Power: 1,
            Toughness: 1,
            Subtypes: new[] { CardSubtype.Bat },
            Keywords: new[] { "Flying" },
            // CR 105 — printed "1/1 black Bat creature token".
            Colors: new[] { ManaColor.Black });

        TokenFactory.CreateOnBattlefield(
            spec, controller, count: 1, zones, zones?.Replacements);
    }
}
