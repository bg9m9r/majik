using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Nightshade Assassin (Time Spiral, {2}{B}{B}).
///
/// Creature — Human Assassin 2/1. Oracle text (Scryfall, verified):
///   "First strike
///    When this creature enters, you may reveal X black cards in your hand. If
///    you do, target creature gets -X/-X until end of turn.
///    Madness {1}{B} (If you discard this card, discard it into exile. When you
///    do, cast it for its madness cost or put it into your graveyard.)"
///
/// ## Shape source
/// Card identity (name, {2}{B}{B}, 2/1, Creature — Human Assassin, First
/// strike) is loaded from
/// <c>Majik.Core/CardData/Cards/nightshade-assassin.json</c> via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> and built through
/// <see cref="CardDefinitionFactory"/> (the <c>keywords</c> array carries First
/// strike, CR 702.7). The variable-X ETB reveal trigger is attached in code
/// below: the declarative JSON schema has no "reveal X cards matching a filter
/// → feed X into −X/−X" verb (this is exactly the deferral this factory pays
/// down), so it is hand-rolled — same posture as
/// <see cref="AcidicSlimeFactory"/> (a bespoke ETB target trigger whose
/// resolution honours an agent-set <see cref="TriggeredAbility.ChosenTargets"/>
/// with a deterministic single-arg fallback) plus
/// <see cref="CoilingOracleFactory"/> (a closure-captured optional
/// <see cref="IEventBus"/> for the CR 701.16 public reveal).
///
/// ## Reveal-count-X primitive (the pay-down)
/// "Reveal X black cards in your hand" is NOT a fixed amount and NOT a per-turn
/// tally (the existing dynamic-X <c>AmountSource</c> family — Spymaster's Vault,
/// Raffine — reads <c>creatures_died_this_turn</c> / <c>attackers_this_turn</c>
/// off <see cref="Majik.Core.Game.TurnState"/>). Here X is a <b>resolution-time
/// player choice</b>: the controller chooses ANY number (0..all) of black cards
/// in hand to reveal (CR 701.16 — "reveal"; CR 107.1 / 117.x — "you may reveal
/// X" lets the player pick X). We model this with the unified
/// <see cref="IPlayerAgent.ChooseAsync"/> sink as a
/// <see cref="ChoiceKind.PickN"/> over the controller's black hand cards,
/// <c>Min: 0</c> (the "may" / "X can be 0" floor), <c>Max</c> = number of black
/// cards in hand, <c>Optional: true</c>. X = the number actually revealed; that
/// count is then fed as the (negated) delta into the SAME Layer-7c
/// <see cref="PumpUntilEndOfTurnEffect"/> primitive the declarative
/// <c>pump_target</c> verb registers — i.e. −X/−X until end of turn (CR 611 /
/// CR 514.2). X = 0 ⇒ no reveal, no pump (the rider's "If you do" gate fails).
///
/// ## Implemented (v1)
/// - 2/1 Creature — Human Assassin at {2}{B}{B} with First strike (from JSON).
/// - <b>ETB trigger (CR 603.6a)</b>: targets any creature (1..1, Intent
///   <see cref="BotIntent.Removal"/>). The whole effect is a single "you may"
///   (CR 603.3c — a may-ETB still goes on the stack with a chosen target; the
///   reveal decides whether the −X/−X happens).
/// - Resolution honours an agent-set <see cref="TriggeredAbility.ChosenTargets"/>
///   (production path), falling back to the first creature on the controller's
///   battlefield for the single-arg dispatcher / no-agent posture (mirrors
///   <see cref="AcidicSlimeFactory"/>).
/// - X = number of black cards the controller chooses to reveal from hand
///   (agent <see cref="ChoiceKind.PickN"/>; no agent ⇒ reveal ALL black cards,
///   the value-maximising deterministic default). Each revealed card publishes
///   a <see cref="CardRevealedEvent"/> when an <see cref="IEventBus"/> is wired.
/// - X &gt; 0 and the chosen creature is still a battlefield
///   <see cref="Creature"/> ⇒ register a −X/−X
///   <see cref="PumpUntilEndOfTurnEffect"/> on its own
///   <see cref="Permanent.ActiveEffects"/> (auto-expires at cleanup; a creature
///   whose toughness drops to 0 or less dies to SBAs, CR 704.5f). CR 608.2b — an
///   illegal target at resolution (gone / no longer a creature) fizzles the
///   −X/−X cleanly even though the reveal already happened.
///
/// ## Madness (NOT wired here — intrinsic)
/// Madness {1}{B} works intrinsically for every catalogued card (CR 702.35) via
/// <see cref="Majik.Core.Keywords.MadnessCatalog"/> consulted by the central
/// discard funnel <see cref="Majik.Core.Primitives.Fx.DiscardCard"/>;
/// "Nightshade Assassin" is catalogued at {1}{B}, so the madness line needs no
/// factory code.
/// </summary>
[CardName("Nightshade Assassin")]
public static class NightshadeAssassinFactory
{
    public const string CardName = "Nightshade Assassin";

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("nightshade-assassin");

    /// <summary>
    /// Construct Nightshade Assassin with its ETB trigger attached to the card
    /// shape but NOT registered with a <see cref="TriggerManager"/> and with no
    /// reveal event bus. Suitable for shape / dispatcher tests.
    /// </summary>
    public static Creature Create(Player owner) => Create(owner, eventBus: null, triggers: null);

    /// <summary>
    /// Construct Nightshade Assassin with optional runtime services. When
    /// <paramref name="triggers"/> is supplied the ETB trigger is registered so
    /// the relevant ETB event places it on the stack automatically (CR 603.3).
    /// When <paramref name="eventBus"/> is supplied each revealed black card
    /// publishes a <see cref="CardRevealedEvent"/> (CR 701.16 public reveal).
    /// </summary>
    public static Creature Create(Player owner, IEventBus? eventBus, TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = (Creature)CardDefinitionFactory.Build(Definition, owner);
        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // ETB triggered ability — CR 603.6a.
        //   "When this creature enters, you may reveal X black cards in your
        //    hand. If you do, target creature gets -X/-X until end of turn."
        //
        // Bespoke 1..1 "target creature" request over every creature on the
        // battlefield across every player. The reveal-count X is resolved at
        // resolution from the controller's choice (see ResolveEtb).
        // ----------------------------------------------------------------
        TriggeredAbility? etb = null;

        var etbEffect = new Effect(
            $"{CardName}: you may reveal X black cards; target creature gets -X/-X until EOT",
            async rc => await ResolveEtbAsync(rc, owner, etb, eventBus).ConfigureAwait(false));

        etb = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnEnterBattlefieldSelf(card),
            effects: new IEffect[] { etbEffect },
            interveningIf: null,
            activeZones: new[] { ZoneType.Battlefield },
            targetRequests: new[]
            {
                new TargetRequest(
                    Description: "target creature",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: GatherCreatureTargets(owner).Cast<object>().ToList(),
                    Intent: BotIntent.Removal,
                    CandidateGatherer: ctx => ctx.AllPlayers
                        .SelectMany(p => p.Zones.Battlefield.GetCards())
                        .OfType<Creature>()
                        .Cast<object>()
                        .ToList()),
            });

        card.AddAbility(etb);
        triggers?.RegisterTriggeredAbility(etb);

        return card;
    }

    /// <summary>
    /// Snapshot the controller-visible creature target set at trigger-creation
    /// time. Production callers refresh via
    /// <see cref="TargetRequest.CandidateGatherer"/> at resolution.
    /// </summary>
    private static IReadOnlyList<Creature> GatherCreatureTargets(Player owner) =>
        owner.Zones.Battlefield.GetCards().OfType<Creature>().ToList();

    /// <summary>
    /// Resolve the ETB. Prompts the controller for the reveal-count X (any
    /// number of black cards in hand, 0..N — CR 117.x "you may reveal X"), emits
    /// a <see cref="CardRevealedEvent"/> per revealed card (CR 701.16), then —
    /// if X &gt; 0 and the chosen target is still a battlefield creature
    /// (CR 608.2b) — registers a −X/−X <see cref="PumpUntilEndOfTurnEffect"/>
    /// (CR 611 / CR 514.2).
    /// </summary>
    private static async ValueTask ResolveEtbAsync(
        ResolutionContext rc, Player owner, TriggeredAbility? etb, IEventBus? eventBus)
    {
        var controller = (rc.Source as Permanent)?.Controller ?? rc.Controller ?? owner;

        // The pool of black cards currently in the controller's hand (CR 105.2c —
        // "black" is the card's color; CardColors.GetColors honours color
        // indicator / devoid). The reveal pool is materialised before any prompt
        // so a misbehaving agent pick is sanitised against it.
        var blackInHand = controller.Zones.Hand.GetCards()
            .Where(c => CardColors.GetColors(c).Contains(ManaColor.Black))
            .ToList();

        // ----- X = the number of black cards the controller chooses to reveal.
        var revealed = await ChooseRevealAsync(rc, controller, blackInHand).ConfigureAwait(false);
        var x = revealed.Count;

        // CR 701.16 — public reveal of each chosen card.
        if (eventBus != null)
        {
            foreach (var c in revealed)
            {
                eventBus.Publish(new CardRevealedEvent(c, controller, ZoneType.Hand, CardName));
            }
        }

        // "If you do" — X == 0 means the player revealed nothing; the rider does
        // not apply (no −X/−X), so we are done.
        if (x <= 0) return;

        // Pick the chosen target (production: agent-set ChosenTargets; fallback:
        // first creature on the controller's battlefield).
        var target = PickTarget(owner, etb);
        if (target == null) return;

        // CR 608.2b — illegal-on-resolution check: the −X/−X fizzles cleanly even
        // though the reveal already happened.
        if (target.Zone != ZoneType.Battlefield) return;
        if (target.ActiveEffects == null) return; // pure-shape test path → no-op

        // CR 611 / CR 514.2 — register −X/−X until end of turn on the creature's
        // own ActiveEffects. SBAs (CR 704.5f) destroy a creature whose toughness
        // is 0 or less.
        target.ActiveEffects.Register(new PumpUntilEndOfTurnEffect(target, -x, -x));
    }

    /// <summary>
    /// Prompt the controller for which black cards to reveal — any number
    /// (<c>Min: 0</c>, <c>Max</c> = pool size), an optional "may" gesture. No
    /// live agent (single-arg dispatcher / agentless harness) ⇒ reveal ALL black
    /// cards in hand (the value-maximising deterministic default — a bigger X is
    /// strictly better for a −X/−X removal spell). A misbehaving agent pick is
    /// sanitised to the in-pool distinct subset.
    /// </summary>
    private static async ValueTask<IReadOnlyList<ICard>> ChooseRevealAsync(
        ResolutionContext rc, Player controller, IReadOnlyList<ICard> blackInHand)
    {
        if (blackInHand.Count == 0) return Array.Empty<ICard>();

        var agent = rc.Agent ?? AgentRegistry.Get(controller);
        if (agent == null)
        {
            // Deterministic fallback — reveal every black card (max X).
            return blackInHand;
        }

        var req = new ChoiceRequest(
            Kind: ChoiceKind.PickN,
            Description: $"{CardName}: reveal any number of black cards from your hand",
            Min: 0,
            Max: blackInHand.Count,
            Candidates: blackInHand.Cast<object>().ToList(),
            Intent: BotIntent.Removal,
            Optional: true);

        var chosen = await agent.ChooseAsync(rc.Game!, req, rc.Ct).ConfigureAwait(false);

        // Sanitise: distinct cards drawn from the black-hand pool only.
        var pool = new HashSet<ICard>(blackInHand);
        var seen = new HashSet<ICard>();
        var result = new List<ICard>(blackInHand.Count);
        foreach (var o in chosen)
        {
            if (o is ICard c && pool.Contains(c) && seen.Add(c)) result.Add(c);
        }
        return result;
    }

    /// <summary>
    /// Pick the −X/−X target. Honours an agent-set
    /// <see cref="TriggeredAbility.ChosenTargets"/> (production path); otherwise
    /// falls back to the first creature on the controller's battlefield
    /// (deterministic single-arg dispatcher posture — mirrors
    /// <see cref="AcidicSlimeFactory"/>).
    /// </summary>
    private static Creature? PickTarget(Player owner, TriggeredAbility? etb)
    {
        if (etb != null
            && etb.ChosenTargets.Count > 0
            && etb.ChosenTargets[0].Count > 0
            && etb.ChosenTargets[0][0] is Creature chosen)
        {
            return chosen;
        }

        return GatherCreatureTargets(owner).FirstOrDefault();
    }
}
