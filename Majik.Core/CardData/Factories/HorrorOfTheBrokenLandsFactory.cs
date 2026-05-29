using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Horror of the Broken Lands (Hour of Devastation,
/// {4}{B}). Creature — Horror 4/4. Oracle text (verified against Scryfall):
///   "Whenever you cycle or discard another card, this creature gets +2/+1
///    until end of turn.
///    Cycling {B} ({B}, Discard this card: Draw a card.)"
///
/// The card's base shape (name, Creature, Horror subtype, {4}{B}, 4/4) is
/// materialised from the embedded JSON definition
/// (<c>horror-of-the-broken-lands.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>. The two printed behaviours
/// (cycle/discard pump trigger, Cycling {B}) are layered on top here — the
/// JSON <c>AbilityDefinition</c> schema doesn't express triggered abilities
/// or keyword activated abilities, so they live in the factory (same posture
/// as <see cref="StormscaleScionFactory"/> and the other JSON-backed cards
/// whose behaviour outgrows the schema).
///
/// ## Implemented (v1)
///
/// - <b>Creature — Horror {4}{B} 4/4</b> from JSON.
/// - <b>"Whenever you cycle ... another card, +2/+1 EOT" trigger</b>
///   (CR 603.1): wired as a <see cref="TriggeredAbility"/> over
///   <see cref="EventTriggerCondition{CardCycledEvent}"/> filtered to
///   <c>e.Player == card.Controller</c> ("you cycle", CR 109.5) AND
///   <c>!ReferenceEquals(e.Card, card)</c> (the "another card" gate — cycling
///   Horror itself does NOT fire its own trigger). <c>activeZones =
///   Battlefield</c> (abilities on a creature card function from the
///   battlefield only, CR 113.6). On resolve registers a one-turn
///   <see cref="PumpUntilEndOfTurnEffect"/>(+2/+1) on the supplied
///   <see cref="ContinuousEffectsService"/> (CR 613.1f, Layer 7c — the pump
///   flows through the layers pipeline; self-expires at cleanup, CR 514.2).
///   Identical trigger predicate + activeZones shape as
///   <see cref="CuratorOfMysteriesFactory"/>'s scry leg, with the scry effect
///   swapped for the EOT pump (same pump-effect shape as
///   <see cref="FestivalCrasherFactory"/>).
/// - <b>Cycling {B}</b> (CR 702.32) — routed through the shared
///   <see cref="CyclingFactory.Build"/> primitive with cycle cost
///   <see cref="ManaCostCost"/>("{B}"). The primitive attaches the
///   <see cref="ActivatedAbility"/> + a <see cref="KeywordAbility"/> "Cycling"
///   marker, layers <see cref="DiscardSelfCost"/> (CR 702.32a hand-zone gate)
///   on the cost stack, and on resolve draws a card then publishes
///   <see cref="CardCycledEvent"/> for CR 702.32d subscribers.
///
/// ## Discard surface deferral
///
/// The "or discard a card" half of the printed trigger is NOT wired in v1 —
/// identical posture to <see cref="CuratorOfMysteriesFactory"/>. The engine
/// has no dedicated <c>DiscardedEvent</c> today, so the trigger only fires on
/// cycle events. Cycling itself is the load-bearing half (the card was
/// printed for the Amonkhet/Hour cycling shell); the discard half is a small
/// future wire-up once a <c>DiscardedEvent</c> surface ships.
///
/// ## Wiring overloads
///
/// - <see cref="Create(Player)"/> — card shape only. The cycle trigger is
///   attached for shape inspection (the pump registers against a fresh
///   throwaway effects service so structural tests can fire it harmlessly);
///   cycling ability attached with no event bus (shape-only — no
///   CardCycledEvent publication). This is the overload
///   <see cref="NamedCardFactory"/> dispatches to.
/// - <see cref="Create(Player, ContinuousEffectsService?, TriggerManager?)"/>
///   — pump wired against the supplied layers service + trigger registered.
/// - <see cref="Create(Player, ContinuousEffectsService?, TriggerManager?, IEventBus?)"/>
///   — fully wired; cycling resolve publishes CardCycledEvent.
///
/// CR rule references: 205.3m (Horror subtype), 603.1 (triggered ability),
/// 613.1f / 514.2 (EOT pump lifecycle), 702.32 (Cycling).
/// </summary>
[CardName("Horror of the Broken Lands")]
public static class HorrorOfTheBrokenLandsFactory
{
    public const string CardName = "Horror of the Broken Lands";
    public const string Slug = "horror-of-the-broken-lands";
    public const int Power = 4;
    public const int Toughness = 4;
    public const string CyclingCost = "{B}";
    public const int PumpPower = 2;
    public const int PumpToughness = 1;

    /// <summary>
    /// Construct Horror of the Broken Lands with no live wiring. The cycle
    /// trigger is attached (its pump registers against a throwaway effects
    /// service so structural tests can fire the effect harmlessly); cycling
    /// ability attached without an event bus (shape-only — no
    /// CardCycledEvent publication). This is the overload
    /// <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Creature Create(Player owner)
        => Create(owner, effects: null, triggers: null, eventBus: null);

    /// <summary>
    /// Construct Horror of the Broken Lands with the pump wired against the
    /// supplied <paramref name="effects"/> service and the trigger registered
    /// with <paramref name="triggers"/>. No event bus — cycling stays
    /// shape-only.
    /// </summary>
    public static Creature Create(
        Player owner,
        ContinuousEffectsService? effects,
        TriggerManager? triggers)
        => Create(owner, effects, triggers, eventBus: null);

    /// <summary>
    /// Construct a fully-wired Horror of the Broken Lands.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="effects">Layers service the +2/+1 pump registers against
    /// (CR 613.1f, Layer 7c). When null a fresh throwaway service is used so
    /// the trigger still attaches + fires structurally.</param>
    /// <param name="triggers">TriggerManager the cycle trigger registers with
    /// so a <see cref="CardCycledEvent"/> auto-queues the pump. May be
    /// null.</param>
    /// <param name="eventBus">When supplied, the cycling resolve body
    /// publishes <see cref="CardCycledEvent"/> (CR 702.32d). May be
    /// null.</param>
    public static Creature Create(
        Player owner,
        ContinuousEffectsService? effects,
        TriggerManager? triggers,
        IEventBus? eventBus)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Creature,
        // Horror subtype, {4}{B}, 4/4). The JSON carries no abilities — the
        // pump trigger + Cycling are layered on below.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Creature)CardDefinitionFactory.Build(definition, owner);

        // ----------------------------------------------------------------
        // "Whenever you cycle ... another card, this creature gets +2/+1
        // until end of turn." (CR 603.1)
        //
        // EventTriggerCondition<CardCycledEvent> gated to:
        //   1. e.Player == card.Controller — "you cycle" (CR 109.5).
        //   2. !ReferenceEquals(e.Card, card) — "another card" gate.
        // activeZones = Battlefield (CR 113.6 — abilities on a creature card
        // function from the battlefield only).
        //
        // On resolve registers a one-turn +2/+1 PumpUntilEndOfTurnEffect on
        // the layers service (CR 613.1f, Layer 7c) that self-expires at
        // cleanup (CR 514.2). Same pump lifecycle as Festival Crasher.
        //
        // Discard-half deferred — engine has no DiscardedEvent surface today
        // (see class doc; identical posture to Curator of Mysteries). The
        // cycle leg alone covers the cycling-shell payoff role.
        // ----------------------------------------------------------------
        var layers = effects ?? new ContinuousEffectsService();
        card.ActiveEffects = layers;

        var pump = new Effect(
            $"{CardName}: +{PumpPower}/+{PumpToughness} until end of turn (cycle or discard another card)",
            () => layers.Register(new PumpUntilEndOfTurnEffect(card, PumpPower, PumpToughness)));

        var cycleCondition = new EventTriggerCondition<CardCycledEvent>(
            (e, _) =>
                ReferenceEquals(e.Player, card.Controller ?? owner)
                && !ReferenceEquals(e.Card, card));

        var cycleTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: cycleCondition,
            effects: new IEffect[] { pump },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(cycleTrigger);
        triggers?.RegisterTriggeredAbility(cycleTrigger);

        // ----------------------------------------------------------------
        // Cycling {B} — CR 702.32. Routed through the shared CyclingFactory
        // primitive; the primitive appends the DiscardSelfCost hand-zone gate
        // (CR 702.32a) and the CardCycledEvent publish (CR 702.32d).
        // ----------------------------------------------------------------
        CyclingFactory.Build(card, new ManaCostCost(CyclingCost), eventBus);

        return card;
    }
}
