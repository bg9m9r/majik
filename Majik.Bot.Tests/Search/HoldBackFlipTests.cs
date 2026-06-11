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
/// THE payoff test for the risk-aware levers (PR "risk-aware determinized
/// vote") riding on sampled-card fidelity (this branch): a determinized board
/// where the racing line genuinely DIES to sampled burn cast in-sim, and the
/// levers FLIP the decision end-to-end through <see cref="DeterminizedSearch.Run"/>:
/// defaults hold back, kill-switches race.
///
/// <para>
/// <b>Why this board could not exist before sampled-card fidelity:</b> sampled
/// cards used to be inert shells — a sampled Lightning Bolt could never be
/// cast in-sim, so no determinized world could ever realize a catastrophe and
/// the risk vote had nothing to bite on (NoPeekRacePlayTests pinned the prior
/// limit: the only constructible catastrophes were all-line collapses). With
/// the per-world materialized base the opponent CASTS the sampled Bolt and the
/// searched seat really dies — but only on the lines that over-committed.
/// </para>
///
/// <para>
/// <b>The board</b> (engine-behavior-exact; all facts verified by tracing the
/// real sandbox):
/// Alice (searched, active, turn 3, Combat) at 8 life: four 1/1 Squires + one
/// 2/4 Guard, all ready. Bob at 6 life: a 4/5 Killer + a 3/3 Cracker + four
/// TAPPED Mountains (tapped out: he cannot interact during Alice's combat;
/// they untap on his turn 4), a 4-card hidden hand and an 8-card hidden
/// library, decklist 3 Lightning Bolt + 13 Mountain.
/// </para>
///
/// <para>
/// The line-dependence that makes a per-line (not all-line) catastrophe:
/// <list type="bullet">
///   <item>Sampled Bolts are cast greedily on Bob's turn and resolve at
///   Alice's FACE (the damage-any template publishes no candidate pool, so
///   <c>TargetPolicy.SynthesizeDefaults</c> aims "any target" at the
///   opponent) — 3 face damage per sampled Bolt, in every line.</item>
///   <item>Bob's 3/3 Cracker attacks on turn 4 ONLY when Alice's Guard is
///   gone: the opponent's combat search only respects hard blocks
///   (blocker toughness &gt; attacker power), and the 2/4 Guard is the only
///   body that hard-blocks a 3-power attacker. The 4/5 Killer is never
///   deterred and attacks every turn 4 (no Alice body has toughness 5).</item>
///   <item>Any attack that INCLUDES the Guard feeds it to the Killer's block
///   (the engine's block policy hard-blocks the highest-power attacker:
///   Killer, toughness 5 &gt; 2, blocks the Guard and kills it, 4 ≥ 4).</item>
/// </list>
/// So on Bob's turn 4: Guard-committing lines take Killer 4 + Cracker 3 +
/// 3 per sampled Bolt = 10 ≥ 8 when ONE Bolt was sampled → a real in-sim
/// death; Guard-home lines take Killer 4 + 3 = 7 → survive at 1 life.
/// In Bolt-free worlds the all-out race is the only +1000: 3 Squires connect
/// at the root (Killer blocks Guard, Cracker eats a Squire) → Bob 6→3, and on
/// turn 5 the three surviving Squires finish through Bob's TAPPED attackers
/// (3 ≥ 3) — a genuine terminal win the partial lines cannot reach (they
/// leave Bob at 1).
/// </para>
///
/// <para>
/// Seeds: baseSeed 128 → <see cref="DeterminizedSearch.Run"/> searches worlds
/// 128..131 (K = 4 from the 1600/400 budgets), whose sampled hands hold
/// exactly [1,0,0,0] Bolts. Everything is deterministic: iteration-bounded
/// MCTS (no RNG), fixed engine seed, fixed world seeds.
/// </para>
/// </summary>
public sealed class HoldBackFlipTests
{
    private static readonly EmbeddedCardRepository Repo = new();
    private static readonly ScryfallCardFactory Factory = new(Repo);

    private static ICard Build(string name, Player owner) => Factory.Create(name, owner);

    /// <summary>Base seed whose 4-world window is [1,0,0,0] sampled Bolts.</summary>
    private const int BaseSeed = 128;

    private const string AllOutKey = "Attack:{SquireA,SquireB,SquireC,SquireD,Guard}";
    private const string HoldBackKey = "Attack:{}";

    /// <summary>
    /// Iteration-bounded, wall-clock-unbounded Mcts (same determinism pattern
    /// as NoPeekRacePlayTests): 32 iterations = the root branching factor
    /// (2^5 attacker subsets), so every root move gets exactly one rollout and
    /// the per-world values are pure line evaluations. Depth 2 → horizon
    /// turns 3..5 (Alice / Bob / Alice).
    /// </summary>
    private static Mcts BuildMcts(
        ArchetypeWeights weights, RolloutDepth rolloutDepth = RolloutDepth.FullTurnPlus) =>
        new(new EngineSimulator(weights),
            new MctsConfig(MaxIterations: 32, MaxMillis: 600_000, DepthTurns: 2, ExplorationC: 1.41,
                RolloutDepth: rolloutDepth));

    private static SimState BuildHoldBackRaceRoot(int baseSeed)
    {
        var alice = new Player("Alice", 8);
        var bob = new Player("Bob", 6);

        foreach (var n in new[] { "A", "B", "C", "D" })
        {
            var c = new Creature($"Squire{n}", "{W}", 1, 1);
            c.ChangeOwner(alice);
            alice.Zones.Battlefield.AddCard(c);
            c.ClearSummoningSickness();
        }
        var guard = new Creature("Guard", "{1}{W}", 2, 4);
        guard.ChangeOwner(alice);
        alice.Zones.Battlefield.AddCard(guard);
        guard.ClearSummoningSickness();

        var killer = new Creature("Killer", "{2}{B}{B}", 4, 5);
        killer.ChangeOwner(bob);
        bob.Zones.Battlefield.AddCard(killer);
        killer.ClearSummoningSickness();
        var cracker = new Creature("Cracker", "{1}{B}{B}", 3, 3);
        cracker.ChangeOwner(bob);
        bob.Zones.Battlefield.AddCard(cracker);
        cracker.ClearSummoningSickness();

        // Tapped out: no instant-speed interaction during Alice's root combat;
        // all four untap for Bob's turn 4 so every sampled Bolt is castable.
        for (var i = 0; i < 4; i++)
        {
            var m = (Permanent)Build("Mountain", bob);
            bob.Zones.Battlefield.AddCard(m);
            m.Tap();
        }

        // Real hidden zones only set the SIZES the sampler re-deals (4 + 8);
        // their contents are replaced in every seeded world.
        for (var i = 0; i < 4; i++)
            bob.Zones.Hand.AddCard(Build("Mountain", bob));
        for (var i = 0; i < 8; i++)
            bob.Zones.GetZone(ZoneType.Library).AddCard(Build("Mountain", bob));

        foreach (var _ in Enumerable.Range(0, 12))
            alice.Zones.GetZone(ZoneType.Library).AddCard(Build("Forest", alice));

        var root = SimState.Capture(
            new[] { alice, bob }, alice, 3, PhaseStateType.Combat, searchedSeat: alice);

        var deck = new List<string>();
        deck.AddRange(Enumerable.Repeat("Lightning Bolt", 3));
        deck.AddRange(Enumerable.Repeat("Mountain", 13));
        return root.WithDeterminization(deck, baseSeed);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // (1) THE FLIP — defaults hold back, kill-switches race.
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void RiskLevers_FlipTheDecision_DefaultsHoldBack_KillSwitchesRace()
    {
        var wOn = ArchetypeWeights.Default;
        wOn.HiddenReach.Should().Be(1.0, "the preset default must be the eval lever's ON state");
        var wOff = wOn with { HiddenReach = 0.0 };

        // ── Per-world premise + value traces ─────────────────────────────────
        // The window must contain a Bolt world AND Bolt-free worlds; in the
        // Bolt world the all-out line really dies IN-SIM to the sampled burn
        // while the hold-back line survives — the previously-unconstructible
        // divergence (pre-fidelity, sampled burn could never be cast, so no
        // line could die in one world and win in another).
        var mcts = BuildMcts(wOn);
        var sim = new EngineSimulator(wOn);
        var root = BuildHoldBackRaceRoot(BaseSeed);

        var tally = new Dictionary<string, DeterminizedSearch.KeyTally>();
        var sawDeadly = false;
        var sawCalm = false;

        for (var w = 0; w < 4; w++)
        {
            var world = root.WithWorldSeed(BaseSeed + w);
            var bolts = sim.DebugSampledOpponentHand(world).Count(n => n == "Lightning Bolt");

            var stats = mcts.SearchWithStats(world).RootStats;
            var allOut = stats.Single(s => s.Move.IsAllOutAttack);
            var holdBack = stats.Single(s => s.Move.IsEmptyAttack);
            var allOutMean = allOut.TotalValue / allOut.Visits;
            var holdBackMean = holdBack.TotalValue / holdBack.Visits;

            if (bolts > 0)
            {
                sawDeadly = true;
                allOutMean.Should().BeLessThan(-500,
                    $"world {BaseSeed + w}: the all-out race feeds the Guard to the Killer's "
                    + "block and taps out, so the freed Cracker + Killer crack-back plus the "
                    + "sampled Bolt CAST IN-SIM at Alice's face (4+3+3 = 10 ≥ 8) is a genuine "
                    + "terminal death — conditional only on the sampler's hidden-hand draw");
                holdBackMean.Should().BeGreaterThan(-500,
                    $"world {BaseSeed + w}: holding everything back keeps the Guard home, the "
                    + "Cracker stays deterred, and Killer 4 + Bolt 3 = 7 < 8 — the SAME sampled "
                    + "world that kills the race is survivable for the safe line, which is "
                    + "exactly the per-line divergence the risk vote needs");
            }
            else
            {
                sawCalm = true;
                allOutMean.Should().Be(1000,
                    $"world {BaseSeed + w}: with no Bolt sampled the all-out race genuinely "
                    + "wins — 3 Squires connect at the root (Bob 6→3) and the survivors finish "
                    + "through Bob's tapped attackers on turn 5");
            }

            DeterminizedSearch.Accumulate(tally, stats);
        }

        sawDeadly.Should().BeTrue("seeds 128..131 must sample a Bolt into at least one hand");
        sawCalm.Should().BeTrue("seeds 128..131 must leave at least one hand Bolt-free");

        // The cross-world tally carries exactly the signal the two-tier vote
        // consumes: the race died somewhere, the hold-back died nowhere.
        tally[AllOutKey].MinWorldMean.Should().BeLessThan(-500,
            "the risk vote must see the all-out line's sampled-world death via MinWorldMean");
        tally[HoldBackKey].MinWorldMean.Should().BeGreaterThan(-500,
            "the hold-back line must be SAFE in every sampled world — without a safe "
            + "alternative the vote deliberately collapses to the legacy order");

        // ── THE FLIP, end-to-end through DeterminizedSearch.Run ─────────────
        // Defaults: risk vote at −500 + HiddenReach 1.0 → hold back.
        var defaults = DeterminizedSearch.Run(
            BuildMcts(wOn), BuildHoldBackRaceRoot(BaseSeed),
            totalBudgetMs: 1600, perWorldBudgetMs: 400);

        // Kill-switches: catastropheThreshold −∞ + HiddenReach 0 → race.
        var killSwitches = DeterminizedSearch.Run(
            BuildMcts(wOff), BuildHoldBackRaceRoot(BaseSeed),
            totalBudgetMs: 1600, perWorldBudgetMs: 400,
            catastropheThreshold: double.NegativeInfinity);

        killSwitches.IsAllOutAttack.Should().BeTrue(
            "with both levers disabled the vote is the legacy summed-robust-child: the "
            + "all-out race wins 3 of 4 sampled worlds (+1000 each) and its summed mean "
            + "dwarfs every hold-back leaf eval, so the bot races into the sampled burn");
        killSwitches.Key.Should().Be(AllOutKey);

        defaults.IsAllOutAttack.Should().BeFalse(
            "at defaults the risk vote demotes every line that died in a sampled world "
            + "below the safe tier — the bot must NOT race");
        defaults.IsEmptyAttack.Should().BeTrue(
            "the empty attack is the best safe line (it died in no world and carries the "
            + "highest safe summed mean), so the defaults specifically HOLD BACK");
        defaults.Key.Should().Be(HoldBackKey);

        defaults.Key.Should().NotBe(killSwitches.Key,
            "the decision must genuinely FLIP: the levers, fed by burn that sampled-card "
            + "fidelity made castable in-sim, change what the bot does at the root");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // (1b) THE FLIP, per RolloutDepth — pinned risk-signal survival table.
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Pins whether the hold-back flip SURVIVES each <see cref="RolloutDepth"/>
    /// (the #2596 rollout-truncation lever) — gate data for adopting a cheaper
    /// depth live. Outcomes were MEASURED first, then pinned (per-world means in
    /// parentheses are the observed values on this board, seeds 128..131):
    ///
    /// <list type="bullet">
    ///   <item><b>FullTurnPlus — FLIPS</b> (hard regression assert, same as the
    ///   default-depth test above): the playout reaches Bob's turn 4, the
    ///   sampled Bolt world realizes a genuine terminal death on the all-out
    ///   line (allOutMean −1000 in world 128 vs +1000 in the calm worlds),
    ///   MinWorldMean trips the risk vote, defaults hold back.</item>
    ///   <item><b>EndOfTurn — NO FLIP</b> (expected signal starvation): the
    ///   playout stops at Alice's turn-3 boundary, BEFORE Bob untaps — the
    ///   catastrophe (Killer 4 + Cracker 3 + Bolt 3 on Bob's turn 4) lies
    ///   entirely beyond the horizon. No world ever realizes a death; every
    ///   world evaluates to the same benign turn-cap evals (allOut 33.2 &gt;
    ///   holdBack 26.5 — the race looks BETTER because 3 Squires connected),
    ///   MinWorldMean starves, the risk vote has nothing to bite on, and both
    ///   defaults and kill-switches race. NOTE: this board's crack-back is
    ///   NEXT-turn; EndOfTurn still sees same-turn deaths (e.g. lethal already
    ///   on the stack), it loses exactly the +1-turn losses pinned here.</item>
    ///   <item><b>LeafEval — NO FLIP</b> (expected signal starvation): no
    ///   playout at all — the sampled Bolt is never CAST, so no terminal loss
    ///   can exist in any world. The materialized Bolt in world 128's hand only
    ///   shades BOTH leaf evals equally via HiddenReach (allOut 29.2 /
    ///   holdBack 22.5 vs 33.2 / 26.5 in calm worlds), preserving the race's
    ///   lead; MinWorldMean stays benign (~29 ≫ −500) → defaults race too.</item>
    /// </list>
    ///
    /// The pin is mechanism-level: per depth we assert the MinWorldMean signal
    /// (present / starved) AND the end-to-end decision, so a future change that
    /// silently revives or re-starves the risk signal at a truncated depth
    /// fails loudly here.
    /// </summary>
    [Theory]
    [InlineData(RolloutDepth.FullTurnPlus, true)]
    [InlineData(RolloutDepth.EndOfTurn, false)]
    [InlineData(RolloutDepth.LeafEval, false)]
    public void RiskSignal_PerRolloutDepth_PinnedFlipOutcome(RolloutDepth depth, bool flipSurvives)
    {
        var wOn = ArchetypeWeights.Default;
        var wOff = wOn with { HiddenReach = 0.0 };

        // ── Mechanism pin: does ANY world realize the all-out death? ─────────
        var mcts = BuildMcts(wOn, depth);
        var root = BuildHoldBackRaceRoot(BaseSeed);
        var tally = new Dictionary<string, DeterminizedSearch.KeyTally>();
        for (var w = 0; w < 4; w++)
            DeterminizedSearch.Accumulate(tally, mcts.SearchWithStats(root.WithWorldSeed(BaseSeed + w)).RootStats);

        if (flipSurvives)
        {
            tally[AllOutKey].MinWorldMean.Should().BeLessThan(-500,
                $"{depth}: the playout horizon must reach Bob's turn 4, where the sampled-Bolt "
                + "world kills the over-committed race — the terminal-loss signal the risk vote feeds on");
            tally[HoldBackKey].MinWorldMean.Should().BeGreaterThan(-500,
                $"{depth}: the hold-back line survives every sampled world — the safe alternative "
                + "the vote needs to demote the race");
        }
        else
        {
            tally[AllOutKey].MinWorldMean.Should().BeGreaterThan(-500,
                $"{depth}: the catastrophe (Bob's turn-4 Killer + Cracker + sampled Bolt) lies "
                + "beyond this truncated horizon — no world ever realizes the death, so the "
                + "MinWorldMean risk signal STARVES (expected; this is the gate's per-depth cost)");
        }

        // ── End-to-end pin through DeterminizedSearch.Run ────────────────────
        var defaults = DeterminizedSearch.Run(
            BuildMcts(wOn, depth), BuildHoldBackRaceRoot(BaseSeed),
            totalBudgetMs: 1600, perWorldBudgetMs: 400);
        var killSwitches = DeterminizedSearch.Run(
            BuildMcts(wOff, depth), BuildHoldBackRaceRoot(BaseSeed),
            totalBudgetMs: 1600, perWorldBudgetMs: 400,
            catastropheThreshold: double.NegativeInfinity);

        killSwitches.Key.Should().Be(AllOutKey,
            $"{depth}: with both levers disabled the legacy summed-robust-child vote always "
            + "races — the race carries the highest summed mean at every depth on this board");

        if (flipSurvives)
        {
            defaults.Key.Should().Be(HoldBackKey,
                $"{depth}: the risk vote sees the sampled-world death and demotes the race — "
                + "the flip must survive at the default depth (hard regression)");
            defaults.Key.Should().NotBe(killSwitches.Key, "the decision must genuinely flip");
        }
        else
        {
            defaults.Key.Should().Be(AllOutKey,
                $"{depth}: with the terminal-loss signal starved by the truncated horizon the "
                + "risk vote finds no catastrophe to demote, so defaults race exactly like the "
                + "kill-switches — the flip does NOT survive this depth (pinned gate data)");
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // (2) Lever attribution — which lever drives the flip.
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Same board, one lever at a time. The vote filter alone flips the
    /// decision; the eval term alone does not. The eval term is inert HERE
    /// because the opponent greedily CASTS every sampled Bolt during the
    /// horizon (fidelity!), so no leaf state still holds burn in hand and
    /// <see cref="BoardEval.HiddenReachPenalty"/> reads reach 0 — the
    /// catastrophe arrives as a real terminal loss, not as a leaf-eval shade.
    /// This pins the gate readout: on burn-actualizing boards the risk VOTE is
    /// the operative lever, while HiddenReach only matters when sampled burn
    /// stays uncast at the horizon (NoPeekRacePlayTests pins that case).
    /// </summary>
    [Fact]
    public void LeverAttribution_VoteFilterAloneFlips_EvalTermAloneDoesNot()
    {
        var wOn = ArchetypeWeights.Default;            // HiddenReach 1.0
        var wOff = wOn with { HiddenReach = 0.0 };     // eval lever killed

        // Vote filter ON (default −500), eval lever OFF → still holds back:
        // the vote filter alone is SUFFICIENT for the flip.
        var voteOnly = DeterminizedSearch.Run(
            BuildMcts(wOff), BuildHoldBackRaceRoot(BaseSeed),
            totalBudgetMs: 1600, perWorldBudgetMs: 400);
        voteOnly.Key.Should().Be(HoldBackKey,
            "the two-tier vote demotes the race on its MinWorldMean terminal death alone — "
            + "no HiddenReach contribution is needed");

        // Eval lever ON, vote filter OFF (−∞) → still races: the eval lever
        // alone is NOT sufficient (every sampled Bolt is cast before the leaf,
        // so the penalty never fires and the legacy vote sees +500 mean).
        var evalOnly = DeterminizedSearch.Run(
            BuildMcts(wOn), BuildHoldBackRaceRoot(BaseSeed),
            totalBudgetMs: 1600, perWorldBudgetMs: 400,
            catastropheThreshold: double.NegativeInfinity);
        evalOnly.Key.Should().Be(AllOutKey,
            "with the vote filter disabled the leaf-eval lever cannot rescue the decision "
            + "on this board: the burn is spent in-sim before any leaf is evaluated, so "
            + "HiddenReach reads reach 0 everywhere and the legacy order races");
    }
}
