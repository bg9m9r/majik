using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.StateMachine;
using Majik.Core.Tokens;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Resplendent Angel (Core Set 2019, {1}{W}{W}).
/// Creature — Angel 3/3. Oracle text (verified against Scryfall):
///   "Flying
///    At the beginning of each end step, if you gained 5 or more life this
///    turn, create a 4/4 white Angel creature token with flying and
///    vigilance.
///    {3}{W}{W}{W}: Until end of turn, this creature gets +2/+2 and gains
///    lifelink."
///
/// The base shape (name, Creature, Angel subtype, {1}{W}{W}, 3/3) is
/// materialised from the embedded JSON definition
/// (<c>resplendent-angel.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>. The JSON carries no abilities
/// — the <c>AbilityDefinition</c> schema doesn't express a "gained N life
/// this turn" intervening-if token trigger or a pump-and-grant activated
/// ability, so all three printed behaviours are layered on here (same
/// posture as <see cref="AdantoVanguardFactory"/>).
///
/// ## Implemented (v1)
/// - <b>Flying</b> (CR 702.9) — <see cref="KeywordAbility"/> marker, the
///   same wiring shape as <see cref="SlickshotShowOffFactory"/>'s Flying.
/// - <b>"At the beginning of each end step, if you gained 5 or more life
///   this turn, create a 4/4 white Angel creature token with flying and
///   vigilance" (CR 603.1 + CR 603.4 intervening-if + CR 111.4 token)</b>:
///     - The trigger fires on EVERY player's End step — printed "each end
///       step" carries NO active-player filter (CR 500.7), exactly the
///       <see cref="WildernessReclamationFactory"/> shape: an
///       <see cref="EventTriggerCondition{T}"/> over
///       <see cref="StepStartedEvent"/> filtered to
///       <see cref="PhaseStateType.End"/> only. (Contrast
///       <see cref="Abilities.Triggers.OnStepBegin"/>, which adds the
///       controller filter for "your end step".)
///     - The intervening-if "if you gained 5 or more life this turn"
///       (CR 603.4) is evaluated at resolution against a per-turn life-gained
///       latch: a <see cref="LifeChangedEvent"/> subscription sums positive
///       deltas (NewLife &gt; PreviousLife) for the Angel's controller, and
///       a <see cref="TurnStartedEvent"/> subscription resets the running
///       total each turn. Same closure-latch idiom as
///       <see cref="OcelotPrideFactory"/>'s "dealt combat damage this turn"
///       latch. The engine has no built-in <c>LifeGainedThisTurn</c>
///       accumulator (only <see cref="Player.LifeLostThisTurn"/>), so the
///       factory maintains its own.
///     - On resolve (latch ≥ 5) a single 4/4 white Angel token with Flying +
///       Vigilance is minted under the Angel's controller via
///       <see cref="TokenFactory.CreateOnBattlefield"/> (count-1 overload,
///       routed through <see cref="ZoneService.Replacements"/> so token
///       doublers — Doubling Season / Anointed Procession — can rewrite the
///       count, CR 614 / CR 616.1c).
/// - <b>"{3}{W}{W}{W}: Until end of turn, this creature gets +2/+2 and gains
///   lifelink" (CR 602 activated ability)</b>: an
///   <see cref="ActivatedAbility"/> whose only cost is
///   <see cref="ManaCostCost"/>("{3}{W}{W}{W}") and which targets nothing —
///   it modifies Resplendent Angel itself (same self-pump shape as
///   <see cref="FieryHellhoundFactory"/>'s firebreathing). On resolution it
///   registers a <see cref="PumpUntilEndOfTurnEffect"/>(+2, +2) (Layer 7c)
///   and a <see cref="GrantKeywordUntilEndOfTurnEffect"/>("Lifelink")
///   (Layer 6) against the Angel's <see cref="Creature.ActiveEffects"/>,
///   both expiring in the cleanup step (CR 514.2). No sorcery-speed gate is
///   printed (CR 602.5a instant speed); the ability is repeatable, each
///   activation stacking another +2/+2 (CR 613.1f).
///
/// ## Wiring overloads
/// - <see cref="Create(Player)"/> — shape only. Flying + the end-step
///   trigger + the activated ability are attached for shape observability;
///   without an event bus the life-gained latch is never updated, without a
///   <see cref="TriggerManager"/> the trigger isn't bus-driven, and without
///   a <see cref="ContinuousEffectsService"/> on
///   <see cref="Creature.ActiveEffects"/> the pump / lifelink grant no-op.
///   This is the overload <see cref="NamedCardFactory"/> dispatches to.
/// - <see cref="Create(Player, ZoneService?, IEventBus?, TriggerManager?, ContinuousEffectsService?)"/>
///   — fully wired.
///
/// ## Deferred (v1 gaps)
/// - <b>Token-creation prompt / choice</b>: none — the token is mandatory and
///   fully specified, so there is nothing to defer here.
/// </summary>
[CardName("Resplendent Angel")]
public static class ResplendentAngelFactory
{
    public const string CardName = "Resplendent Angel";
    public const string Slug = "resplendent-angel";

    /// <summary>Mana cost of the pump-and-lifelink activated ability.</summary>
    public const string PumpCost = "{3}{W}{W}{W}";

    /// <summary>Life that must be gained this turn for the end-step token
    /// trigger's intervening-if (CR 603.4) to be satisfied.</summary>
    public const int LifeGainThreshold = 5;

    /// <summary>Power bonus from the activated ability (CR 613.1f Layer 7c).</summary>
    public const int PumpPower = 2;

    /// <summary>Toughness bonus from the activated ability.</summary>
    public const int PumpToughness = 2;

    /// <summary>
    /// Construct Resplendent Angel with no live wiring. Flying + the
    /// end-step token trigger + the pump-and-lifelink activated ability are
    /// attached structurally; the life-gained latch is never updated (no
    /// event bus), the trigger is not bus-registered (no
    /// <see cref="TriggerManager"/>), and the pump / lifelink grant no-op (no
    /// <see cref="ContinuousEffectsService"/>). Suitable for shape /
    /// dispatcher tests. This is the overload <see cref="NamedCardFactory"/>
    /// dispatches to.
    /// </summary>
    public static Creature Create(Player owner)
        => Create(owner, zoneService: null, eventBus: null, triggers: null, effects: null);

    /// <summary>
    /// Construct Resplendent Angel with optional runtime services.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="zoneService">Routes the minted Angel token through
    /// <see cref="ZoneService"/> (so <see cref="CardMovedEvent"/> + token
    /// doublers fire). Null → token minted directly with no event/doubler
    /// plumbing.</param>
    /// <param name="eventBus">Subscribes the per-turn "gained N life this
    /// turn" latch (positive <see cref="LifeChangedEvent"/> deltas for the
    /// controller, reset on <see cref="TurnStartedEvent"/>). Null → latch
    /// stays at 0 so the intervening-if never fires.</param>
    /// <param name="triggers">Registers the end-step token trigger for
    /// bus-driven firing. Null → trigger is attached to the card but not
    /// bus-driven.</param>
    /// <param name="effects">Layers service the +2/+2 pump (Layer 7c) and
    /// the Lifelink grant (Layer 6) are registered against on each
    /// activation. Bound onto <see cref="Creature.ActiveEffects"/>. Null →
    /// the activated ability resolves to a no-op.</param>
    public static Creature Create(
        Player owner,
        ZoneService? zoneService,
        IEventBus? eventBus,
        TriggerManager? triggers,
        ContinuousEffectsService? effects)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Creature, Angel
        // subtype, {1}{W}{W}, 3/3). The JSON carries no abilities — all three
        // printed behaviours are layered on below.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Creature)CardDefinitionFactory.Build(definition, owner);

        // Bind the effects service so live P/T + keyword reads flow through
        // the layers compute (CR 613). Mirrors SlickshotShowOff.
        if (effects != null)
        {
            card.ActiveEffects = effects;
        }

        // CR 702.9 — Flying. Keyword marker consumed by CombatValidator /
        // CombatAbilities (same wiring as SlickshotShowOff's Flying).
        card.AddAbility(new KeywordAbility("Flying", card, owner));

        // ----------------------------------------------------------------
        // Per-turn "gained 5 or more life this turn" latch — subscribed when
        // an event bus is supplied. The engine has no built-in
        // LifeGainedThisTurn accumulator (only Player.LifeLostThisTurn), so
        // we maintain our own running total. Closure-captured so the trigger
        // body inspects it at resolution (CR 603.4 intervening-if).
        // ----------------------------------------------------------------
        var lifeGainedThisTurn = new int[] { 0 };

        if (eventBus != null)
        {
            eventBus.Subscribe<LifeChangedEvent>(e =>
            {
                // "you gained life" — the Angel's controller. CR 119.3 — only
                // a strict increase counts as life gain (loss / set-to does
                // not accumulate here). Read controller live so a control
                // change (Confiscate / Threaten) routes the latch to the
                // current controller.
                var controller = card.Controller ?? owner;
                if (!ReferenceEquals(e.Player, controller)) return;
                if (e.NewLife <= e.PreviousLife) return;
                lifeGainedThisTurn[0] += e.NewLife - e.PreviousLife;
            });

            // CR 500.7 — the running total is per-turn; reset at each turn
            // start (mirrors OcelotPride's latch reset).
            eventBus.Subscribe<TurnStartedEvent>(_ => lifeGainedThisTurn[0] = 0);
        }

        // ----------------------------------------------------------------
        // "At the beginning of each end step, if you gained 5 or more life
        // this turn, create a 4/4 white Angel creature token with flying and
        // vigilance." CR 603.1 + CR 603.4 (intervening-if) + CR 111.4.
        //
        // "each end step" → NO active-player filter (fires on every player's
        // End step), exactly the Wilderness Reclamation shape. The
        // intervening-if is checked at resolution against the life-gained
        // latch.
        // ----------------------------------------------------------------
        var tokenEffect = new Effect(
            $"{CardName}: create a 4/4 white Angel token with flying and vigilance " +
            "if you gained 5 or more life this turn",
            () =>
            {
                if (card.Zone != ZoneType.Battlefield) return;
                if (lifeGainedThisTurn[0] < LifeGainThreshold) return;

                // CR 110.2 — read controller at resolve so the token enters
                // under the current controller.
                var controller = card.Controller ?? owner;
                CreateAngelToken(controller, zoneService);
            });

        var endStepTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: new EventTriggerCondition<StepStartedEvent>(
                (e, _) => e.StepType == PhaseStateType.End),
            effects: new IEffect[] { tokenEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(endStepTrigger);
        triggers?.RegisterTriggeredAbility(endStepTrigger);

        // ----------------------------------------------------------------
        // "{3}{W}{W}{W}: Until end of turn, this creature gets +2/+2 and
        // gains lifelink." CR 602 activated ability. Cost = ManaCostCost
        // ("{3}{W}{W}{W}"); no target (self-pump, FieryHellhound shape). On
        // resolve register PumpUntilEndOfTurnEffect(+2,+2) (Layer 7c) +
        // GrantKeywordUntilEndOfTurnEffect("Lifelink") (Layer 6), both EOT
        // (CR 514.2). null ActiveEffects → silent no-op (shape-only path).
        // ----------------------------------------------------------------
        var pumpEffect = new Effect(
            $"{CardName}: +{PumpPower}/+{PumpToughness} and gains lifelink until end of turn",
            () =>
            {
                card.ActiveEffects?.Register(
                    new PumpUntilEndOfTurnEffect(card, PumpPower, PumpToughness));
                card.ActiveEffects?.Register(
                    new GrantKeywordUntilEndOfTurnEffect(card, "Lifelink"));
            });

        card.AddAbility(new ActivatedAbility(
            source: card,
            controller: owner,
            costs: new ICost[] { new ManaCostCost(PumpCost) },
            effects: new IEffect[] { pumpEffect }));

        return card;
    }

    /// <summary>
    /// CR 111.4 — create a single 4/4 white Angel creature token with Flying
    /// + Vigilance under <paramref name="controller"/>'s control. Routed
    /// through the count-1 token overload so token doublers can rewrite the
    /// count (CR 614 / CR 616.1c) when a <see cref="ZoneService"/> with a
    /// <see cref="ReplacementBus"/> is wired.
    /// </summary>
    private static void CreateAngelToken(Player controller, ZoneService? zones)
    {
        var spec = new TokenFactory.TokenSpec(
            Name: "Angel",
            Power: 4,
            Toughness: 4,
            Subtypes: new[] { CardSubtype.Angel },
            Keywords: new[] { "Flying", "Vigilance" },
            // CR 105 / CR 111.4 — printed "4/4 white Angel creature token".
            Colors: new[] { ManaColor.White });

        TokenFactory.CreateOnBattlefield(
            spec, controller, count: 1, zones, zones?.Replacements);
    }
}
