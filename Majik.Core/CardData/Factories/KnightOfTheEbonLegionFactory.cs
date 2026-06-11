using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Costs;
using Majik.Core.Counters;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.StateMachine;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Knight of the Ebon Legion (Core Set 2020, {B}).
/// Creature — Vampire Knight 1/2. Oracle text (verified against Scryfall):
///   "{2}{B}: This creature gets +3/+3 and gains deathtouch until end of turn.
///    At the beginning of your end step, if a player lost 4 or more life this
///    turn, put a +1/+1 counter on this creature. (Damage causes loss of
///    life.)"
///
/// The base shape (name, Creature, Vampire/Knight subtypes, {B}, 1/2) is
/// materialised from the embedded JSON definition
/// (<c>knight-of-the-ebon-legion.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>. The JSON carries no abilities —
/// the <c>AbilityDefinition</c> schema doesn't express a self-pump-and-grant
/// activated ability or a "lost N life this turn" intervening-if end-step
/// trigger, so both printed behaviours are layered on here (same posture as
/// <see cref="ResplendentAngelFactory"/> / <see cref="AdantoVanguardFactory"/>).
///
/// ## Implemented (v1)
/// - <b>"{2}{B}: This creature gets +3/+3 and gains deathtouch until end of
///   turn" (CR 602 activated ability)</b>: an <see cref="ActivatedAbility"/>
///   whose only cost is <see cref="ManaCostCost"/>("{2}{B}") and which targets
///   nothing — it modifies the Knight itself (same self-pump shape as
///   <see cref="ResplendentAngelFactory"/>'s "{3}{W}{W}{W}: +2/+2 and gains
///   lifelink"). On resolution it registers a
///   <see cref="PumpUntilEndOfTurnEffect"/>(+3, +3) (CR 613.1f Layer 7c) and a
///   <see cref="GrantKeywordUntilEndOfTurnEffect"/>("Deathtouch") (CR 702.2 /
///   613.1c Layer 6) against the Knight's <see cref="Creature.ActiveEffects"/>,
///   both expiring in the cleanup step (CR 514.2). No sorcery-speed gate is
///   printed (CR 602.5a instant speed); the ability is repeatable, each
///   activation stacking another +3/+3 (CR 613.1f).
/// - <b>"At the beginning of your end step, if a player lost 4 or more life
///   this turn, put a +1/+1 counter on this creature" (CR 603.1 + CR 603.4
///   intervening-if + CR 121.1)</b>:
///     - "your end step" carries the controller filter (CR 500) via
///       <see cref="Triggers.OnStepBegin"/>(controller,
///       <see cref="StepStateType.End"/>) — contrast Resplendent Angel's
///       filter-free "each end step".
///     - The intervening-if "if a player lost 4 or more life this turn"
///       (CR 603.4) reads ANY player's <see cref="Player.LifeLostThisTurn"/>
///       (CR 119.3 — the engine accumulates this on every <c>LoseLife</c>,
///       and "Damage causes loss of life" (CR 120.3 → CR 119.8) routes combat
///       / direct damage through the same accumulator). "a player" includes
///       the controller, so the full player list is consulted via
///       <paramref name="playerResolver"/>; <see cref="Player.ResetTurnTrackers"/>
///       zeroes the accumulator at each turn start (CR 500.1) so no per-turn
///       latch is needed here.
///     - On resolve (some player lost ≥ 4 life) one
///       <see cref="CounterType.PlusOnePlusOne"/> counter is put on the Knight
///       via <see cref="CountersService.Add"/> (routed through the optional
///       <see cref="ReplacementBus"/> so Hardened Scales / Doubling Season —
///       CR 614 — rewrite the count, and the post-commit
///       <see cref="CounterAddedEvent"/> publishes).
///
/// ## Wiring overloads
/// - <see cref="Create(Player)"/> — shape only. Both abilities are attached for
///   shape observability; without a <see cref="TriggerManager"/> the end-step
///   trigger isn't bus-driven, without a <paramref name="playerResolver"/> the
///   intervening-if reads no players (false), and without a
///   <see cref="ContinuousEffectsService"/> on
///   <see cref="Creature.ActiveEffects"/> the pump / deathtouch grant no-op.
///   This is the overload <see cref="NamedCardFactory"/> dispatches to.
/// - <see cref="Create(Player, IEventBus?, TriggerManager?, ContinuousEffectsService?, ReplacementBus?, Func{IReadOnlyList{Player}})"/>
///   — fully wired.
///
/// ## Deferred (v1 gaps)
/// - <b>Live player enumeration</b>: no <c>Player.Opponents</c>/all-players
///   accessor exists at v1 (same gap as <see cref="HiredClawFactory"/>), so the
///   "a player lost 4+ life this turn" intervening-if reads the player set
///   through <paramref name="playerResolver"/>. Without a resolver the
///   intervening-if is false (the trigger resolves to a no-op).
/// </summary>
[CardName("Knight of the Ebon Legion")]
public static class KnightOfTheEbonLegionFactory
{
    public const string CardName = "Knight of the Ebon Legion";
    public const string Slug = "knight-of-the-ebon-legion";

    /// <summary>Mana cost of the pump-and-deathtouch activated ability.</summary>
    public const string PumpCost = "{2}{B}";

    /// <summary>Power bonus from the activated ability (CR 613.1f Layer 7c).</summary>
    public const int PumpPower = 3;

    /// <summary>Toughness bonus from the activated ability.</summary>
    public const int PumpToughness = 3;

    /// <summary>Life a player must have lost this turn for the end-step
    /// intervening-if (CR 603.4) to be satisfied.</summary>
    public const int LifeLostThreshold = 4;

    /// <summary>+1/+1 counters placed by the end-step trigger (CR 121.1).</summary>
    public const int CounterAmount = 1;

    /// <summary>
    /// Construct Knight of the Ebon Legion with no live wiring. The activated
    /// pump-and-deathtouch ability and the end-step counter trigger are
    /// attached structurally; the trigger is not bus-registered (no
    /// <see cref="TriggerManager"/>), its intervening-if reads no players (no
    /// resolver → false), and the pump / deathtouch grant no-op (no
    /// <see cref="ContinuousEffectsService"/>). Suitable for shape / dispatcher
    /// tests. This is the overload <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Creature Create(Player owner)
        => Create(owner, eventBus: null, triggers: null, effects: null,
                  replacements: null);

    /// <summary>
    /// Construct Knight of the Ebon Legion with optional runtime services. The
    /// end-step "if a player lost 4 or more life this turn" intervening-if reads
    /// every player from the LIVE resolution context (<c>ctx.Game.AllPlayers</c>)
    /// at resolution — no captured player resolver, so it is correct on the
    /// production routed build (mirrors #2551); with no live game context the
    /// intervening-if is false.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="eventBus">Routed through <see cref="CountersService.Add"/>
    /// so the +1/+1 placement publishes <see cref="CounterAddedEvent"/>. May be
    /// null.</param>
    /// <param name="triggers">Registers the end-step counter trigger for
    /// bus-driven firing. Null → trigger is attached to the card but not
    /// bus-driven.</param>
    /// <param name="effects">Layers service the +3/+3 pump (Layer 7c) and the
    /// Deathtouch grant (Layer 6) are registered against on each activation.
    /// Bound onto <see cref="Creature.ActiveEffects"/>. Null → the activated
    /// ability resolves to a no-op.</param>
    /// <param name="replacements">Optional <see cref="ReplacementBus"/> routed
    /// through <see cref="CountersService.Add"/> for the +1/+1 placement
    /// (Hardened Scales / Doubling Season — CR 614).</param>
    public static Creature Create(
        Player owner,
        IEventBus? eventBus,
        TriggerManager? triggers,
        ContinuousEffectsService? effects,
        ReplacementBus? replacements)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Creature,
        // Vampire/Knight subtypes, {B}, 1/2). The JSON carries no abilities —
        // both printed behaviours are layered on below.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Creature)CardDefinitionFactory.Build(definition, owner);

        // Bind the effects service so live P/T + keyword reads flow through the
        // layers compute (CR 613). Mirrors Resplendent Angel.
        if (effects != null)
        {
            card.ActiveEffects = effects;
        }

        // ----------------------------------------------------------------
        // "{2}{B}: This creature gets +3/+3 and gains deathtouch until end of
        // turn." CR 602 activated ability. Cost = ManaCostCost("{2}{B}"); no
        // target (self-pump, Resplendent Angel shape). On resolve register
        // PumpUntilEndOfTurnEffect(+3,+3) (Layer 7c) +
        // GrantKeywordUntilEndOfTurnEffect("Deathtouch") (Layer 6), both EOT
        // (CR 514.2). null ActiveEffects → silent no-op (shape-only path).
        // ----------------------------------------------------------------
        var pumpEffect = new Effect(
            $"{CardName}: +{PumpPower}/+{PumpToughness} and gains deathtouch until end of turn",
            () =>
            {
                card.ActiveEffects?.Register(
                    new PumpUntilEndOfTurnEffect(card, PumpPower, PumpToughness));
                card.ActiveEffects?.Register(
                    new GrantKeywordUntilEndOfTurnEffect(card, "Deathtouch"));
            });

        card.AddAbility(new ActivatedAbility(
            source: card,
            controller: owner,
            costs: new ICost[] { new ManaCostCost(PumpCost) },
            effects: new IEffect[] { pumpEffect }));

        // ----------------------------------------------------------------
        // "At the beginning of your end step, if a player lost 4 or more life
        // this turn, put a +1/+1 counter on this creature." CR 603.1 +
        // CR 603.4 (intervening-if) + CR 121.1.
        //
        // "your end step" → controller filter (Triggers.OnStepBegin). The
        // intervening-if is checked at resolution against any player's
        // Player.LifeLostThisTurn (CR 119.3); "Damage causes loss of life"
        // (CR 120.3 → 119.8) is already folded into that accumulator. The
        // engine resets it per turn (Player.ResetTurnTrackers, CR 500.1).
        // ----------------------------------------------------------------
        var counterEffect = new Effect(
            $"{CardName}: put a +1/+1 counter on this creature " +
            "if a player lost 4 or more life this turn",
            ctx =>
            {
                if (card.Zone != ZoneType.Battlefield) return ValueTask.CompletedTask;
                if (!AnyPlayerLostFourOrMoreLifeThisTurn(ctx.Game?.AllPlayers))
                    return ValueTask.CompletedTask;

                CountersService.Add(
                    card,
                    CounterType.PlusOnePlusOne,
                    CounterAmount,
                    replacements,
                    eventBus);

                return ValueTask.CompletedTask;
            });

        var endStepTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnStepBegin(owner, StepStateType.End),
            effects: new IEffect[] { counterEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(endStepTrigger);
        triggers?.RegisterTriggeredAbility(endStepTrigger);

        return card;
    }

    /// <summary>
    /// CR 603.4 — true iff at least one player (any player — "a player"
    /// includes the controller) has lost ≥ 4 life this turn
    /// (<see cref="Player.LifeLostThisTurn"/>). <paramref name="players"/> is the
    /// live player list read off the resolution context; without a live game
    /// this is false (the intervening-if fails and the trigger no-ops).
    /// </summary>
    private static bool AnyPlayerLostFourOrMoreLifeThisTurn(
        IReadOnlyList<Player>? players)
    {
        if (players == null) return false;

        foreach (var p in players)
        {
            if (p.LifeLostThisTurn >= LifeLostThreshold) return true;
        }
        return false;
    }
}
