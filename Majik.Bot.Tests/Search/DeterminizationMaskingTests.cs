using FluentAssertions;
using Majik.Bot.Decks;
using Majik.Bot.Evaluation;
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
/// Behavioral proof that determinization MASKS the opponent's real hidden hand.
///
/// <para>
/// When <see cref="BotConfig.OpponentArchetype"/> is set, the search resamples the
/// opponent's hidden hand + libraries from the decklist per world (seeded), instead
/// of reading the opponent's ACTUAL hidden cards. The whole point of determinization
/// is that the bot must NOT gain information from the opponent's real hidden hand.
/// </para>
///
/// <para>
/// These tests build TWO games that are identical in every PUBLIC respect (same
/// searched-seat board + hand, same opponent battlefield / graveyard / life, same
/// phase) but DIFFER ONLY in the opponent's REAL hidden hand contents, then run the
/// determinized decision on each and assert the chosen move is the SAME — because a
/// determinized bot samples from the decklist and never reads the real hand, the real
/// hand's contents are invisible and must not change its decision.
/// </para>
///
/// <para>
/// The "teeth" test proves this isn't vacuous: the two real hands genuinely differ,
/// and the perfect-info path (null archetype) actually READS them — while the
/// determinized path returns decklist-sampled cards for both, regardless of the real
/// hand. Demonstrated via the test-only
/// <see cref="EngineSimulator.DebugSampledOpponentHand"/> hook.
/// </para>
/// </summary>
public class DeterminizationMaskingTests
{
    private static readonly EmbeddedCardRepository Repo = new();
    private static readonly ScryfallCardFactory Factory = new(Repo);

    private static ICard Build(string name, Player owner) => Factory.Create(name, owner);

    /// <summary>
    /// Small iteration cap so the determinized K-world search runs fast in the
    /// suite. Fixed RandomSeed so both games sample identical opponent worlds.
    ///
    /// <para>
    /// The wall-clock budget is set deliberately high (60 s) so the
    /// DETERMINISTIC <see cref="BotConfig.MaxMctsIterations"/> cap always
    /// governs the search depth — never the timer. A low wall-clock budget
    /// (the previous 800 ms) made the result machine-timing-dependent: on a
    /// slower host (CI) the search was cut off mid-iteration at a different
    /// count than on a fast host, so the masking invariant (decision identical
    /// across the two real opponent hands) flipped non-deterministically. Any
    /// change to per-iteration cost — e.g. growing the implemented-card pool —
    /// could expose that timing flake. Capping on iterations alone removes the
    /// timing coupling entirely.
    /// </para>
    /// </summary>
    private static BotConfig Config(string? opponentArchetype) =>
        new BotConfig(
            ArchetypeName: "Burn",
            Strategy: "mcts",
            RandomSeed: 7,
            MaxMctsIterations: 80,
            MaxMctsBudgetMs: 60_000,
            OpponentArchetype: opponentArchetype);

    // Two clearly-different, clone-clean opponent real hands (avoid Brainstorm —
    // known CloneForSim gap). These are the ONLY thing that differs between the two
    // games: every public fact (board, life, graveyard, phase) is identical.
    private static readonly string[] RealHandA = { "Lightning Bolt", "Lightning Bolt", "Goblin Guide" };
    private static readonly string[] RealHandB = { "Island", "Island", "Island" };

    /// <summary>
    /// Builds a combat board for <see cref="SearchStrategy.PickAttackers"/>. The
    /// searched seat (Alice) has two ready 2/2s; the opponent (Bob) is at 3 life with
    /// no blockers and a fixed graveyard. <paramref name="oppRealHand"/> is the ONLY
    /// per-game difference. Returns (ctx, self, eligible) ready for PickAttackers.
    /// </summary>
    private static (GameContext ctx, Player self, IReadOnlyList<Creature> eligible)
        BuildCombatBoard(string[] oppRealHand)
    {
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 3);

        var bears = new List<Creature>();
        foreach (var n in new[] { "A", "B" })
        {
            var c = new Creature($"Bear{n}", "{1}{G}", 2, 2);
            c.ChangeOwner(alice);
            alice.Zones.Battlefield.AddCard(c);
            c.ClearSummoningSickness();
            bears.Add(c);
        }

        // Identical opponent graveyard (a public zone) in both games.
        bob.Zones.GetZone(ZoneType.Graveyard).AddCard(Build("Mountain", bob));

        // Opponent's REAL hidden hand — the ONLY thing that differs between A and B.
        foreach (var n in oppRealHand)
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
        return (ctx, alice, bears);
    }

    /// <summary>
    /// Builds a priority board (pre-combat main) for
    /// <see cref="SearchStrategy.PickPriorityAction"/>. Alice has lands + a castable
    /// creature; the opponent's REAL hand is <paramref name="oppRealHand"/>, the only
    /// per-game difference.
    /// </summary>
    private static (GameContext ctx, Player self) BuildPriorityBoard(string[] oppRealHand)
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

        // Opponent's REAL hidden hand — the ONLY thing that differs between A and B.
        foreach (var n in oppRealHand)
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

    private static List<string> AttackerNames(CombatPlan plan) =>
        plan.Attackers.Select(a => a.Attacker.Name).OrderBy(n => n).ToList();

    // ── Masking: PickAttackers ───────────────────────────────────────────────────

    [Fact]
    public void PickAttackers_Determinized_SameDecision_RegardlessOfOpponentRealHand()
    {
        var (ctxA, selfA, eligibleA) = BuildCombatBoard(RealHandA);
        var (ctxB, selfB, eligibleB) = BuildCombatBoard(RealHandB);

        // Independent strategy instances, but IDENTICAL config (same OpponentArchetype,
        // same fixed RandomSeed) → both sample the SAME opponent worlds from the Burn
        // decklist, never the real hands.
        var planA = new SearchStrategy(Config("Burn")).PickAttackers(ctxA, selfA, eligibleA);
        var planB = new SearchStrategy(Config("Burn")).PickAttackers(ctxB, selfB, eligibleB);

        AttackerNames(planA).Should().Equal(
            AttackerNames(planB),
            "the determinized bot samples the opponent hand from the decklist and never "
            + "reads the real hidden cards, so the opponent's real hand (Bolt/Bolt/Goblin "
            + "Guide vs Island/Island/Island) is invisible and must not change the attack");
    }

    // ── Masking: PickPriorityAction ──────────────────────────────────────────────

    [Fact]
    public void PickPriorityAction_Determinized_SameDecision_RegardlessOfOpponentRealHand()
    {
        var (ctxA, selfA) = BuildPriorityBoard(RealHandA);
        var (ctxB, selfB) = BuildPriorityBoard(RealHandB);

        var actionA = new SearchStrategy(Config("Burn")).PickPriorityAction(ctxA, selfA);
        var actionB = new SearchStrategy(Config("Burn")).PickPriorityAction(ctxB, selfB);

        // Compare by a hand-name-independent key: action kind + (for plays) the card
        // name. Both games are independent object graphs, so reference equality across
        // them is meaningless; the masked decision must agree on WHAT to do.
        ActionKey(actionA).Should().Be(
            ActionKey(actionB),
            "determinization masks the opponent's real hand, so the searched seat's "
            + "priority decision must be identical across the two real hands");
    }

    private static string ActionKey(PriorityAction action) => action switch
    {
        PriorityAction.CastSpell cs => $"Cast:{cs.Card.Name}",
        PriorityAction.PlayLand pl => $"PlayLand:{pl.Land.Name}",
        PriorityAction.PassAction => "Pass",
        _ => action.GetType().Name,
    };

    // ── Teeth / contrast ─────────────────────────────────────────────────────────
    // Proves the masking test isn't vacuous: the two real hands genuinely DIFFER, the
    // perfect-info path actually READS them (returns hand A vs hand B distinctly),
    // and the determinized path returns decklist-sampled cards for BOTH — independent
    // of the real hand. If the real hands were the same, or determinization secretly
    // read them, this test fails.

    [Fact]
    public void Contrast_PerfectInfoReadsRealHand_ButDeterminizationSamplesFromDecklist()
    {
        // Build two roots that differ only in the opponent's real hand.
        var rootPerfectA = BuildSamplerRoot(RealHandA, worldSeed: null);
        var rootPerfectB = BuildSamplerRoot(RealHandB, worldSeed: null);
        var rootDetA = BuildSamplerRoot(RealHandA, worldSeed: 7);
        var rootDetB = BuildSamplerRoot(RealHandB, worldSeed: 7);

        var sim = new EngineSimulator(ArchetypeWeights.Default);

        var perfectA = sim.DebugSampledOpponentHand(rootPerfectA).OrderBy(n => n).ToList();
        var perfectB = sim.DebugSampledOpponentHand(rootPerfectB).OrderBy(n => n).ToList();
        var detA = sim.DebugSampledOpponentHand(rootDetA).OrderBy(n => n).ToList();
        var detB = sim.DebugSampledOpponentHand(rootDetB).OrderBy(n => n).ToList();

        // (1) The two real hands genuinely differ — the test has teeth.
        perfectA.Should().Equal(
            RealHandA.OrderBy(n => n).ToList(),
            "perfect-info READS the opponent's actual hand A");
        perfectB.Should().Equal(
            RealHandB.OrderBy(n => n).ToList(),
            "perfect-info READS the opponent's actual hand B");
        perfectA.Should().NotEqual(
            perfectB,
            "the two real opponent hands are clearly different (Burn vs Islands)");

        // (2) Determinization MASKS the real hand: both worlds sample from the Burn
        // decklist, and — crucially — the sampled hand does NOT depend on which real
        // hand was present (identical seed → identical sample for A and B).
        var burn = BotDeckCatalog.Get("Burn");
        detA.Should().OnlyContain(n => burn.Contains(n),
            "the determinized opponent hand is drawn from the Burn decklist, not the real hand");
        detB.Should().OnlyContain(n => burn.Contains(n));
        detA.Should().Equal(
            detB,
            "the determinized sample is identical across the two real hands — the real "
            + "hand is invisible to the determinized search");
    }

    /// <summary>
    /// Two-seat sampler root mirroring the masking boards: the opponent's real hand is
    /// <paramref name="oppRealHand"/> (the only difference). When
    /// <paramref name="worldSeed"/> is set the root is determinized against the Burn
    /// decklist; when null it stays perfect-info.
    /// </summary>
    private static SimState BuildSamplerRoot(string[] oppRealHand, int? worldSeed)
    {
        var self = new Player("Self", 20);
        var opp = new Player("Opp", 20);

        foreach (var n in oppRealHand)
            opp.Zones.Hand.AddCard(Build(n, opp));
        foreach (var _ in Enumerable.Range(0, 6))
        {
            opp.Zones.GetZone(ZoneType.Library).AddCard(Build("Mountain", opp));
            self.Zones.GetZone(ZoneType.Library).AddCard(Build("Forest", self));
        }

        var root = SimState.Capture(
            livePlayers: new[] { self, opp },
            activePlayer: self,
            turnNumber: 2,
            phase: PhaseStateType.PreCombatMain,
            searchedSeat: self);

        return worldSeed is int seed
            ? root.WithDeterminization(BotDeckCatalog.Get("Burn"), seed)
            : root;
    }
}
