using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Counters;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Kutzil's Flanker (The Lost Caverns of Ixalan,
/// {2}{W}).
///
/// Creature — Cat Warrior 3/1. Oracle text (verified against Scryfall):
///   "Flash
///    When this creature enters, choose one —
///    • Put a +1/+1 counter on this creature for each creature that left the
///      battlefield under your control this turn.
///    • You gain 2 life and scry 2.
///    • Exile target player's graveyard."
///
/// The base shape (name, Creature, Cat + Warrior subtypes, {2}{W}, 3/1, and
/// the <b>Flash</b> keyword CR 702.8) is materialised from the embedded JSON
/// definition (<c>kutzils-flanker.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>. The modal ETB triggered ability
/// is layered on here — the JSON <c>AbilityDefinition</c> schema doesn't yet
/// express a "choose one —" modal trigger, so it lives in the factory (same
/// posture as <see cref="CharmingPrinceFactory"/>).
///
/// ## Implemented (v1)
/// - <b>Flash (CR 702.8)</b> — declarative JSON keyword.
/// - <b>ETB modal triggered ability</b> (CR 700.2d — "Choose one —",
///   CR 603.1 / CR 603.6a): three modes, one chosen at resolve time via the
///   engine-recorded mode (<see cref="ResolutionContext.ChosenMode"/>,
///   surfaced by the declared <see cref="ModeRequest"/> at stack entry per
///   CR 603.3), with a captured fallback for the no-agent dispatcher path —
///   same posture as <see cref="CharmingPrinceFactory"/>.
///
/// ## Modes
/// - <b>Mode 0 — "Put a +1/+1 counter on this creature for each creature that
///   left the battlefield under your control this turn."</b> (CR 122.1c /
///   CR 700.4): reads <see cref="Game.TurnState.CreaturesDiedByController"/>
///   for the controller. The engine increments that per-controller tally for
///   ANY creature leaving the battlefield (to any zone) while it had the
///   Creature type — exactly "creatures that left the battlefield under your
///   control this turn." N counters are added via
///   <c>card.Counters.Add(CounterType.PlusOnePlusOne, N)</c>. Without a live
///   <see cref="Game.TurnState"/> (shape / dispatcher tests) the count is 0
///   and no counters are placed.
/// - <b>Mode 1 — "You gain 2 life and scry 2."</b> (CR 119.3 / CR 701.20):
///   <c>controller.GainLife(2)</c> then the standard <see cref="ScryAction"/>
///   pipeline for N=2 (same as <see cref="CharmingPrinceFactory"/>'s scry
///   arm). The agent's <see cref="IPlayerAgent.ChooseScryDecisionAsync"/> is
///   consulted when one is registered; otherwise it falls back to all-bottom.
/// - <b>Mode 2 — "Exile target player's graveyard."</b> (CR 701.21): snapshots
///   and exiles every card in the target player's graveyard to that player's
///   exile zone. An empty graveyard is a clean no-op (CR 608.2b). Mirrors
///   <see cref="ThrabenCharmFactory"/>'s graveyard-exile arm.
///
/// ## Deferred (v1 gaps)
/// - <b>True agent-driven mode prompt on every path</b>: the mode is captured
///   at factory time for test convenience; the engine-recorded
///   <see cref="ResolutionContext.ChosenMode"/> is preferred when present
///   (the live agent-driven path). Same posture as
///   <see cref="CharmingPrinceFactory"/>.
/// </summary>
[CardName("Kutzil's Flanker")]
public static class KutzilsFlankerFactory
{
    public const string CardName = "Kutzil's Flanker";
    public const string Slug = "kutzils-flanker";
    public const int Power = 3;
    public const int Toughness = 1;

    /// <summary>Mode 0 — +1/+1 counter per creature that left your control this turn.</summary>
    public const int ModeCounters = 0;
    /// <summary>Mode 1 — gain 2 life and scry 2.</summary>
    public const int ModeLifeAndScry = 1;
    /// <summary>Mode 2 — exile target player's graveyard.</summary>
    public const int ModeExileGraveyard = 2;

    private const int LifeGainAmount = 2;
    private const int ScryAmount = 2;

    /// <summary>Printed mode labels, in oracle order (CR 700.2d).</summary>
    public static IReadOnlyList<string> Modes => new[]
    {
        "Put a +1/+1 counter on this creature for each creature that left the battlefield under your control this turn.",
        "You gain 2 life and scry 2.",
        "Exile target player's graveyard.",
    };

    private static readonly IReadOnlyList<BotIntent> ModeIntents = new[]
    {
        BotIntent.Buff,     // grow Kutzil's Flanker with +1/+1 counters
        BotIntent.Heal,     // gain 2 life + scry 2 — stabilise + card quality
        BotIntent.Removal,  // graveyard hate
    };

    /// <summary>
    /// Construct Kutzil's Flanker with no live wiring. The modal ETB trigger
    /// is attached for shape observability but not registered with a
    /// <see cref="TriggerManager"/>. Suitable for dispatcher / structural
    /// tests. This is the overload <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Creature Create(Player owner) => Create(owner, ModeLifeAndScry, triggers: null);

    /// <summary>
    /// Construct Kutzil's Flanker with a pre-selected mode (used by the
    /// no-agent dispatcher path / tests). Supplying a
    /// <see cref="TriggerManager"/> additionally registers the ETB trigger on
    /// the bus.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="mode">Pre-selected mode (0=counters, 1=life+scry,
    /// 2=exile graveyard). Overridden by the engine-recorded
    /// <see cref="ResolutionContext.ChosenMode"/> when present.</param>
    /// <param name="triggers">TriggerManager the ETB trigger is registered
    /// with so it surfaces as pending. May be null.</param>
    public static Creature Create(Player owner, int mode, TriggerManager? triggers = null)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Creature,
        // Cat + Warrior subtypes, {2}{W}, 3/1, Flash keyword). The JSON
        // carries no abilities beyond Flash — the modal ETB is layered below.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Creature)CardDefinitionFactory.Build(definition, owner);

        // ----------------------------------------------------------------
        // ETB modal triggered ability (CR 603.1 / CR 603.6a / CR 700.2d).
        // ----------------------------------------------------------------
        TriggeredAbility? etbTrigger = null;
        var etbEffect = new Effect(
            $"{CardName}: choose one — +1/+1 per creature that left; gain 2 life + scry 2; exile target player's graveyard",
            async ctx =>
            {
                if (etbTrigger == null) return;

                var controller = card.Controller ?? owner;
                var chosenMode = PickMode(mode, ctx);

                switch (chosenMode)
                {
                    case ModeCounters:
                        ExecuteCounters(card, controller, ctx);
                        break;

                    case ModeLifeAndScry:
                        await ExecuteLifeAndScryAsync(controller, ctx).ConfigureAwait(false);
                        break;

                    case ModeExileGraveyard:
                        ExecuteExileGraveyard(etbTrigger);
                        break;
                }
            });

        etbTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnEnterBattlefieldSelf(card),
            effects: new IEffect[] { etbEffect },
            interveningIf: null,
            activeZones: new[] { ZoneType.Battlefield },
            // CR 700.2d — declare the "choose one —" mode request so the engine
            // prompts the controller's agent at stack entry (CR 603.3) and
            // records the chosen mode on the ability; PickMode reads it off
            // ResolutionContext.ChosenMode at resolve time.
            modeRequest: new ModeRequest(
                Modes: Modes,
                MinModes: 1,
                MaxModes: 1,
                ModeIntents: ModeIntents),
            targetRequests: new[]
            {
                // Mode 2 target slot. MinTargets=0 so modes 0 and 1 don't
                // require a target (CR 700.2d — only the chosen mode's
                // targeting is relevant).
                new TargetRequest(
                    Description: "target player",
                    MinTargets: 0,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.Removal,
                    CandidateGatherer: ctx => ctx.AllPlayers
                        .Cast<object>()
                        .ToList()),
            });

        card.AddAbility(etbTrigger);
        triggers?.RegisterTriggeredAbility(etbTrigger);

        return card;
    }

    // ------------------------------------------------------------------
    // Mode helpers
    // ------------------------------------------------------------------

    /// <summary>
    /// Resolve the mode to execute. PREFERS the engine-recorded mode the
    /// controller's agent chose at STACK ENTRY (CR 700.2d / CR 603.3 — surfaced
    /// on <see cref="ResolutionContext.ChosenMode"/> by the declared
    /// <see cref="ModeRequest"/>); falls back to the captured
    /// <paramref name="defaultMode"/> (factory-time mode / no-agent dispatcher
    /// path). Same posture as <see cref="CharmingPrinceFactory"/>.
    /// </summary>
    private static int PickMode(int defaultMode, ResolutionContext ctx)
    {
        if (ctx.ChosenMode is { } recorded && recorded >= 0 && recorded < Modes.Count)
        {
            return recorded;
        }

        return defaultMode;
    }

    /// <summary>
    /// Mode 0 — "Put a +1/+1 counter on this creature for each creature that
    /// left the battlefield under your control this turn." (CR 122.1c).
    ///
    /// Reads <see cref="Game.TurnState.CreaturesDiedByController"/> — the
    /// engine increments that per-controller tally for any creature leaving the
    /// battlefield (to any zone) while it had the Creature type (see
    /// <c>TurnDriver.OnCardMoved</c>), which is precisely "creatures that left
    /// the battlefield under your control this turn." With no live TurnState
    /// wired (shape tests), N=0 and no counters are placed.
    /// </summary>
    private static void ExecuteCounters(Creature card, Player controller, ResolutionContext ctx)
    {
        var n = ctx.Game?.TurnState?.CreaturesDiedByController(controller) ?? 0;
        if (n <= 0) return;
        card.Counters.Add(CounterType.PlusOnePlusOne, n);
    }

    /// <summary>
    /// Mode 1 — "You gain 2 life and scry 2." (CR 119.3 / CR 701.20). Gain
    /// happens first, then scry 2 via the standard <see cref="ScryAction"/>
    /// pipeline (same body as <see cref="CharmingPrinceFactory"/>'s scry arm).
    /// </summary>
    private static async ValueTask ExecuteLifeAndScryAsync(Player controller, ResolutionContext ctx)
    {
        controller.GainLife(LifeGainAmount);

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
            // Pre-agent default: all peeked cards to bottom.
            decision = new ScryAction.ScryDecision(
                ToBottom: peeked.ToList(),
                TopOrder: Array.Empty<ICard>());
        }

        ScryAction.Apply(controller, peeked.Count, decision);
    }

    /// <summary>
    /// Mode 2 — "Exile target player's graveyard." (CR 701.21). Snapshots and
    /// exiles every card in the target player's graveyard to that player's
    /// exile zone. An empty graveyard is a clean no-op (CR 608.2b). Mirrors
    /// <see cref="ThrabenCharmFactory"/>'s graveyard-exile arm.
    /// </summary>
    private static void ExecuteExileGraveyard(TriggeredAbility trigger)
    {
        var chosen = trigger.ChosenTargets;
        if (chosen.Count == 0 || chosen[0].Count == 0) return;

        // CR 608.2b — target must be a Player.
        if (chosen[0][0] is not Player targetPlayer) return;

        var graveyardCards = targetPlayer.Zones.Graveyard.GetCards().ToList();
        foreach (var card in graveyardCards)
        {
            targetPlayer.Zones.Graveyard.RemoveCard(card);
            targetPlayer.Zones.Exile.AddCard(card);
            card.SetZone(ZoneType.Exile);
        }
    }
}
