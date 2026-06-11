using System.Diagnostics;
using FluentAssertions;
using Majik.Bot.Search;
using Majik.Core.Cards;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Bot.Tests.Search;

/// <summary>
/// Locks the process-wide search concurrency gate added for the live MCTS flip
/// (1-vCPU prod): overlapping LIVE searches QUEUE instead of contending for the
/// single core, so every search runs at full strength.
///
/// <list type="bullet">
///   <item><b>Opt-in only:</b> <see cref="BotConfig.SearchConcurrency"/> null
///     (the default) installs NO gate — unit tests, the parallel strength
///     probes, and sim-internal searches are unaffected.</item>
///   <item><b>Serialization:</b> with concurrency 1, two concurrent Picks on
///     different strategy instances sharing a gate never overlap inside the
///     search (max observed in-flight == 1).</item>
///   <item><b>Starvation guard:</b> a gate held past the bounded wait makes the
///     Pick FALL BACK to the heuristic decision (valid plan, no throw, no
///     indefinite stall).</item>
/// </list>
/// </summary>
public class SearchConcurrencyGateTests
{
    /// <summary>
    /// Board position mirroring SearchStrategyTests: Alice has two ready 2/2
    /// bears; Bob is at 3 life with no blockers. Any sane decision (search or
    /// heuristic fallback) attacks. Fresh objects per call so concurrent picks
    /// never share mutable state.
    /// </summary>
    private static (GameContext Ctx, Player Self, List<Creature> Eligible) AttackScenario()
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

        // Pad libraries so the sandbox engine does not draw-lose immediately.
        foreach (var _ in Enumerable.Range(0, 15))
        {
            var f = new Land("Forest");
            f.ChangeOwner(alice);
            alice.Zones.GetZone(ZoneType.Library).AddCard(f);
            var g = new Land("Forest");
            g.ChangeOwner(bob);
            bob.Zones.GetZone(ZoneType.Library).AddCard(g);
        }

        return (SearchTestCtx.AtCombat(alice, bob), alice, bears);
    }

    /// <summary>Small bounded search so gate tests finish quickly.</summary>
    private static BotConfig MctsConfig(int? searchConcurrency = null) => new(
        "Burn",
        Strategy: "mcts",
        MaxMctsIterations: 30,
        MaxMctsBudgetMs: 800,
        SearchConcurrency: searchConcurrency);

    // ── Opt-in only: null = no gate, today's behavior ─────────────────────────

    [Fact]
    public async Task NullSearchConcurrency_InstallsNoGate_AndConcurrentPicksComplete()
    {
        var strat1 = new SearchStrategy(MctsConfig(searchConcurrency: null));
        var strat2 = new SearchStrategy(MctsConfig(searchConcurrency: null));

        strat1.Gate.Should().BeNull(
            "null SearchConcurrency must keep the ungated fast path — unit tests and " +
            "the parallel strength probes must never serialize");
        strat2.Gate.Should().BeNull();

        // Two ungated picks on different threads both complete with live plans.
        var (ctx1, self1, eligible1) = AttackScenario();
        var (ctx2, self2, eligible2) = AttackScenario();

        var t1 = Task.Run(() => strat1.PickAttackers(ctx1, self1, eligible1));
        var t2 = Task.Run(() => strat2.PickAttackers(ctx2, self2, eligible2));
        var plans = await Task.WhenAll(t1, t2);

        plans[0].Attackers.Should().OnlyContain(a => eligible1.Contains(a.Attacker));
        plans[1].Attackers.Should().OnlyContain(a => eligible2.Contains(a.Attacker));
    }

    // ── Shared static gate semantics ──────────────────────────────────────────

    [Fact]
    public void SearchConcurrencyConfigured_StrategiesShareTheProcessWideGate()
    {
        var strat1 = new SearchStrategy(MctsConfig(searchConcurrency: 1));
        var strat2 = new SearchStrategy(MctsConfig(searchConcurrency: 1));

        strat1.Gate.Should().NotBeNull();
        strat1.Gate.Should().BeSameAs(strat2.Gate,
            "the gate is PROCESS-WIDE: overlapping live searches from different " +
            "matches must queue on the same semaphore");
    }

    [Fact]
    public void SharedGate_FirstConfiguredValueWins()
    {
        var first = SearchConcurrencyGate.Shared(1);
        var second = SearchConcurrencyGate.Shared(2);

        second.Should().BeSameAs(first,
            "first-configured wins (documented) — a later disagreeing config logs and " +
            "reuses the existing gate rather than splitting searches across two semaphores");
        second.Permits.Should().Be(1);
    }

    // ── Serialization proof: concurrency 1 → max in-flight == 1 ──────────────

    [Fact]
    public async Task SearchConcurrencyOne_ConcurrentAttackPicks_Serialize()
    {
        // Isolated gate instance (not the process-wide one) so this test cannot
        // interfere with the shared-gate tests above.
        var gate = new SearchGate(permits: 1, timeout: TimeSpan.FromSeconds(30));
        var strat1 = new SearchStrategy(MctsConfig(), gate);
        var strat2 = new SearchStrategy(MctsConfig(), gate);

        var (ctx1, self1, eligible1) = AttackScenario();
        var (ctx2, self2, eligible2) = AttackScenario();

        var t1 = Task.Run(() => strat1.PickAttackers(ctx1, self1, eligible1));
        var t2 = Task.Run(() => strat2.PickAttackers(ctx2, self2, eligible2));
        var plans = await Task.WhenAll(t1, t2);

        gate.EnterCount.Should().Be(2, "both searched picks must route through the gate");
        gate.MaxObservedConcurrency.Should().Be(1,
            "with one permit the two searches must SERIALIZE — never two in flight");

        plans[0].Attackers.Should().OnlyContain(a => eligible1.Contains(a.Attacker));
        plans[1].Attackers.Should().OnlyContain(a => eligible2.Contains(a.Attacker));
    }

    [Fact]
    public void PriorityPick_AlsoRoutesThroughGate()
    {
        var gate = new SearchGate(permits: 1, timeout: TimeSpan.FromSeconds(30));
        var strat = new SearchStrategy(MctsConfig(), gate);

        // Land-in-hand main-phase scenario (mirrors PrioritySearchTests): two
        // legal actions (PlayLand / Pass) → the priority search actually runs.
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);
        var land = new Land("Forest");
        land.ChangeOwner(alice);
        alice.Zones.Hand.AddCard(land);
        foreach (var _ in Enumerable.Range(0, 15))
        {
            var f = new Land("Forest");
            f.ChangeOwner(alice);
            alice.Zones.GetZone(ZoneType.Library).AddCard(f);
            var g = new Land("Forest");
            g.ChangeOwner(bob);
            bob.Zones.GetZone(ZoneType.Library).AddCard(g);
        }
        var ctx = PrioritySearchTestCtx.AtMain(alice, bob);

        var action = strat.PickPriorityAction(ctx, alice);

        gate.EnterCount.Should().Be(1,
            "PickPriorityAction is the second live decision entry that runs the search " +
            "and must hold the same gate");
        action.Should().NotBeNull();
    }

    // ── Starvation guard: bounded wait → heuristic fallback ──────────────────

    [Fact]
    public void GateHeldBeyondTimeout_PickFallsBackToHeuristic_NoThrowNoStall()
    {
        var gate = new SearchGate(permits: 1, timeout: TimeSpan.FromMilliseconds(200));
        gate.TryEnter().Should().BeTrue("the test holds the only permit");
        try
        {
            var strat = new SearchStrategy(MctsConfig(), gate);
            var (ctx, self, eligible) = AttackScenario();

            var sw = Stopwatch.StartNew();
            CombatPlan plan = null!;
            var act = () => plan = strat.PickAttackers(ctx, self, eligible);

            act.Should().NotThrow("a starved search must degrade, never crash the match");
            sw.Stop();

            sw.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(10),
                "the bounded wait must prevent an indefinite stall");
            plan.Should().NotBeNull();
            plan.Attackers.Should().OnlyContain(a => eligible.Contains(a.Attacker),
                "the fallback heuristic decision must be a valid LIVE plan");
            gate.EnterCount.Should().Be(1,
                "only the test's own hold entered — the timed-out pick never ran the search");
        }
        finally
        {
            gate.Exit();
        }
    }
}
