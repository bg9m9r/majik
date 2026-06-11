using FluentAssertions;
using Majik.Bot.Search;
using Xunit;

namespace Majik.Bot.Tests.Search;

/// <summary>
/// Config threading for the <see cref="RolloutDepth"/> knob:
/// <see cref="BotConfig.RolloutDepth"/> (string?, null = FullTurnPlus — the
/// production-safe default) parses case-insensitively in
/// <see cref="SearchStrategy.ConfigFrom"/>, fails FAST on an unknown value
/// (mirroring the strategy-name fail-fast), and is PRESERVED by
/// <see cref="SearchStrategy.DeterminizedConfigFrom"/> so every per-world
/// determinized search inherits the configured depth.
/// </summary>
public class RolloutDepthConfigTests
{
    // ── BotConfig default ─────────────────────────────────────────────────────

    [Fact]
    public void BotConfig_RolloutDepth_DefaultsNull()
    {
        new BotConfig("Burn").RolloutDepth.Should().BeNull(
            "null = FullTurnPlus — existing callers are untouched");
    }

    // ── ConfigFrom: parse + default ───────────────────────────────────────────

    [Fact]
    public void ConfigFrom_NullRolloutDepth_IsFullTurnPlus()
    {
        var cfg = SearchStrategy.ConfigFrom(new BotConfig("Burn", Strategy: "mcts"));

        cfg.RolloutDepth.Should().Be(RolloutDepth.FullTurnPlus,
            "null must resolve to today's behaviour — byte-identical default");
        cfg.DepthTurns.Should().Be(1, "the Stage-A live playout cap is unchanged");
    }

    [Theory]
    [InlineData("LeafEval", RolloutDepth.LeafEval)]
    [InlineData("leafeval", RolloutDepth.LeafEval)]
    [InlineData("ENDOFTURN", RolloutDepth.EndOfTurn)]
    [InlineData("EndOfTurn", RolloutDepth.EndOfTurn)]
    [InlineData("FullTurnPlus", RolloutDepth.FullTurnPlus)]
    [InlineData("fullturnplus", RolloutDepth.FullTurnPlus)]
    public void ConfigFrom_ParsesRolloutDepth_CaseInsensitively(string value, RolloutDepth expected)
    {
        SearchStrategy.ConfigFrom(new BotConfig("Burn", Strategy: "mcts", RolloutDepth: value))
            .RolloutDepth.Should().Be(expected);
    }

    // ── Fail fast on a bad knob ───────────────────────────────────────────────

    [Theory]
    [InlineData("warpspeed")]
    [InlineData("1")] // numeric enum values are NOT names — reject, never silently map
    [InlineData("")]
    public void ConfigFrom_UnknownRolloutDepth_ThrowsNamingTheValue(string bad)
    {
        var act = () => SearchStrategy.ConfigFrom(
            new BotConfig("Burn", Strategy: "mcts", RolloutDepth: bad));

        act.Should().Throw<ArgumentException>().WithMessage($"*'{bad}'*",
            "the exception must NAME the bad value (mirror the strategy-name fail-fast)");
    }

    [Fact]
    public void SearchStrategy_Construction_FailsFast_OnUnknownRolloutDepth()
    {
        var act = () => new SearchStrategy(
            new BotConfig("Burn", Strategy: "mcts", RolloutDepth: "bogus"));

        act.Should().Throw<ArgumentException>().WithMessage("*bogus*",
            "a typo'd depth must fail at construction, not silently degrade");
    }

    // ── Determinized per-world configs inherit the depth ──────────────────────

    [Theory]
    [InlineData(RolloutDepth.LeafEval)]
    [InlineData(RolloutDepth.EndOfTurn)]
    [InlineData(RolloutDepth.FullTurnPlus)]
    public void DeterminizedConfigFrom_PreservesRolloutDepth(RolloutDepth depth)
    {
        var full = new MctsConfig(
            MaxIterations: 150,
            MaxMillis: 1500,
            DepthTurns: 1,
            ExplorationC: 1.41,
            RolloutDepth: depth);

        var perWorld = SearchStrategy.DeterminizedConfigFrom(full, perWorldBudgetMs: 400);

        perWorld.RolloutDepth.Should().Be(depth,
            "every per-world determinized search must INHERIT the configured depth");
        perWorld.DepthTurns.Should().Be(1);
        perWorld.MaxMillis.Should().Be(400, "the budget split is unchanged");
    }
}
