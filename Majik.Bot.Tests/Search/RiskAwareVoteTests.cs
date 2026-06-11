using FluentAssertions;
using Majik.Bot.Search;
using Xunit;

namespace Majik.Bot.Tests.Search;

/// <summary>
/// Tests for the risk-aware two-tier vote in <see cref="DeterminizedSearch"/>:
/// moves whose worst per-world mean (<c>KeyTally.MinWorldMean</c>) falls at or
/// below the catastrophe threshold are demoted below safe moves. Within each
/// tier the legacy order holds (visits → mean → ordinal key); when ALL moves
/// are catastrophic the vote degrades to the legacy single-tier behavior
/// (deliberately NOT maximin — the bot still races when every line dies
/// somewhere). Tallies are built synthetically via the internal
/// <c>KeyTally</c>/<c>Accumulate</c>/<c>Vote</c> surface.
/// </summary>
public class RiskAwareVoteTests
{
    private static SimMove Move(string key) => SimMove.ForTest(key);

    private static Dictionary<string, DeterminizedSearch.KeyTally> Tally() => new();

    /// <summary>One world's worth of stats folded into <paramref name="tally"/>.</summary>
    private static void World(Dictionary<string, DeterminizedSearch.KeyTally> tally, params RootStat[] stats) =>
        DeterminizedSearch.Accumulate(tally, stats);

    // ── Two-tier demotion ────────────────────────────────────────────────────

    [Fact]
    public void Vote_SafeLowVisitMove_BeatsCatastrophicHighVisitMove()
    {
        var risky = Move("risky");
        var safe = Move("safe");

        var tally = Tally();
        // risky: more visits, wins big in world 1 (mean +800) but DIES in world 2
        // (mean -800 → catastrophic at threshold -500). safe: fewer visits, modest
        // positive mean (+50) in both worlds.
        World(tally, new RootStat(risky, Visits: 50, TotalValue: 40_000),
                     new RootStat(safe, Visits: 30, TotalValue: 1_500));
        World(tally, new RootStat(risky, Visits: 50, TotalValue: -40_000),
                     new RootStat(safe, Visits: 30, TotalValue: 1_500));

        tally["risky"].Visits.Should().Be(100);
        tally["safe"].Visits.Should().Be(60);
        tally["risky"].MinWorldMean.Should().Be(-800);
        tally["safe"].MinWorldMean.Should().Be(50);

        var winner = DeterminizedSearch.Vote(tally, risky, catastropheThreshold: -500);

        winner.Key.Should().Be("safe",
            "a move that is catastrophic in any sampled world must rank below every safe move");
    }

    [Fact]
    public void Vote_AllMovesCatastrophic_FallsBackToLegacyVisitOrder()
    {
        var a = Move("a");
        var b = Move("b");

        var tally = Tally();
        World(tally, new RootStat(a, Visits: 100, TotalValue: -90_000),  // mean -900
                     new RootStat(b, Visits: 60, TotalValue: -42_000)); // mean -700

        var winner = DeterminizedSearch.Vote(tally, b, catastropheThreshold: -500);

        winner.Key.Should().Be("a",
            "when every move is catastrophic the vote degrades to the legacy order (most visits) — the bot still races");
    }

    [Fact]
    public void Vote_NegativeInfinityThreshold_DisablesTheFilter()
    {
        var risky = Move("risky");
        var safe = Move("safe");

        var tally = Tally();
        World(tally, new RootStat(risky, Visits: 100, TotalValue: -80_000), // mean -800
                     new RootStat(safe, Visits: 60, TotalValue: 3_000));    // mean +50

        var winner = DeterminizedSearch.Vote(tally, safe, catastropheThreshold: double.NegativeInfinity);

        winner.Key.Should().Be("risky",
            "no finite mean is <= -Infinity, so every move is safe and pure visit order decides");
    }

    // ── MinWorldMean accumulation ────────────────────────────────────────────

    [Fact]
    public void Accumulate_UnvisitedStat_DoesNotCountAsCatastrophic()
    {
        var move = Move("unvisited");

        var tally = Tally();
        World(tally, new RootStat(move, Visits: 0, TotalValue: 0));

        tally["unvisited"].MinWorldMean.Should().Be(double.PositiveInfinity,
            "a world that never visited the move observed nothing — that is not evidence of catastrophe");
    }

    [Fact]
    public void Accumulate_MinWorldMean_FoldsAcrossWorlds()
    {
        var move = Move("m");

        var tally = Tally();
        World(tally, new RootStat(move, Visits: 10, TotalValue: 1_000));  // world 1 mean +100
        World(tally, new RootStat(move, Visits: 20, TotalValue: -14_000)); // world 2 mean -700

        tally["m"].MinWorldMean.Should().Be(-700);
        tally["m"].Visits.Should().Be(30);
        tally["m"].TotalValue.Should().Be(-13_000);
    }

    [Fact]
    public void Accumulate_RepeatedKeyWithinOneWorld_UsesTheCombinedPerWorldMean()
    {
        // Two copies of the same card in hand enumerate two CastSpell actions whose
        // SimMove.Keys collide (name-based) — so one world's RootStats CAN repeat a
        // Key. The per-world mean must be computed over the Key's combined visits
        // in that world, not per-stat.
        var move = Move("Cast:Lightning Bolt");

        var tally = Tally();
        // Same world: (10, -9000) mean -900 alone, (10, +7000) mean +700 alone;
        // combined world mean = -2000 / 20 = -100.
        World(tally, new RootStat(move, Visits: 10, TotalValue: -9_000),
                     new RootStat(move, Visits: 10, TotalValue: 7_000));

        tally["Cast:Lightning Bolt"].MinWorldMean.Should().Be(-100,
            "a Key repeating within one world is ONE move in that world — its world mean is the combined mean");
    }
}
