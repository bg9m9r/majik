using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Majik.Bot;
using Majik.Bot.Decks;
using Majik.Bot.OpponentModel;
using Majik.Bot.Search;
using Majik.Core.CardData;
using Majik.Core.Cards;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.StateMachine;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Bot.Tests.Search;

/// <summary>
/// Behavioral proof that the "honest-vs-human" INFERENCE path masks the opponent's
/// real hidden hand — the bot infers a belief from the opponent's PUBLIC cards only
/// and never peeks at the human's actual hand.
///
/// <para>
/// With <see cref="BotConfig.InferOpponentArchetype"/> true (+ <c>Strategy:"mcts"</c>,
/// <c>OpponentArchetype:null</c>), <see cref="SearchStrategy"/> reads the opponent's
/// public battlefield / graveyard / exile, infers an archetype belief
/// (<see cref="ArchetypeInferencer"/>), spreads K determinized worlds across it, and
/// resamples the opponent's hidden zones from the belief — instead of reading the real
/// hidden hand.
/// </para>
///
/// <para>
/// (1) <em>Masking — decisive.</em> Two games identical in every PUBLIC respect but
/// differing ONLY in the opponent's real hidden hand produce the SAME decision, for
/// both <see cref="SearchStrategy.PickAttackers"/> and
/// <see cref="SearchStrategy.PickPriorityAction"/>.
/// (2) <em>Teeth.</em> The two real hands genuinely differ, yet the inference INPUT
/// (opponent public cards) is identical → belief identical — the masking test is not
/// vacuous.
/// (3) <em>Belief shift.</em> No public cards → belief tracks the metagame prior; Burn
/// signature cards on the public board → belief's top archetype is Burn.
/// </para>
/// </summary>
public class InferenceMaskingTests
{
    private static readonly EmbeddedCardRepository Repo = new();
    private static readonly ScryfallCardFactory Factory = new(Repo);

    private static ICard Build(string name, Player owner) => Factory.Create(name, owner);

    // Two clearly-different, clone-clean opponent real HANDS (avoid Brainstorm — known
    // CloneForSim gap; these are the cards already used by the determinization/inference
    // suites). The hidden hand is the ONLY per-game difference.
    private static readonly string[] RealHandA = { "Lightning Bolt", "Lightning Bolt", "Goblin Guide" };
    private static readonly string[] RealHandB = { "Island", "Island", "Island" };

    /// <summary>
    /// The "honest-vs-human" config: inference ON, no explicit archetype, mcts strategy,
    /// fixed seed so both games sample identical worlds, small budgets for suite speed.
    /// </summary>
    private static BotConfig Config() =>
        new BotConfig(
            ArchetypeName: "Burn",
            Strategy: "mcts",
            RandomSeed: 7,
            MaxMctsIterations: 80,
            // 60 s budget so the deterministic iteration cap (80) governs the
            // search depth, never the wall clock — a low budget made the
            // masking invariant machine-timing-dependent and flaky on slow
            // (CI) hosts.
            MaxMctsBudgetMs: 60_000,
            OpponentArchetype: null,
            InferOpponentArchetype: true);

    /// <summary>
    /// A bundle of the live objects the search needs, plus the live opponent so the
    /// teeth test can read the real hidden hand and the opponent's public card names.
    /// </summary>
    private sealed record PublicGame(
        GameContext ctx,
        Player self,
        Player opp,
        IReadOnlyList<Creature> eligible);

    /// <summary>
    /// Builds a lethal-swing-style combat board (mirrors the determinization /
    /// inference fixtures). The searched seat (Alice) has two ready 2/2s and is
    /// dominant; the opponent (Bob) is at 3 life with no blockers, IDENTICAL public
    /// cards (a creature on the battlefield + a card in the graveyard) across both
    /// calls, and a DIFFERENT hidden hand per <paramref name="realOppHand"/>. Phase is
    /// Combat / DeclareAttackers.
    ///
    /// <para>Public (identical both games): Alice's board + life, Bob's battlefield +
    /// graveyard + exile + life, phase, turn. Hidden (the only difference): Bob's hand.
    /// Bob's library is identical-ish (irrelevant padding) both games.</para>
    /// </summary>
    private static PublicGame BuildPublicIdenticalGame(string[] realOppHand)
    {
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 3);

        // Searched seat: two ready 2/2 attackers (dominant board, lethal vs Bob @ 3).
        var bears = new List<Creature>();
        foreach (var n in new[] { "A", "B" })
        {
            var c = new Creature($"Bear{n}", "{1}{G}", 2, 2);
            c.ChangeOwner(alice);
            alice.Zones.Battlefield.AddCard(c);
            c.ClearSummoningSickness();
            bears.Add(c);
        }

        // Opponent PUBLIC cards — IDENTICAL across both games. A revealed creature on
        // the battlefield (it does not block: it stays summoning-sick / we just declare
        // attackers) plus a card in the graveyard. These are the inference INPUT.
        var revealed = new Creature("Goblin Guide", "{R}", 2, 2);
        revealed.ChangeOwner(bob);
        bob.Zones.Battlefield.AddCard(revealed);
        bob.Zones.GetZone(ZoneType.Graveyard).AddCard(Build("Lightning Bolt", bob));

        // Opponent's REAL hidden hand — the ONLY thing that differs between A and B.
        foreach (var n in realOppHand)
            bob.Zones.Hand.AddCard(Build(n, bob));

        // Identical libraries (hidden, padded so the engine doesn't draw-lose).
        foreach (var _ in Enumerable.Range(0, 15))
        {
            var f = new Land("Forest");
            f.ChangeOwner(alice);
            alice.Zones.GetZone(ZoneType.Library).AddCard(f);
            var g = new Land("Forest");
            g.ChangeOwner(bob);
            bob.Zones.GetZone(ZoneType.Library).AddCard(g);
        }

        var ctx = SearchTestCtx.AtCombat(alice, bob);
        return new PublicGame(ctx, alice, bob, bears);
    }

    /// <summary>
    /// Builds a pre-combat-main priority board mirroring the combat one: Alice has lands
    /// + a castable creature; Bob has IDENTICAL public cards across both games and a
    /// DIFFERENT hidden hand per <paramref name="realOppHand"/>.
    /// </summary>
    private static (GameContext ctx, Player self) BuildPriorityGame(string[] realOppHand)
    {
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);

        foreach (var _ in Enumerable.Range(0, 2))
        {
            var land = new Land("Forest");
            land.ChangeOwner(alice);
            alice.Zones.Battlefield.AddCard(land);
        }

        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        bear.ChangeOwner(alice);
        alice.Zones.Hand.AddCard(bear);

        // Opponent PUBLIC cards — IDENTICAL across both games (the inference input).
        var revealed = new Creature("Goblin Guide", "{R}", 2, 2);
        revealed.ChangeOwner(bob);
        bob.Zones.Battlefield.AddCard(revealed);
        bob.Zones.GetZone(ZoneType.Graveyard).AddCard(Build("Lightning Bolt", bob));

        // Opponent's REAL hidden hand — the ONLY per-game difference.
        foreach (var n in realOppHand)
            bob.Zones.Hand.AddCard(Build(n, bob));

        foreach (var _ in Enumerable.Range(0, 15))
        {
            var f = new Land("Forest");
            f.ChangeOwner(alice);
            alice.Zones.GetZone(ZoneType.Library).AddCard(f);
            var g = new Land("Forest");
            g.ChangeOwner(bob);
            bob.Zones.GetZone(ZoneType.Library).AddCard(g);
        }

        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var ctx = new GameContext(
            self: alice,
            allPlayers: new[] { alice, bob },
            activePlayer: alice,
            turnNumber: 3,
            currentPhase: StepStateType.PreCombatMain,
            stack: stack);

        return (ctx, alice);
    }

    private static List<string> Names(CombatPlan plan) =>
        plan.Attackers.Select(a => a.Attacker.Name).OrderBy(n => n).ToList();

    private static string ActionKey(PriorityAction action) => action switch
    {
        PriorityAction.CastSpell cs => $"Cast:{cs.Card.Name}",
        PriorityAction.PlayLand pl => $"PlayLand:{pl.Land.Name}",
        PriorityAction.PassAction => "Pass",
        _ => action.GetType().Name,
    };

    private static List<string> HiddenHandNames(PublicGame g) =>
        g.opp.Zones.Hand.GetCards().Select(c => c.Name).OrderBy(n => n).ToList();

    private static IReadOnlyList<string> OppPublicNames(PublicGame g) =>
        g.opp.Zones.Battlefield.GetCards()
            .Concat(g.opp.Zones.Graveyard.GetCards())
            .Concat(g.opp.Zones.Exile.GetCards())
            .Select(c => c.Name)
            .ToList();

    // ── (1) Masking — decisive ───────────────────────────────────────────────────

    [Fact]
    public void PickAttackers_Inference_IgnoresOpponentRealHand()
    {
        var g1 = BuildPublicIdenticalGame(realOppHand: new[] { "Lightning Bolt", "Lightning Bolt", "Goblin Guide" });
        var g2 = BuildPublicIdenticalGame(realOppHand: new[] { "Island", "Island", "Island" });
        var cfg = new BotConfig("Burn", Strategy: "mcts", RandomSeed: 7,
            MaxMctsIterations: 80, MaxMctsBudgetMs: 60_000, InferOpponentArchetype: true);

        var a1 = new SearchStrategy(cfg).PickAttackers(g1.ctx, g1.self, g1.eligible);
        var a2 = new SearchStrategy(cfg).PickAttackers(g2.ctx, g2.self, g2.eligible);

        Names(a1).Should().Equal(Names(a2),
            "inference reads only public cards — the real hidden hand must not change the decision");
    }

    [Fact]
    public void PickPriorityAction_Inference_IgnoresOpponentRealHand()
    {
        var (ctx1, self1) = BuildPriorityGame(new[] { "Lightning Bolt", "Lightning Bolt", "Goblin Guide" });
        var (ctx2, self2) = BuildPriorityGame(new[] { "Island", "Island", "Island" });
        // Masking needs the two runs to do IDENTICAL search work, so the
        // iteration cap (12/world after the 400 ms per-world split) must bind
        // long before the wall clock: now that the sandbox carries a
        // spell-definition resolver each iteration really CASTS sampled
        // spells (heavier sims), and 40 iterations/world flirted with the
        // 400 ms per-world wall under full-suite CPU contention — truncating
        // the two runs asymmetrically and flaking the equality.
        var cfg = new BotConfig("Burn", Strategy: "mcts", RandomSeed: 7,
            MaxMctsIterations: 80, MaxMctsBudgetMs: 60_000, InferOpponentArchetype: true);

        var p1 = new SearchStrategy(cfg).PickPriorityAction(ctx1, self1);
        var p2 = new SearchStrategy(cfg).PickPriorityAction(ctx2, self2);

        ActionKey(p1).Should().Be(ActionKey(p2),
            "inference reads only public cards — the real hidden hand must not change the priority decision");
    }

    // ── (2) Teeth / contrast — prove the masking test isn't vacuous ───────────────

    [Fact]
    public void Inference_BeliefIdenticalAcrossDifferentHiddenHands_ButHandsDiffer()
    {
        var g1 = BuildPublicIdenticalGame(new[] { "Lightning Bolt", "Lightning Bolt", "Goblin Guide" });
        var g2 = BuildPublicIdenticalGame(new[] { "Island", "Island", "Island" });

        // hidden hands really differ:
        HiddenHandNames(g1).Should().NotEqual(HiddenHandNames(g2));

        // inference input = opponent public cards → identical → belief identical:
        var inf = new ArchetypeInferencer();
        var b1 = inf.Infer(OppPublicNames(g1));
        var b2 = inf.Infer(OppPublicNames(g2));
        b1.Select(x => (x.Archetype, x.Weight)).Should().Equal(b2.Select(x => (x.Archetype, x.Weight)));
    }

    // ── (3) Belief shifts with public reveals ─────────────────────────────────────

    [Fact]
    public void Inference_BeliefShiftsTowardRevealedArchetype()
    {
        var inf = new ArchetypeInferencer();
        inf.Infer(Array.Empty<string>()).OrderByDescending(x => x.Weight).First().Archetype
            .Should().Be(MetagamePrior.All.OrderByDescending(x => x.Weight).First().Archetype);

        var burnSig = BotDeckCatalog.Get("Burn").Where(n => n is "Goblin Guide" or "Lava Spike").Distinct().ToList();
        burnSig.Should().NotBeEmpty();
        inf.Infer(burnSig).OrderByDescending(x => x.Weight).First().Archetype.Should().Be("Burn");
    }
}
