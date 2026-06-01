using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Stormwing Entity (Strixhaven: School of Mages,
/// {3}{U}{U}). Creature — Elemental 3/3. Oracle text (verified against
/// Scryfall):
///   "This spell costs {2}{U} less to cast if you've cast an instant or
///    sorcery spell this turn.
///    Flying
///    Prowess (Whenever you cast a noncreature spell, this creature gets
///    +1/+1 until end of turn.)
///    When this creature enters, scry 2."
///
/// The base shape (name, Creature, Elemental subtype, {3}{U}{U}, 3/3) is
/// materialised from the embedded JSON definition
/// (<c>stormwing-entity.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>. The printed behaviours
/// (conditional cost reduction, Flying, Prowess, ETB scry 2) are layered on
/// top here — the JSON <c>AbilityDefinition</c> schema doesn't yet express
/// keyword markers, cost reducers, Prowess, or scry, so they live in the
/// factory (same posture as <see cref="StormscaleScionFactory"/> and the
/// other JSON-backed cards whose behaviour outgrows the schema).
///
/// ## Implemented (v1)
/// - 3/3 Creature — Elemental at {3}{U}{U}; owner / controller wired.
/// - <b>Conditional self cost reduction (CR 117.7)</b>: "This spell costs
///   {2}{U} less to cast if you've cast an instant or sorcery spell this
///   turn." Wired as a <see cref="CostReductionAbility"/> using the
///   <see cref="CostReductionAbility.TotalReducer"/> shape. The reducer
///   reads a per-card "instant/sorcery cast this turn" flag that is driven
///   off the live <see cref="EventBus"/> (when supplied): the flag flips
///   true on a controller-cast instant/sorcery <see cref="SpellCastEvent"/>
///   and resets on the controller's <see cref="TurnStartedEvent"/> (the
///   "this turn" window — CR 500.4 / 514). Same EventBus-driven per-card
///   flag pattern as <see cref="RalMonsoonMageFactory"/>'s "during your
///   turn" window.
///     - no instant/sorcery cast this turn → pays {3}{U}{U}
///     - instant/sorcery cast this turn → the {2} generic collapses
///   <b>Colored-pip-reduction gap (documented v1 approximation)</b>: the
///   printed discount is "{2}{U} less", i.e. it removes the {2} generic AND
///   one {U} pip (reduced cost {1}{U}). The engine's cost-reduction pipeline
///   (<see cref="CostReduction.GetEffectiveCost"/>) only reduces generic
///   mana and floors at the colored pips (CR 117.7c) — there is no
///   colored-pip reducer in the printed-cost path (only Convoke's
///   payment-time peel). So v1 reduces the {2} generic only, leaving
///   {1}{U}{U} rather than the printed {1}{U}. This is the exact same
///   accepted approximation as <see cref="DemilichFactory"/> /
///   <see cref="BedlamRevelerFactory"/> (both documented as "{U} less" but
///   implemented as generic-floor reducers). The card is over-costed by one
///   {U} pip when the discount is active; the discount still meaningfully
///   accelerates the spell (the load-bearing {2} generic is removed).
/// - <b>Flying (CR 702.9)</b>: <see cref="KeywordAbility"/> marker so
///   <see cref="Majik.Core.Combat.CombatAbilities.HasFlying"/> /
///   <see cref="Majik.Core.Combat.CombatAbilities.CanBlockFlying"/> surface
///   the evasion / block-legality properties. Same shape as
///   <see cref="StormscaleScionFactory"/>.
/// - <b>Prowess (CR 702.108)</b>: wired via <see cref="ProwessFactory.Build"/>
///   when a <see cref="ContinuousEffectsService"/> is supplied (Layer 7c
///   +1/+1-until-end-of-turn pump per noncreature spell). Same shape as
///   <see cref="SoulScarMageFactory"/>.
/// - <b>ETB scry 2 (CR 603.6a / 701.20)</b>: a <see cref="TriggeredAbility"/>
///   whose condition is <see cref="Triggers.OnEnterBattlefieldSelf"/>; on
///   resolution it runs the standard <see cref="ScryAction"/> pipeline for
///   N=2 (agent-driven decision when an agent is registered, all-bottom
///   default otherwise — same body as <see cref="CharmingPrinceFactory"/>'s
///   scry mode).
///
/// ## Wiring overloads
/// - <see cref="Create(Player)"/> — shape only (dispatcher path). Flying +
///   cost reducer + ETB scry trigger attached; Prowess NOT wired (no effects
///   service) and the cost-reduction flag stays off (no EventBus → no
///   instant/sorcery this turn detected → printed cost). Suitable for
///   dispatcher / structural tests.
/// - <see cref="Create(Player, ContinuousEffectsService?, EventBus?, TriggerManager?)"/>
///   — fully wired. Prowess registers when <paramref name="effects"/> is
///   supplied; the cost-reduction flag is bus-driven when
///   <paramref name="eventBus"/> is supplied; the ETB trigger registers when
///   <paramref name="triggers"/> is supplied.
///
/// ## Deferred (v1 gaps)
/// - <b>Colored-pip reduction</b> — see the cost-reduction note above; the
///   {U} portion of "{2}{U} less" is not peeled (Demilich / Bedlam Reveler
///   posture). When a colored-pip reducer lands in the printed-cost pipeline
///   this factory's reducer becomes the wiring point.
/// - <b>ETB scry without a live bus</b> — the ETB trigger fires structurally
///   but only mutates the library when resolved through the normal stack /
///   resolution path with a live <see cref="ScryAction"/> seam (same posture
///   as every other ETB-scry factory).
/// </summary>
[CardName("Stormwing Entity")]
public static class StormwingEntityFactory
{
    public const string CardName = "Stormwing Entity";
    public const string Slug = "stormwing-entity";
    public const int Power = 3;
    public const int Toughness = 3;
    private const int ScryAmount = 2;

    /// <summary>Generic-mana portion of the printed "{2}{U} less" discount.
    /// The {U} portion is not peeled in v1 (see class remarks).</summary>
    private const int GenericDiscount = 2;

    /// <summary>
    /// Construct Stormwing Entity with no live wiring (shape / dispatcher
    /// path). Flying, the conditional cost reducer, and the ETB scry trigger
    /// are attached so structural assertions see them; Prowess is NOT wired
    /// (no effects service) and the cost-reduction flag stays off (no
    /// EventBus). This is the overload <see cref="NamedCardFactory"/>
    /// dispatches to.
    /// </summary>
    public static Creature Create(Player owner)
        => Create(owner, effects: null, eventBus: null, triggers: null);

    /// <summary>
    /// Construct Stormwing Entity with optional runtime services.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="effects">ContinuousEffectsService for the Prowess pump
    /// (CR 702.108, Layer 7c). May be null — Prowess is not wired.</param>
    /// <param name="eventBus">EventBus driving the "instant/sorcery cast this
    /// turn" cost-reduction flag. When null the flag stays off (printed cost).</param>
    /// <param name="triggers">TriggerManager — registers the ETB scry
    /// trigger (and the Prowess trigger, when effects are supplied). May be
    /// null — the triggers still attach to the card shape.</param>
    public static Creature Create(
        Player owner,
        ContinuousEffectsService? effects,
        EventBus? eventBus,
        TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Creature,
        // Elemental subtype, {3}{U}{U}, 3/3). The JSON carries no abilities —
        // cost reducer / Flying / Prowess / ETB scry are layered on below.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Creature)CardDefinitionFactory.Build(definition, owner);

        // ----------------------------------------------------------------
        // CR 117.7 — "This spell costs {2}{U} less to cast if you've cast an
        // instant or sorcery spell this turn." A per-card flag tracks
        // whether the controller has cast an instant/sorcery this turn.
        // Boxed in a single-element array so the bus subscriptions and the
        // reducer closure share one mutable cell (same boxed-flag pattern as
        // RalMonsoonMage's "during your turn" window). With no live bus the
        // flag stays false → no discount (printed cost), which is the
        // correct shape-path default.
        //
        // CR 117.7c — the engine reduces GENERIC mana only and floors at the
        // colored pips. The printed "{2}{U} less" therefore peels the {2}
        // generic but NOT the {U} pip in v1 (documented approximation — same
        // posture as Demilich / Bedlam Reveler; see class remarks).
        // ----------------------------------------------------------------
        var castInstantOrSorceryThisTurn = new bool[1];

        eventBus?.Subscribe<SpellCastEvent>(e =>
        {
            var controller = card.Controller ?? owner;
            if (!ReferenceEquals(e.Spell.Controller, controller)) return;
            if (e.Spell.Card.HasType(CardType.Instant)
                || e.Spell.Card.HasType(CardType.Sorcery))
            {
                castInstantOrSorceryThisTurn[0] = true;
            }
        });

        // "this turn" window resets at the start of the controller's turn
        // (CR 500.4 / 514 cleanup). Reset on any TurnStartedEvent — a brand-
        // new turn always begins a fresh "this turn" tally for everyone.
        eventBus?.Subscribe<TurnStartedEvent>(_ => castInstantOrSorceryThisTurn[0] = false);

        card.AddAbility(new CostReductionAbility(
            totalReducer: _ => castInstantOrSorceryThisTurn[0] ? GenericDiscount : 0,
            description:
                "This spell costs {2}{U} less to cast if you've cast an " +
                "instant or sorcery spell this turn."));

        // CR 702.9 — Flying. KeywordAbility marker so CombatAbilities
        // surfaces evasion / block-legality.
        card.AddAbility(new KeywordAbility("Flying", card, owner));

        // ----------------------------------------------------------------
        // Prowess (CR 702.108) — "Whenever you cast a noncreature spell,
        // this creature gets +1/+1 until end of turn." Wired via
        // ProwessFactory.Build when a ContinuousEffectsService is supplied
        // (same shape as Soul-Scar Mage). Shape-only path keeps the card
        // lean.
        // ----------------------------------------------------------------
        if (effects != null)
        {
            card.ActiveEffects = effects;
            var prowessTrigger = ProwessFactory.Build(card, effects);
            card.AddAbility(prowessTrigger);
            triggers?.RegisterTriggeredAbility(prowessTrigger);
        }

        // ----------------------------------------------------------------
        // ETB scry 2 (CR 603.6a / 701.20) — "When this creature enters,
        // scry 2." Standard ScryAction pipeline (N=2), agent-driven when an
        // agent is registered, all-bottom default otherwise. Same body as
        // Charming Prince's scry mode.
        // ----------------------------------------------------------------
        card.AddAbility(BuildEtbScry(card, owner, triggers));

        return card;
    }

    /// <summary>
    /// Build the "When this creature enters, scry 2" triggered ability
    /// (CR 603.6a / 701.20). Registered on <paramref name="triggers"/> when
    /// supplied so the runtime queues it on the ETB.
    /// </summary>
    private static TriggeredAbility BuildEtbScry(
        Creature card,
        Player owner,
        TriggerManager? triggers)
    {
        var condition = Triggers.OnEnterBattlefieldSelf(card);

        var scryEffect = new Effect(
            $"{CardName}: when this creature enters, scry {ScryAmount} (CR 701.20)",
            async ctx =>
            {
                var controller = card.Controller ?? owner;

                var peeked = ScryAction.Peek(controller, ScryAmount);
                if (peeked.Count == 0) return;

                var agent = ctx.Agent ?? AgentRegistry.Get(controller);
                ScryAction.ScryDecision decision;
                if (agent != null)
                {
                    decision = await agent.ChooseScryDecisionAsync(ctx.Game, peeked)
                        .ConfigureAwait(false);
                }
                else
                {
                    // Pre-agent default: all peeked cards to bottom (same
                    // fallback as Charming Prince's scry mode).
                    decision = new ScryAction.ScryDecision(
                        ToBottom: peeked.ToList(),
                        TopOrder: Array.Empty<ICard>());
                }

                ScryAction.Apply(controller, peeked.Count, decision);
            });

        var trigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: condition,
            effects: new IEffect[] { scryEffect },
            activeZones: new[] { ZoneType.Battlefield });

        triggers?.RegisterTriggeredAbility(trigger);
        return trigger;
    }
}
