using FluentAssertions;
using Majik.Bot.Evaluation;
using Majik.Bot.Search;
using Majik.Core.CardData;
using Majik.Core.Cards;
using Majik.Core.Players;
using Majik.Core.StateMachine;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Bot.Tests.Search;

/// <summary>
/// End-to-end "no-peek race play" behavioral tests for the two risk levers:
/// the <see cref="BoardEval.HiddenReachPenalty"/> eval term
/// (<see cref="ArchetypeWeights.HiddenReach"/>, 0 = kill-switch) and the
/// risk-aware two-tier vote in <see cref="DeterminizedSearch"/>
/// (<c>catastropheThreshold</c>, <see cref="double.NegativeInfinity"/> =
/// kill-switch). All tests run the REAL determinized pipeline: live fixture →
/// <see cref="SimState.WithDeterminization(System.Collections.Generic.IReadOnlyList{string}, int)"/>
/// → clone → <see cref="DeterminizationSampler"/> → engine sandbox → eval.
///
/// <para>
/// <b>Scope finding (verified empirically while building these tests — see PR
/// notes):</b> a defaults-vs-kill-switches DECISION flip is not constructible
/// against the current sandbox, because sampled hidden cards are built by
/// <see cref="ScryfallCardFactory"/> WITHOUT keyword markers (the embedded
/// seed has no keywords field) and without named-factory routing — so sampled
/// burn can never be cast in the sandbox (no spell-definition resolver in
/// <c>SandboxGame</c>) and sampled creatures have no haste (they can never
/// attack on the opponent's first turn of the horizon). Hand-conditional
/// catastrophes therefore cannot differentiate root moves. These tests pin the
/// strongest TRUE end-to-end behaviors instead: (1) the eval lever shifts
/// every sampled world's search values by EXACTLY the sampled-hand penalty and
/// the kill-switch zeroes it; (2) sampled hidden hands drive REAL win/death
/// divergence across determinized worlds, consumed by the risk vote (including
/// its documented all-catastrophic collapse); (3) masking survives the new
/// terms.
/// </para>
/// </summary>
public class NoPeekRacePlayTests
{
    private static readonly EmbeddedCardRepository Repo = new();
    private static readonly ScryfallCardFactory Factory = new(Repo);

    private static ICard Build(string name, Player owner) => Factory.Create(name, owner);

    /// <summary>
    /// Iteration-bounded, wall-clock-unbounded Mcts so runs are fully
    /// deterministic (no RNG in <see cref="Mcts"/>; <see cref="EngineSimulator"/>
    /// uses a fixed seed). <paramref name="iters"/> is kept at/near the root
    /// branching factor so values are pure rollouts of each root move.
    /// </summary>
    private static Mcts BuildMcts(ArchetypeWeights weights, int iters, int depth) =>
        new(new EngineSimulator(weights),
            new MctsConfig(MaxIterations: iters, MaxMillis: 600_000, DepthTurns: depth, ExplorationC: 1.41));

    // ─────────────────────────────────────────────────────────────────────────
    // (1) HiddenReach eval lever — end-to-end through the determinized pipeline.
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Priority-decision root: Alice (searched, active) at 5 life with a
    /// castable Grizzly Bears; Bob is inert (creatureless; his hidden zones
    /// hold ONLY Lightning Bolts, which the sandbox cannot cast). The hidden
    /// pool is exactly 8 Bolts (hand 4 + library 4) after subtracting Bob's 2
    /// visible Mountains, so EVERY world's sampled hand is 4 Bolts regardless
    /// of seed: reach 12, penalty = 12 − (5 − 1) = 8.
    /// </summary>
    private static SimState BuildBoltReachRoot(int baseSeed)
    {
        var alice = new Player("Alice", 5);
        var bob = new Player("Bob", 20);

        for (var i = 0; i < 2; i++)
            alice.Zones.Battlefield.AddCard(Build("Forest", alice));
        alice.Zones.Hand.AddCard(Build("Grizzly Bears", alice));

        for (var i = 0; i < 2; i++)
            bob.Zones.Battlefield.AddCard(Build("Mountain", bob));
        for (var i = 0; i < 4; i++)
            bob.Zones.Hand.AddCard(Build("Mountain", bob));       // sampler refills
        for (var i = 0; i < 4; i++)
            bob.Zones.GetZone(ZoneType.Library).AddCard(Build("Mountain", bob));

        foreach (var _ in Enumerable.Range(0, 10))
            alice.Zones.GetZone(ZoneType.Library).AddCard(Build("Forest", alice));

        var root = SimState.Capture(
            new[] { alice, bob }, alice, 3, PhaseStateType.PreCombatMain, searchedSeat: alice);

        var deck = new List<string>();
        deck.AddRange(Enumerable.Repeat("Lightning Bolt", 8));
        deck.AddRange(Enumerable.Repeat("Mountain", 2)); // the 2 visible ones
        return root.WithDeterminization(deck, baseSeed);
    }

    [Fact]
    public void HiddenReachLever_ShiftsEverySampledWorldValue_ExactlyByPenalty_AndKillSwitchZeroesIt()
    {
        // penalty = reach 12 (4 sampled Bolts × 3) − (5 life − 1 margin) = 8.
        const double ExpectedPenalty = 8.0;

        var wOn = ArchetypeWeights.Default;                       // HiddenReach = 1.0
        var wOff = ArchetypeWeights.Default with { HiddenReach = 0.0 };
        wOn.HiddenReach.Should().Be(1.0, "the preset default must be the ON state");

        var mctsOn = BuildMcts(wOn, iters: 4, depth: 0);
        var mctsOff = BuildMcts(wOff, iters: 4, depth: 0);

        var rootOn = BuildBoltReachRoot(baseSeed: 7);
        var rootOff = BuildBoltReachRoot(baseSeed: 7);
        var sim = new EngineSimulator(wOn);

        for (var w = 0; w < 4; w++)
        {
            var worldOn = rootOn.WithWorldSeed(7 + w);
            var worldOff = rootOff.WithWorldSeed(7 + w);

            // Premise: the sampled hand really is all burn in every world.
            sim.DebugSampledOpponentHand(worldOn)
                .Should().OnlyContain(n => n == "Lightning Bolt",
                    "the hidden pool is exactly 8 Bolts, so every sampled hand is all-Bolt");

            var statsOn = mctsOn.SearchWithStats(worldOn).RootStats
                .ToDictionary(s => s.Move.Key, s => s.TotalValue / s.Visits);
            var statsOff = mctsOff.SearchWithStats(worldOff).RootStats
                .ToDictionary(s => s.Move.Key, s => s.TotalValue / s.Visits);

            statsOn.Keys.Should().BeEquivalentTo(statsOff.Keys,
                "the lever only changes leaf eval, never the legal-move set or rollout trajectory");

            foreach (var key in statsOn.Keys)
            {
                (statsOff[key] - statsOn[key]).Should().BeApproximately(
                    ExpectedPenalty, 1e-9,
                    because: $"world {w} move '{key}': HiddenReach 1.0 must subtract exactly the "
                           + "sampled-hand burn-reach penalty (8) from the leaf eval, and "
                           + "HiddenReach 0 (the WeightsOverride kill-switch) must remove the "
                           + "term entirely — proving the lever is live end-to-end through "
                           + "clone → sampler → sandbox → eval");
            }
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // (2) Risk-vote pipeline — real hand-conditional wins/deaths across worlds.
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Race board, attack-decision root: Alice (searched, active) at 3 life
    /// with two ready 2/2s; Bob at 7 with no creatures and 4 Mountains. The
    /// opponent decklist is 1 Hellrider + 11 Mountain. In "safe" worlds
    /// (Hellrider not sampled into hand) the all-out race kills Bob on the
    /// second swing (+1000 real terminal win). In "deadly" worlds Bob casts
    /// the sampled Hellrider (a real castable body) and its crack-back kills
    /// Alice within the horizon (−1000 real terminal loss). The win/death
    /// split is decided ONLY by what the sampler put in Bob's hidden hand.
    /// </summary>
    private static SimState BuildHellriderRaceRoot(int baseSeed)
    {
        var alice = new Player("Alice", 3);
        var bob = new Player("Bob", 7);

        foreach (var n in new[] { "A", "B" })
        {
            var c = new Creature($"Bear{n}", "{1}{G}", 2, 2);
            c.ChangeOwner(alice);
            alice.Zones.Battlefield.AddCard(c);
            c.ClearSummoningSickness();
        }

        for (var i = 0; i < 4; i++)
            bob.Zones.Battlefield.AddCard(Build("Mountain", bob));
        for (var i = 0; i < 3; i++)
            bob.Zones.Hand.AddCard(Build("Mountain", bob));
        for (var i = 0; i < 5; i++)
            bob.Zones.GetZone(ZoneType.Library).AddCard(Build("Mountain", bob));

        foreach (var _ in Enumerable.Range(0, 12))
            alice.Zones.GetZone(ZoneType.Library).AddCard(Build("Forest", alice));

        var root = SimState.Capture(
            new[] { alice, bob }, alice, 3, PhaseStateType.Combat, searchedSeat: alice);

        var deck = new List<string> { "Hellrider" };
        deck.AddRange(Enumerable.Repeat("Mountain", 11));
        return root.WithDeterminization(deck, baseSeed);
    }

    [Fact]
    public void RiskVotePipeline_SampledHiddenHands_DriveRealWinsAndDeaths_EndToEnd()
    {
        // depth 3 lets the race resolve: t3 all-out (Bob 7→3), t4 Bob casts the
        // sampled Hellrider (deadly worlds), t5 Alice's second swing (+1000 in
        // safe worlds) / t6 Hellrider crack-back (−1000 in deadly worlds —
        // sampled cards carry no haste, see class doc, so the crack lands a
        // turn later but still inside the horizon).
        var weights = ArchetypeWeights.Default;
        var mcts = BuildMcts(weights, iters: 4, depth: 3);
        var sim = new EngineSimulator(weights);

        var root = BuildHellriderRaceRoot(baseSeed: 7);

        var tally = new Dictionary<string, DeterminizedSearch.KeyTally>();
        var sawDeadlyWorld = false;
        var sawSafeWorld = false;

        for (var w = 0; w < 4; w++)
        {
            var world = root.WithWorldSeed(7 + w);
            var deadly = sim.DebugSampledOpponentHand(world).Contains("Hellrider");

            var res = mcts.SearchWithStats(world);
            var allOut = res.RootStats.Single(s => s.Move.IsAllOutAttack);
            var mean = allOut.TotalValue / allOut.Visits;

            if (deadly)
            {
                sawDeadlyWorld = true;
                mean.Should().BeLessThan(-500,
                    "in a world whose SAMPLED hand holds Hellrider the all-out race line "
                    + "really dies to the crack-back through Alice's tapped-out board — a "
                    + "genuine terminal loss produced by the engine, conditional only on "
                    + "the sampler's hidden-hand draw");
            }
            else
            {
                sawSafeWorld = true;
                mean.Should().Be(1000,
                    "in a world whose sampled hand is all Mountains the all-out race "
                    + "kills Bob on the second swing — a genuine terminal win");
            }

            DeterminizedSearch.Accumulate(tally, res.RootStats);
        }

        // The fixed seeds must produce both world kinds, or the test is vacuous.
        sawDeadlyWorld.Should().BeTrue("seeds 7..10 must sample Hellrider into at least one hand");
        sawSafeWorld.Should().BeTrue("seeds 7..10 must leave at least one hand Hellrider-free");

        // The cross-world tally records the all-out line's worst world as a real
        // catastrophe — this is exactly the signal the risk-aware vote consumes.
        tally.Values.Single(t => t.Move.IsAllOutAttack).MinWorldMean
            .Should().BeLessThan(-500,
                "the risk vote sees the all-out line's sampled-world death via MinWorldMean");

        // End-to-end through DeterminizedSearch.Run: on THIS board every line is
        // catastrophic in the deadly worlds (the rollout policy re-attacks each
        // turn, so by the crack-back turn every line is equally tapped out) —
        // the documented all-catastrophic collapse applies and the bot still
        // races, with the default threshold and with the kill-switch alike.
        var runDefault = DeterminizedSearch.Run(
            BuildMcts(weights, iters: 4, depth: 3), BuildHellriderRaceRoot(baseSeed: 7),
            totalBudgetMs: 1600, perWorldBudgetMs: 400);
        var runDisabled = DeterminizedSearch.Run(
            BuildMcts(weights, iters: 4, depth: 3), BuildHellriderRaceRoot(baseSeed: 7),
            totalBudgetMs: 1600, perWorldBudgetMs: 400,
            catastropheThreshold: double.NegativeInfinity);

        runDefault.IsAllOutAttack.Should().BeTrue(
            "when every line dies somewhere the two-tier vote deliberately collapses "
            + "to the legacy order and the bot still races (DeterminizedSearch.Vote doc)");
        runDisabled.Key.Should().Be(runDefault.Key,
            "with all moves catastrophic the collapse makes the default threshold and "
            + "the -infinity kill-switch agree by construction");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // (3) Masking — the new terms at defaults still never read the REAL hand.
    // ─────────────────────────────────────────────────────────────────────────

    // Two clearly-different opponent REAL hands. Hand A is exactly the shape the
    // new eval term reacts to (burn in hand, bot at low life): if any part of
    // the new pipeline peeked at the REAL hand, game A would evaluate as
    // mortally dangerous and game B as harmless.
    private static readonly string[] RealHandBurn = { "Lightning Bolt", "Lightning Bolt", "Lightning Bolt" };
    private static readonly string[] RealHandLands = { "Island", "Island", "Island" };

    /// <summary>
    /// Mirrors the InferenceMaskingTests combat fixture, but the searched seat
    /// sits at 5 life so <see cref="BoardEval.HiddenReachPenalty"/> WOULD fire
    /// (reach 9 vs 5 life) if the real hidden hand ever leaked into the eval.
    /// Public state is identical across both games; ONLY the real hidden hand
    /// differs.
    /// </summary>
    private static (Majik.Core.Game.GameContext ctx, Player self, IReadOnlyList<Creature> eligible)
        BuildLowLifeCombatBoard(string[] oppRealHand)
    {
        var alice = new Player("Alice", 5);
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

        // Identical opponent public cards (battlefield + graveyard) in both games.
        var revealed = new Creature("Goblin Guide", "{R}", 2, 2);
        revealed.ChangeOwner(bob);
        bob.Zones.Battlefield.AddCard(revealed);
        bob.Zones.GetZone(ZoneType.Graveyard).AddCard(Build("Lightning Bolt", bob));
        bob.Zones.Battlefield.AddCard(Build("Mountain", bob));
        bob.Zones.Battlefield.AddCard(Build("Mountain", bob));

        // The REAL hidden hand — the ONLY difference between the two games.
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

        var ctx = SearchTestCtx.AtCombat(alice, bob);
        return (ctx, alice, bears);
    }

    [Fact]
    public void PickAttackers_InferenceWithNewTermsAtDefaults_IgnoresOpponentRealHand()
    {
        var (ctxA, selfA, eligibleA) = BuildLowLifeCombatBoard(RealHandBurn);
        var (ctxB, selfB, eligibleB) = BuildLowLifeCombatBoard(RealHandLands);

        // Defaults for BOTH new terms: RiskVoteThreshold null (→ −500 default
        // risk filter ON) and no WeightsOverride (→ HiddenReach 1.0). Inference
        // ON, fixed seed → identical sampled worlds across the two games.
        var cfg = new BotConfig("Burn", Strategy: "mcts", RandomSeed: 7,
            MaxMctsIterations: 80, MaxMctsBudgetMs: 800, InferOpponentArchetype: true);
        cfg.RiskVoteThreshold.Should().BeNull("the risk filter must be at its ON default");
        cfg.WeightsOverride.Should().BeNull("HiddenReach must be at its 1.0 default");

        var planA = new SearchStrategy(cfg).PickAttackers(ctxA, selfA, eligibleA);
        var planB = new SearchStrategy(cfg).PickAttackers(ctxB, selfB, eligibleB);

        var namesA = planA.Attackers.Select(a => a.Attacker.Name).OrderBy(n => n).ToList();
        var namesB = planB.Attackers.Select(a => a.Attacker.Name).OrderBy(n => n).ToList();

        namesA.Should().Equal(namesB,
            "with the risk vote and HiddenReach at defaults the eval reads only the "
            + "SAMPLED sandbox hand — identical worlds across both games — so the "
            + "opponent's real hidden hand (triple Bolt vs triple Island, against a "
            + "5-life bot the burn hand would terrify) must not change the decision");
    }
}
