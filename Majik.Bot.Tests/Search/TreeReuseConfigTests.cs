using FluentAssertions;
using Majik.Bot.Search;
using Xunit;

namespace Majik.Bot.Tests.Search;

/// <summary>
/// Config threading for the <see cref="MctsConfig.TreeStateReuse"/> knob
/// (mirroring the RolloutDepth threading exactly):
/// <see cref="BotConfig.TreeStateReuse"/> (bool?, null = off — the
/// production-safe default) resolves in <see cref="SearchStrategy.ConfigFrom"/>
/// and is PRESERVED by <see cref="SearchStrategy.DeterminizedConfigFrom"/> so
/// every per-world determinized search inherits the configured reuse mode.
/// </summary>
public class TreeReuseConfigTests
{
    // ── BotConfig default ─────────────────────────────────────────────────────

    [Fact]
    public void BotConfig_TreeStateReuse_DefaultsNull()
    {
        new BotConfig("Burn").TreeStateReuse.Should().BeNull(
            "null = reuse off — existing callers are untouched");
    }

    // ── ConfigFrom: resolve + default ─────────────────────────────────────────

    [Fact]
    public void ConfigFrom_NullTreeStateReuse_IsOff()
    {
        var cfg = SearchStrategy.ConfigFrom(new BotConfig("Burn", Strategy: "mcts"));

        cfg.TreeStateReuse.Should().BeFalse(
            "null must resolve to today's root-replay loop — byte-identical default");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ConfigFrom_ThreadsTreeStateReuse(bool reuse)
    {
        SearchStrategy.ConfigFrom(new BotConfig("Burn", Strategy: "mcts", TreeStateReuse: reuse))
            .TreeStateReuse.Should().Be(reuse);
    }

    // ── Determinized per-world configs inherit the knob ───────────────────────

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void DeterminizedConfigFrom_PreservesTreeStateReuse(bool reuse)
    {
        var full = new MctsConfig(
            MaxIterations: 150,
            MaxMillis: 1500,
            DepthTurns: 1,
            ExplorationC: 1.41,
            TreeStateReuse: reuse);

        var perWorld = SearchStrategy.DeterminizedConfigFrom(full, perWorldBudgetMs: 400);

        perWorld.TreeStateReuse.Should().Be(reuse,
            "every per-world determinized search must INHERIT the configured reuse mode");
        perWorld.MaxMillis.Should().Be(400, "the budget split is unchanged");
        perWorld.DepthTurns.Should().Be(1);
    }
}
