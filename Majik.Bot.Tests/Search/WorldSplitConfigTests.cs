using FluentAssertions;
using Majik.Bot.Search;
using Xunit;

namespace Majik.Bot.Tests.Search;

/// <summary>
/// Config threading for the determinized WORLD-SPLIT knobs (mirroring the
/// TreeStateReuse threading exactly): <see cref="BotConfig.PerWorldBudgetMs"/>
/// (int?, null = 400 — today's per-world budget) and
/// <see cref="BotConfig.MaxWorlds"/> (int?, null = 8 — today's kMax clamp)
/// resolve in the <see cref="SearchStrategy"/> constructor and govern the
/// K-world split: <c>K = clamp(round(total / perWorld), 1, MaxWorlds)</c>
/// (see <see cref="DeterminizedSearch.KFor"/>), with the per-world
/// <see cref="MctsConfig"/> (<see cref="SearchStrategy.DeterminizedConfigFrom"/>)
/// scaling MaxIterations by the SAME perWorld/total fraction.
///
/// <para>The live arithmetic these knobs unlock (total 1500 ms, iteration cap
/// 800): perWorld=400 (default) → K=4 × ~213 iters/world; perWorld=200 +
/// MaxWorlds=8 → K=8 × ~107 iters/world.</para>
/// </summary>
public class WorldSplitConfigTests
{
    // ── BotConfig defaults ────────────────────────────────────────────────────

    [Fact]
    public void BotConfig_PerWorldBudgetMs_DefaultsNull()
    {
        new BotConfig("Burn").PerWorldBudgetMs.Should().BeNull(
            "null = today's 400 ms per-world budget — existing callers are untouched");
    }

    [Fact]
    public void BotConfig_MaxWorlds_DefaultsNull()
    {
        new BotConfig("Burn").MaxWorlds.Should().BeNull(
            "null = today's kMax of 8 — existing callers are untouched");
    }

    // ── SearchStrategy resolution: null → today's constants ──────────────────

    [Fact]
    public void SearchStrategy_NullKnobs_ResolveToLiveDefaults()
    {
        var strategy = new SearchStrategy(new BotConfig("Burn", Strategy: "mcts"));

        strategy.PerWorldBudgetMsResolved.Should().Be(400,
            "null must resolve to today's 400 ms per-world budget — byte-identical default");
        strategy.KMaxResolved.Should().Be(8,
            "null must resolve to today's kMax of 8 — byte-identical default");
    }

    [Fact]
    public void SearchStrategy_ExplicitKnobs_Resolve()
    {
        var strategy = new SearchStrategy(new BotConfig(
            "Burn", Strategy: "mcts",
            MaxWorlds: 4,
            PerWorldBudgetMs: 200));

        strategy.PerWorldBudgetMsResolved.Should().Be(200);
        strategy.KMaxResolved.Should().Be(4);
    }

    // ── The per-world Mcts is bounded to the CONFIGURED per-world budget ─────

    [Fact]
    public void SearchStrategy_DeterminizedMcts_BoundedToConfiguredPerWorldBudget()
    {
        // Known archetype → the determinized Mcts is built; its per-world config
        // must carry the CONFIGURED split, not the old 400 ms const.
        var strategy = new SearchStrategy(new BotConfig(
            "Burn", Strategy: "mcts",
            MaxMctsIterations: 800,
            MaxMctsBudgetMs: 1500,
            OpponentArchetype: "Burn",
            MaxWorlds: 8,
            PerWorldBudgetMs: 200));

        var perWorld = strategy.DeterminizedMctsConfig;
        perWorld.Should().NotBeNull("a known OpponentArchetype builds the determinized Mcts");
        perWorld!.MaxMillis.Should().Be(200,
            "the per-world Mcts must be wall-clock-bounded to the configured per-world budget");
        perWorld.MaxIterations.Should().Be(107,
            "the iteration cap splits by the SAME perWorld/total fraction: " +
            "round(800 × 200 / 1500) = 107 per world");
    }

    [Fact]
    public void SearchStrategy_DefaultKnobs_DeterminizedMcts_KeepsTodaysSplit()
    {
        var strategy = new SearchStrategy(new BotConfig(
            "Burn", Strategy: "mcts",
            MaxMctsIterations: 800,
            MaxMctsBudgetMs: 1500,
            OpponentArchetype: "Burn"));

        var perWorld = strategy.DeterminizedMctsConfig;
        perWorld.Should().NotBeNull();
        perWorld!.MaxMillis.Should().Be(400, "defaults preserve today's 400 ms per-world bound");
        perWorld.MaxIterations.Should().Be(213,
            "round(800 × 400 / 1500) = 213 per world — today's arithmetic unchanged");
    }

    // ── KFor: the K the knobs produce at the live budget ─────────────────────

    [Theory]
    [InlineData(1500, 400, 8, 4)]   // today's live split: K=4
    [InlineData(1500, 200, 8, 8)]   // probe cell (a): round(7.5)=8, kMax 8 → K=8
    [InlineData(1500, 200, 4, 4)]   // MaxWorlds caps: round(7.5)=8 clamped to 4
    [InlineData(1500, 400, 4, 4)]   // probe cell (b): explicit K=4 control
    [InlineData(6000, 400, 8, 8)]   // the 6000 ms probe regime that motivated this knob
    public void KFor_ConfiguredSplit_YieldsExpectedWorldCount(
        int totalMs, int perWorldMs, int kMax, int expectedK)
    {
        DeterminizedSearch.KFor(totalMs, perWorldMs, kMax).Should().Be(expectedK);
    }

    // ── DeterminizedConfigFrom stays consistent with the knob ────────────────

    [Theory]
    [InlineData(400, 213)]   // today's split at cap 800 / 1500 ms
    [InlineData(200, 107)]   // the K=8 probe split at cap 800 / 1500 ms
    public void DeterminizedConfigFrom_ScalesIterationsByPerWorldFraction(
        int perWorldMs, int expectedIterations)
    {
        var full = new MctsConfig(
            MaxIterations: 800,
            MaxMillis: 1500,
            DepthTurns: 1,
            ExplorationC: 1.41,
            TreeStateReuse: true);

        var perWorld = SearchStrategy.DeterminizedConfigFrom(full, perWorldBudgetMs: perWorldMs);

        perWorld.MaxMillis.Should().Be(perWorldMs);
        perWorld.MaxIterations.Should().Be(expectedIterations,
            "the iteration split must use the SAME perWorld/total fraction as the time split");
        perWorld.TreeStateReuse.Should().BeTrue("reuse is preserved into every per-world search");
    }
}
