using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Tokens;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Elvish Warmaster (Kaldheim Commander — Creature —
/// Elf Warrior {1}{G} 2/2).
///
/// Oracle text (verified against Scryfall):
///   "Whenever one or more other Elves you control enter, create a 1/1 green
///    Elf Warrior creature token. This ability triggers only once each turn.
///    {5}{G}{G}: Elves you control get +2/+2 and gain deathtouch until end of
///    turn."
///
/// A go-wide Elf payoff: every turn the first other Elf (or batch of Elves)
/// entering coughs up another body, and the {5}{G}{G} overrun turns the swarm
/// lethal. The base shape (name, Creature — Elf Warrior, {1}{G}, 2/2) is
/// materialised from the embedded JSON definition (<c>elvish-warmaster.json</c>)
/// via <see cref="CardDefinitionLoader.FromEmbeddedResource(string)"/> +
/// <see cref="CardDefinitionFactory.Build"/>. The two abilities are layered on
/// top here — the JSON <c>AbilityDefinition</c> schema doesn't express an
/// ETB-watching once-per-turn token trigger nor a tribal overrun activated
/// ability (same posture as <see cref="ImperiousPerfectFactory"/> /
/// <see cref="CraterhoofBehemothFactory"/>).
///
/// ## Implemented (v1)
///
/// ### "Whenever one or more other Elves you control enter, create a 1/1 green Elf Warrior creature token. This ability triggers only once each turn." (CR 603.1 / 603.2c)
/// A <see cref="TriggeredAbility"/> over <see cref="CardMovedEvent"/> fires when
/// another permanent enters the controller's battlefield that is an Elf
/// (Stack/anywhere → Battlefield, controller-owned, has the Elf subtype, and is
/// NOT the Warmaster itself — "other"). On resolution it mints one 1/1 green Elf
/// Warrior token (CR 111 / 111.4) via the shared
/// <see cref="ImperiousPerfectFactory.CreateElfWarriorToken"/> minter, routed
/// through the supplied <see cref="ZoneService"/> so token-ETB triggers chain
/// (CR 603.3). The minted Elf Warrior is itself an Elf, so it would re-trigger —
/// but the once-each-turn lock (below) prevents an infinite token cascade.
///
/// <b>"This ability triggers only once each turn." (CR 603.2c)</b> — a per-turn
/// <c>int[1]{0}</c> lock, shared between the trigger condition and a
/// <see cref="TurnStartedEvent"/> reset handler. The lock is read AND set inside
/// the condition predicate: the FIRST matching Elf-enter this turn flips the
/// lock and the ability triggers; every later Elf-enter the same turn sees the
/// lock closed and does NOT trigger (the restriction is on triggering, CR 603.2c,
/// not on resolution — so the body never enqueues a second time). The lock is
/// reset to 0 at the start of each turn (CR 500.1). Without an event bus the
/// lock stays closed after the first trigger (acceptable for single-turn /
/// shape tests) — mirrors <see cref="HiredClawFactory"/>'s once-per-turn lock.
///
/// ### "{5}{G}{G}: Elves you control get +2/+2 and gain deathtouch until end of turn." (CR 602 / 613)
/// An <see cref="ActivatedAbility"/> costing {5}{G}{G}. On resolution it
/// snapshots the Elves the controller controls and, for each, registers a
/// <see cref="PumpUntilEndOfTurnEffect"/>(+2/+2, Layer 7c per CR 613.1c) and a
/// <see cref="GrantKeywordUntilEndOfTurnEffect"/>("Deathtouch", Layer 6 per CR
/// 613.1c / 702.2). Both expire at cleanup (CR 514.2). Same temporary-team-pump
/// shape as <see cref="CraterhoofBehemothFactory.ApplyTrampleAndPump"/>, but
/// scoped to Elves and granting Deathtouch rather than Trample. Elves without a
/// wired <see cref="ContinuousEffectsService"/> no-op cleanly (shape-only guard).
///
/// ## Wiring overloads
/// - <see cref="Create(Player)"/> — single-arg dispatcher path
///   (<see cref="NamedCardFactory"/>). Both abilities are attached for shape
///   observability; the token trigger is NOT registered with any
///   <see cref="TriggerManager"/> and tokens land via raw zone moves (no
///   <see cref="ZoneService"/>); the once-per-turn lock is never reset (no event
///   bus).
/// - <see cref="Create(Player, IEventBus?, TriggerManager?, ZoneService?)"/> —
///   fully-wired overload registering the token trigger, funnelling token
///   creation through the zone service, and resetting the once-per-turn lock on
///   <see cref="TurnStartedEvent"/>.
///
/// ## Deferred (v1 gaps)
/// - <b>"one or more" batching</b>: CR 603.2c treats a batch of Elves entering
///   simultaneously as a single trigger event. The engine publishes one
///   <see cref="CardMovedEvent"/> per card, so the once-each-turn lock — which
///   closes on the first Elf-enter of the turn — already collapses a same-turn
///   batch to a single token, matching the printed once-per-turn outcome.
/// - <b>LTB unregister</b>: the registered until-end-of-turn effects are
///   self-expiring (CR 514.2); the token trigger's <c>activeZones</c> gates it to
///   the battlefield so it no-ops once the Warmaster leaves play.
/// </summary>
[CardName("Elvish Warmaster")]
public static class ElvishWarmasterFactory
{
    public const string CardName = "Elvish Warmaster";
    public const string Slug = "elvish-warmaster";

    public const int PumpPower = 2;
    public const int PumpToughness = 2;

    /// <summary>Granted evergreen keyword — CR 702.2 Deathtouch.</summary>
    public const string GrantedDeathtouch = "Deathtouch";

    /// <summary>
    /// Single-arg dispatcher path. Both abilities are attached structurally so
    /// the card shape is correct; the token trigger is not registered with any
    /// <see cref="TriggerManager"/>, tokens land via raw zone moves, and the
    /// once-per-turn lock is never reset (no event bus). This is the overload
    /// <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Creature Create(Player owner)
        => Create(owner, eventBus: null, triggers: null, zoneService: null);

    /// <summary>
    /// Construct a fully-wired Elvish Warmaster.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="eventBus">When supplied, a <see cref="TurnStartedEvent"/>
    /// handler resets the once-per-turn token-trigger lock (CR 500.1 / 603.2c).
    /// When null the lock stays closed after the first trigger of the turn.</param>
    /// <param name="triggers">TriggerManager the token trigger registers with so
    /// an Elf-enter <see cref="CardMovedEvent"/> fires it automatically. May be
    /// null — the trigger is still attached to the card shape.</param>
    /// <param name="zoneService">Optional zone service so each minted Elf Warrior
    /// token publishes <see cref="CardMovedEvent"/> on ETB. When null, tokens are
    /// placed via raw zone moves.</param>
    public static Creature Create(
        Player owner,
        IEventBus? eventBus,
        TriggerManager? triggers,
        ZoneService? zoneService)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (Creature — Elf Warrior,
        // {1}{G}, 2/2). The JSON carries no abilities — both are layered below.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Creature)CardDefinitionFactory.Build(definition, owner);

        BuildTokenTrigger(card, owner, eventBus, triggers, zoneService);
        BuildOverrunAbility(card, owner);

        return card;
    }

    // --- Once-per-turn Elf-enters token trigger (CR 603.1 / 603.2c) --------

    private static void BuildTokenTrigger(
        Creature card,
        Player owner,
        IEventBus? eventBus,
        TriggerManager? triggers,
        ZoneService? zoneService)
    {
        // CR 603.2c — "This ability triggers only once each turn." Per-turn
        // lock shared between the trigger condition (read + set) and the
        // TurnStartedEvent reset handler. 0 = open (may trigger), 1 = closed.
        var firedThisTurn = new int[] { 0 };

        // CR 603.1 / 603.2c — "Whenever one or more OTHER Elves YOU control
        // enter ... This ability triggers only once each turn."
        //   * ToZone == Battlefield (something entered the battlefield),
        //   * the entering card is an Elf creature,
        //   * its controller is this card's controller ("you control"),
        //   * it is NOT the Warmaster itself ("other"),
        //   * AND the once-per-turn lock is still open.
        // The predicate flips the lock on the first match of the turn, so a
        // same-turn batch of Elves collapses to a single trigger (CR 603.2c).
        var condition = new EventTriggerCondition<CardMovedEvent>((e, _) =>
        {
            if (e.ToZone != ZoneType.Battlefield) return false;
            if (ReferenceEquals(e.Card, card)) return false; // "other"
            if (e.Card is not Creature entered) return false;
            if (!entered.HasSubtype(CardSubtype.Elf)) return false;

            var controller = card.Controller ?? owner;
            if (!ReferenceEquals(entered.Controller, controller)) return false;

            if (firedThisTurn[0] != 0) return false; // once-per-turn lock closed.
            firedThisTurn[0] = 1;                     // CR 603.2c — close it.
            return true;
        });

        var tokenEffect = new Effect(
            $"{CardName}: create a 1/1 green Elf Warrior creature token (once each turn)",
            () => ImperiousPerfectFactory.CreateElfWarriorToken(
                card.Controller ?? owner, zoneService));

        var tokenTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: condition,
            effects: new IEffect[] { tokenEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(tokenTrigger);
        triggers?.RegisterTriggeredAbility(tokenTrigger);

        // CR 500.1 — reset the once-per-turn lock at the start of each turn.
        eventBus?.Subscribe<TurnStartedEvent>(_ => firedThisTurn[0] = 0);
    }

    // --- {5}{G}{G} tribal overrun (CR 602 / 613) ---------------------------

    private static void BuildOverrunAbility(Creature card, Player owner)
    {
        // CR 602 — "{5}{G}{G}: Elves you control get +2/+2 and gain deathtouch
        // until end of turn." On resolution each Elf the controller controls
        // gets a +2/+2 pump (Layer 7c) + a Deathtouch grant (Layer 6), both
        // until end of turn (CR 514.2). Same temporary-team-pump shape as
        // Craterhoof's ETB rider, scoped to Elves + granting Deathtouch.
        var overrunEffect = new Effect(
            $"{CardName}: Elves you control get +{PumpPower}/+{PumpToughness} and gain deathtouch until end of turn",
            () => ApplyOverrun(card.Controller ?? owner));

        var overrunAbility = new ActivatedAbility(
            source: card,
            controller: owner,
            costs: new ICost[] { new ManaCostCost("{5}{G}{G}") },
            effects: new IEffect[] { overrunEffect });

        card.AddAbility(overrunAbility);
    }

    /// <summary>
    /// Apply the {5}{G}{G} overrun rider to every Elf
    /// <paramref name="controller"/> controls at the moment this effect runs.
    /// Each Elf: +2/+2 pump (CR 613.1c Layer 7c) + Deathtouch grant (CR 613.1c
    /// Layer 6 / CR 702.2), both until end of turn (CR 514.2). Elves without a
    /// wired <see cref="ContinuousEffectsService"/> no-op cleanly.
    /// </summary>
    public static void ApplyOverrun(Player controller)
    {
        ArgumentNullException.ThrowIfNull(controller);

        // Snapshot to a list so same-step side effects don't disturb the
        // enumeration (mirrors CraterhoofBehemothFactory.ApplyTrampleAndPump).
        var elves = controller.Zones.Battlefield.GetCards()
            .OfType<Creature>()
            .Where(c => c.HasSubtype(CardSubtype.Elf))
            .ToList();

        foreach (var elf in elves)
        {
            // Shape-only safety — without a live ContinuousEffectsService the
            // grant/pump silently no-ops rather than NRE'ing.
            if (elf.ActiveEffects == null) continue;

            // CR 613.1c Layer 7c — +2/+2 pump (until end of turn).
            elf.ActiveEffects.Register(
                new PumpUntilEndOfTurnEffect(elf, PumpPower, PumpToughness));

            // CR 613.1c Layer 6 — Deathtouch grant (CR 702.2, until end of turn).
            elf.ActiveEffects.Register(
                new GrantKeywordUntilEndOfTurnEffect(elf, GrantedDeathtouch));
        }
    }
}
