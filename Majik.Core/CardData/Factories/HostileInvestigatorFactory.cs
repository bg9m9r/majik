using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Primitives;
using Majik.Core.Services;
using Majik.Core.Tokens;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Hostile Investigator (Murders at Karlov Manor,
/// {3}{B}).
///
/// Creature — Ogre Rogue Detective 4/3. Oracle text (verified against
/// Scryfall):
///   "When this creature enters, target opponent discards a card.
///    Whenever one or more players discard one or more cards, investigate.
///    This ability triggers only once each turn. (Create a Clue token. It's
///    an artifact with '{2}, Sacrifice this token: Draw a card.')"
///
/// Hostile Investigator pairs a Mind-Rot-style ETB discard with a
/// once-per-turn Investigate payoff that keys off ANY discard (CR 701.16 /
/// CR 701.39). Its own ETB discard satisfies the second ability the turn it
/// enters: the ETB discard funnels through the central discard chokepoint
/// (<see cref="Fx.Discard"/> → <see cref="Fx.DiscardCard"/>) which publishes
/// a <see cref="DiscardedEvent"/>, so the Investigate trigger observes it and
/// banks a Clue. It shares the Clue primitive with Thraben Inspector /
/// Bygone Bishop / Tireless Tracker (<see cref="TokenFactory.CreateClue"/>),
/// the once-per-turn reset shape with <see cref="EnduringInnocenceFactory"/>,
/// and the "target opponent → discards" ETB request with
/// <see cref="TourachDreadCantorFactory"/>.
///
/// ## Implemented (v1)
///
/// - 4/3 Creature — Ogre Rogue Detective at {3}{B}. The base shape (name,
///   Creature type, Ogre + Rogue + Detective subtypes, cost, P/T) is
///   materialised from the embedded JSON definition
///   (<c>hostile-investigator.json</c>) via
///   <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
///   <see cref="CardDefinitionFactory.Build"/>. The JSON carries no abilities
///   — both triggered abilities are layered on here.
///
/// - <b>ETB — "target opponent discards a card" (CR 603.6a / CR 701.16)</b>:
///   a <see cref="TriggeredAbility"/> over
///   <see cref="Triggers.OnEnterBattlefieldSelf"/> carrying a single
///   "target opponent" <see cref="TargetRequest"/> (same shape as Tourach's
///   kicked ETB). On resolution the chosen opponent discards one card via the
///   central <see cref="Fx.Discard"/> chokepoint — which routes through
///   <see cref="Fx.DiscardCard"/> and publishes a <see cref="DiscardedEvent"/>
///   so the Investigate ability below sees it. (Unlike Tourach's "discard at
///   random", this is a normal discard — the discarding player chooses which
///   card; v1 <see cref="Fx.Discard"/> uses the deterministic first-in-hand
///   pick, same agent-choice gap as Mind Rot / Faithless Looting.)
///
/// - <b>"Whenever one or more players discard one or more cards, investigate.
///   This ability triggers only once each turn." (CR 603.1 / CR 603.2c /
///   CR 701.39)</b>: a <see cref="TriggeredAbility"/> over
///   <see cref="DiscardedEvent"/> — the dedicated discard-detection surface —
///   matching ANY player's discard ("one or more players", CR 102.1). The
///   "only once each turn" clause is a captured <c>firedThisTurn</c> flag
///   checked in the predicate (suppresses further triggers once set this
///   turn) and reset on every <see cref="TurnStartedEvent"/> (CR 500.1) when
///   an event bus is supplied — same once-per-turn-reset machinery as
///   <see cref="EnduringInnocenceFactory"/>. On resolution it investigates
///   (CR 701.39): one Clue token under Hostile Investigator's controller via
///   the shared <see cref="TokenFactory.CreateClue"/> helper.
///
///   Note the printed "one or more players discard one or more cards" is a
///   batch clause: a single multi-card discard event (e.g. a Mind Rot) is one
///   trigger. The engine's per-card <see cref="DiscardedEvent"/> stream fires
///   once per discarded card; the once-per-turn flag is the dominant
///   constraint and makes the common case correct (the FIRST discard this
///   turn investigates once; subsequent discards this turn — whether in the
///   same batch or later — do not investigate again), matching the printed
///   "only once each turn" intent.
///
/// ## Wiring
/// - Single-arg <c>Create(Player)</c>: both triggers attached for shape
///   inspection; the Clue the effect would create and the discard bypass live
///   services when invoked manually. This is the overload
///   <see cref="NamedCardFactory"/> dispatches to.
/// - Multi-arg overload: when <paramref name="triggers"/> is supplied both
///   triggered abilities are registered with the manager; when
///   <paramref name="eventBus"/> is supplied a <see cref="TurnStartedEvent"/>
///   handler re-arms the once-per-turn flag (CR 500.1); when
///   <paramref name="zoneService"/> is supplied the Clue is placed via the
///   ZoneService so its arrival event fires.
/// </summary>
[CardName("Hostile Investigator")]
public static class HostileInvestigatorFactory
{
    public const string CardName = "Hostile Investigator";
    public const string Slug = "hostile-investigator";

    /// <summary>Cards the ETB makes the target opponent discard.</summary>
    public const int DiscardCount = 1;

    /// <summary>
    /// Construct Hostile Investigator with no live runtime services. Both
    /// triggers are attached for shape inspection. This is the overload
    /// <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, eventBus: null, triggers: null, zoneService: null);

    /// <summary>
    /// Construct a fully-wired Hostile Investigator.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="eventBus">When supplied, a <see cref="TurnStartedEvent"/>
    /// handler resets the once-per-turn Investigate flag (CR 500.1).</param>
    /// <param name="triggers">When supplied, both triggered abilities are
    /// registered so matching events land them on the stack automatically.</param>
    /// <param name="zoneService">When supplied, the Clue token is placed onto
    /// the battlefield via the ZoneService so its arrival event fires.</param>
    public static Creature Create(
        Player owner,
        IEventBus? eventBus,
        TriggerManager? triggers,
        ZoneService? zoneService)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Creature type,
        // Ogre + Rogue + Detective subtypes, {3}{B}, 4/3). The JSON carries no
        // abilities — both triggers are layered on below.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Creature)CardDefinitionFactory.Build(definition, owner);

        // ----------------------------------------------------------------
        // ETB — "When this creature enters, target opponent discards a
        //  card." (CR 603.6a / CR 701.16). One "target opponent" request;
        //  the chosen opponent discards one card through the central
        //  Fx.Discard chokepoint, which fires a DiscardedEvent so the
        //  Investigate trigger below observes it.
        // ----------------------------------------------------------------
        TriggeredAbility? etbTrigger = null;

        var etbEffect = new Effect(
            $"{CardName}: ETB — target opponent discards a card (CR 701.16)",
            () => ResolveEtbDiscard(etbTrigger));

        var targetRequest = new TargetRequest(
            Description: "target opponent",
            MinTargets: 1,
            MaxTargets: 1,
            LegalCandidates: Array.Empty<object>());

        etbTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnEnterBattlefieldSelf(card),
            effects: new IEffect[] { etbEffect },
            // CR 603.6a — ETB trigger only active while on the battlefield.
            activeZones: new[] { ZoneType.Battlefield },
            targetRequests: new[] { targetRequest });

        card.AddAbility(etbTrigger);
        triggers?.RegisterTriggeredAbility(etbTrigger);

        // ----------------------------------------------------------------
        // "Whenever one or more players discard one or more cards,
        //  investigate. This ability triggers only once each turn."
        //  (CR 603.1 / CR 603.2c / CR 701.39).
        // Subscribes to the dedicated DiscardedEvent (any player's discard,
        // CR 102.1 — "one or more players"); the once-per-turn flag gates
        // further triggers and is reset on TurnStartedEvent (CR 500.1).
        // ----------------------------------------------------------------
        var firedThisTurn = false;

        var investigateCondition = new EventTriggerCondition<DiscardedEvent>((_, _) =>
        {
            // CR 603.2c — "only once each turn": after the ability has fired
            // this turn the predicate suppresses any further triggers.
            return !firedThisTurn;
        });

        var investigateEffect = Fx.Inline(
            $"{CardName}: a discard happened — investigate (create a Clue token, CR 701.39, once each turn)",
            () =>
            {
                // CR 603.2c — mark fired so the predicate suppresses any
                // further triggers this turn. Set even before investigating so
                // a same-turn re-arm can't slip through. Reset on
                // TurnStartedEvent below.
                firedThisTurn = true;
                TokenFactory.CreateClue(card.Controller ?? owner, zoneService);
            });

        var investigateTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: investigateCondition,
            effects: new IEffect[] { investigateEffect },
            // CR 603.6a — only active while Hostile Investigator is on the
            // battlefield.
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(investigateTrigger);
        triggers?.RegisterTriggeredAbility(investigateTrigger);

        // CR 500.1 — a new turn re-arms the once-per-turn Investigate ability.
        if (eventBus != null)
        {
            eventBus.Subscribe<TurnStartedEvent>(_ => firedThisTurn = false);
        }

        return card;
    }

    /// <summary>
    /// Resolve the ETB trigger: the chosen target opponent discards one card.
    /// CR 701.16 — a normal discard (the discarding player chooses; v1
    /// <see cref="Fx.Discard"/> uses the deterministic first-in-hand pick).
    /// Routing through <see cref="Fx.Discard"/> fires a
    /// <see cref="DiscardedEvent"/> so the Investigate trigger observes it.
    /// Exposed for direct invocation by tests.
    /// </summary>
    public static void ResolveEtbDiscard(TriggeredAbility? trigger)
    {
        var opponent = ResolveTargetOpponent(trigger);
        if (opponent is null) return; // no legal target chosen → no-op.

        Fx.Discard(opponent, DiscardCount);
    }

    private static Player? ResolveTargetOpponent(TriggeredAbility? trigger)
    {
        if (trigger is null
            || trigger.ChosenTargets.Count == 0
            || trigger.ChosenTargets[0].Count == 0)
        {
            return null;
        }
        return trigger.ChosenTargets[0][0] as Player;
    }
}
