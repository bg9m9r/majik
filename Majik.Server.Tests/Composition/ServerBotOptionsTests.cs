using FluentAssertions;
using Majik.Server.Composition;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Majik.Server.Tests.Composition;

/// <summary>
/// Locks the server-side bot-strategy configuration seam added for the live
/// MCTS flip (profiled in #2596 on the production 1-vCPU shape):
///
/// <list type="bullet">
///   <item>The CODE default stays <c>heuristic</c> — dev, tests, and the
///     durable-log rehydration replay (which re-computes bot decisions and
///     relies on their determinism) are unchanged unless prod opts in.</item>
///   <item><c>Bot:Strategy=mcts</c> (env <c>Bot__Strategy</c>, set in
///     render.yaml) flips the live bot to the search brain at the profiled
///     parameters: 150 iterations / 1500 ms budget — ~115–127 iterations
///     actually complete on 1 vCPU — with honest opponent-archetype
///     INFERENCE (no hidden-zone peek, <c>OpponentArchetype</c> never set
///     for a human opponent).</item>
///   <item>A bad strategy value fails FAST at registration, not at first
///     match creation.</item>
/// </list>
/// </summary>
public class ServerBotOptionsTests
{
    private static ServerGameFactory FactoryWith(ServerBotOptions? options) =>
        new(new Majik.Core.Api.GameRegistry(), botOptions: options);

    // ── Defaults: nothing changes unless prod opts in ─────────────────────────

    [Fact]
    public void DefaultOptions_BuildBotConfig_StaysHeuristic()
    {
        var factory = FactoryWith(null);

        var cfg = factory.BuildBotConfig("Burn", decisionSink: null);

        cfg.ArchetypeName.Should().Be("Burn");
        cfg.Strategy.Should().Be("heuristic",
            "the code default must keep dev / tests / rehydration replay on the deterministic heuristic");
        cfg.DecisionSink.Should().BeNull();
        cfg.SearchConcurrency.Should().BeNull(
            "heuristic decisions are microseconds — only mcts searches are gated");
    }

    // ── The flip: mcts at the profiled live parameters ────────────────────────

    [Fact]
    public void MctsOptions_BuildBotConfig_CarriesProfiledLiveParameters()
    {
        var factory = FactoryWith(new ServerBotOptions { Strategy = "mcts" });

        var cfg = factory.BuildBotConfig("Burn", decisionSink: null);

        cfg.Strategy.Should().Be("mcts");
        cfg.MaxMctsIterations.Should().Be(150,
            "the #2596-profiled iteration cap — the regime the strength gates were measured at");
        cfg.MaxMctsBudgetMs.Should().Be(1500,
            "the #2596-profiled wall-clock budget that fits ~120 iterations on the 1-vCPU prod box");
        cfg.PrioritySearchEnabled.Should().BeTrue("BotConfig default — priority windows are searched");
        cfg.InferOpponentArchetype.Should().BeTrue(
            "vs a human the bot must INFER the opponent's archetype from public cards");
        cfg.OpponentArchetype.Should().BeNull(
            "a human opponent's deck is never known — and setting it would also disable inference");
        cfg.SearchConcurrency.Should().Be(1,
            "live searches on the 1-vCPU prod box must QUEUE (full strength each) " +
            "instead of splitting the core");
    }

    [Fact]
    public void MctsOptions_BotPlayerAgent_Constructs()
    {
        // Proves the wired strategy string resolves to a real strategy
        // (BotPlayerAgent throws on unknown strategy names at construction).
        var factory = FactoryWith(new ServerBotOptions { Strategy = "mcts" });
        var cfg = factory.BuildBotConfig("Burn", decisionSink: null);

        var seat = new Majik.Core.Players.Player("Bob", 20);
        var act = () => new Majik.Bot.BotPlayerAgent(seat, cfg);

        act.Should().NotThrow();
    }

    // ── RolloutDepth (rollout-truncation knob) ─────────────────────────────────

    [Fact]
    public void DefaultOptions_RolloutDepth_IsFullTurnPlus_AndStaysNullUnderHeuristic()
    {
        new ServerBotOptions().RolloutDepth.Should().Be("FullTurnPlus",
            "the code default is today's full playout — zero behaviour change");

        var cfg = FactoryWith(null).BuildBotConfig("Burn", decisionSink: null);
        cfg.RolloutDepth.Should().BeNull(
            "the heuristic strategy never rolls out — only mcts threads the depth");
    }

    [Fact]
    public void MctsOptions_BuildBotConfig_CarriesRolloutDepth()
    {
        var factory = FactoryWith(new ServerBotOptions
        {
            Strategy = "mcts",
            RolloutDepth = "EndOfTurn",
        });

        factory.BuildBotConfig("Burn", decisionSink: null)
            .RolloutDepth.Should().Be("EndOfTurn",
                "the live flip of a probe-gate winner is config-only (Bot__RolloutDepth)");
    }

    [Fact]
    public void MctsOptions_WithRolloutDepth_BotPlayerAgent_Constructs()
    {
        // Proves the wired depth string parses downstream (SearchStrategy
        // fails fast at construction on an unknown RolloutDepth).
        var factory = FactoryWith(new ServerBotOptions
        {
            Strategy = "mcts",
            RolloutDepth = "LeafEval",
        });
        var cfg = factory.BuildBotConfig("Burn", decisionSink: null);

        var seat = new Majik.Core.Players.Player("Bob", 20);
        var act = () => new Majik.Bot.BotPlayerAgent(seat, cfg);

        act.Should().NotThrow();
    }

    // ── TreeStateReuse (tree-state reuse knob) ─────────────────────────────────

    [Fact]
    public void DefaultOptions_TreeStateReuse_IsFalse_AndStaysNullUnderHeuristic()
    {
        new ServerBotOptions().TreeStateReuse.Should().BeFalse(
            "the code default is today's root-replay UCT loop — zero behaviour change");

        var cfg = FactoryWith(null).BuildBotConfig("Burn", decisionSink: null);
        cfg.TreeStateReuse.Should().BeNull(
            "the heuristic strategy never searches — only mcts threads the knob");
    }

    [Fact]
    public void MctsOptions_BuildBotConfig_CarriesTreeStateReuse()
    {
        var factory = FactoryWith(new ServerBotOptions
        {
            Strategy = "mcts",
            TreeStateReuse = true,
        });

        factory.BuildBotConfig("Burn", decisionSink: null)
            .TreeStateReuse.Should().BeTrue(
                "the live flip of the reuse-gate winner is config-only (Bot__TreeStateReuse)");
    }

    [Fact]
    public void MctsOptions_WithTreeStateReuse_BotPlayerAgent_Constructs()
    {
        // Proves the wired knob resolves downstream (SearchStrategy threads it
        // into the MctsConfig at construction).
        var factory = FactoryWith(new ServerBotOptions
        {
            Strategy = "mcts",
            TreeStateReuse = true,
        });
        var cfg = factory.BuildBotConfig("Burn", decisionSink: null);

        var seat = new Majik.Core.Players.Player("Bob", 20);
        var act = () => new Majik.Bot.BotPlayerAgent(seat, cfg);

        act.Should().NotThrow();
    }

    // ── RootBlockSearch (root-level block search kill switch) ──────────────────

    [Fact]
    public void DefaultOptions_RootBlockSearch_IsTrue_AndStaysNullUnderHeuristic()
    {
        new ServerBotOptions().RootBlockSearch.Should().BeTrue(
            "root block search ships ON — Bot__RootBlockSearch=false is the kill switch");

        var cfg = FactoryWith(null).BuildBotConfig("Burn", decisionSink: null);
        cfg.RootBlockSearch.Should().BeNull(
            "the heuristic strategy never searches blocks — only mcts threads the knob");
    }

    [Fact]
    public void MctsOptions_BuildBotConfig_CarriesRootBlockSearch()
    {
        var factory = FactoryWith(new ServerBotOptions
        {
            Strategy = "mcts",
            RootBlockSearch = false,
        });

        factory.BuildBotConfig("Burn", decisionSink: null)
            .RootBlockSearch.Should().BeFalse(
                "the kill switch is config-only (Bot__RootBlockSearch=false pins the legacy eval path)");
    }

    [Fact]
    public void MctsOptions_WithRootBlockSearchDisabled_BotPlayerAgent_Constructs()
    {
        // Proves the wired knob resolves downstream (SearchStrategy threads it
        // at construction).
        var factory = FactoryWith(new ServerBotOptions
        {
            Strategy = "mcts",
            RootBlockSearch = false,
        });
        var cfg = factory.BuildBotConfig("Burn", decisionSink: null);

        var seat = new Majik.Core.Players.Player("Bob", 20);
        var act = () => new Majik.Bot.BotPlayerAgent(seat, cfg);

        act.Should().NotThrow();
    }

    // ── MaxWorlds / PerWorldBudgetMs (determinized world-split knobs) ──────────

    [Fact]
    public void DefaultOptions_WorldSplitKnobs_AreNull_AndStayNullUnderHeuristic()
    {
        var options = new ServerBotOptions();
        options.MaxWorlds.Should().BeNull(
            "null = the engine default (kMax 8) — zero behaviour change");
        options.PerWorldBudgetMs.Should().BeNull(
            "null = the engine default (400 ms per world → K=4 at the live 1500 ms) — " +
            "zero behaviour change");

        var cfg = FactoryWith(null).BuildBotConfig("Burn", decisionSink: null);
        cfg.MaxWorlds.Should().BeNull(
            "the heuristic strategy never determinizes — only mcts threads the knob");
        cfg.PerWorldBudgetMs.Should().BeNull(
            "the heuristic strategy never determinizes — only mcts threads the knob");
    }

    [Fact]
    public void MctsOptions_BuildBotConfig_CarriesWorldSplitKnobs()
    {
        var factory = FactoryWith(new ServerBotOptions
        {
            Strategy = "mcts",
            MaxWorlds = 8,
            PerWorldBudgetMs = 200,
        });

        var cfg = factory.BuildBotConfig("Burn", decisionSink: null);

        cfg.MaxWorlds.Should().Be(8,
            "the live flip of a K-tuning probe winner is config-only (Bot__MaxWorlds)");
        cfg.PerWorldBudgetMs.Should().Be(200,
            "the live flip of a K-tuning probe winner is config-only (Bot__PerWorldBudgetMs)");
    }

    [Fact]
    public void HeuristicOptions_WorldSplitKnobs_StayNullEvenWhenSet()
    {
        // mcts-only: a heuristic deployment with stray world-split env vars must
        // not thread them (mirrors the SearchConcurrency / RolloutDepth /
        // TreeStateReuse mcts-only rule).
        var factory = FactoryWith(new ServerBotOptions
        {
            Strategy = "heuristic",
            MaxWorlds = 8,
            PerWorldBudgetMs = 200,
        });

        var cfg = factory.BuildBotConfig("Burn", decisionSink: null);

        cfg.MaxWorlds.Should().BeNull();
        cfg.PerWorldBudgetMs.Should().BeNull();
    }

    [Fact]
    public void MctsOptions_WithWorldSplitKnobs_BotPlayerAgent_Constructs()
    {
        // Proves the wired knobs resolve downstream (SearchStrategy threads them
        // into the determinized split at construction).
        var factory = FactoryWith(new ServerBotOptions
        {
            Strategy = "mcts",
            MaxWorlds = 8,
            PerWorldBudgetMs = 200,
        });
        var cfg = factory.BuildBotConfig("Burn", decisionSink: null);

        var seat = new Majik.Core.Players.Player("Bob", 20);
        var act = () => new Majik.Bot.BotPlayerAgent(seat, cfg);

        act.Should().NotThrow();
    }

    // ── Fail fast on a bad knob ────────────────────────────────────────────────

    [Theory]
    [InlineData("warpspeed")]
    [InlineData("")]
    public void UnknownRolloutDepth_Throws(string bad)
    {
        var act = () => FactoryWith(new ServerBotOptions { RolloutDepth = bad });

        act.Should().Throw<ArgumentException>().WithMessage($"*'{bad}'*",
            "a typo'd Bot__RolloutDepth must fail at registration and NAME the bad value");
    }

    [Fact]
    public void UnknownStrategy_Throws()
    {
        var act = () => FactoryWith(new ServerBotOptions { Strategy = "minimax" });

        act.Should().Throw<ArgumentException>().WithMessage("*minimax*");
    }

    [Theory]
    [InlineData(0, 1500)]
    [InlineData(150, 0)]
    [InlineData(-1, 1500)]
    public void NonPositiveBudgets_Throw(int iterations, int budgetMs)
    {
        var act = () => FactoryWith(new ServerBotOptions
        {
            Strategy = "mcts",
            MaxMctsIterations = iterations,
            MaxMctsBudgetMs = budgetMs,
        });

        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void NonPositiveMaxWorlds_Throws(int maxWorlds)
    {
        var act = () => FactoryWith(new ServerBotOptions
        {
            Strategy = "mcts",
            MaxWorlds = maxWorlds,
        });

        act.Should().Throw<ArgumentException>().WithMessage("*MaxWorlds*",
            "a nonsensical Bot__MaxWorlds must fail at registration and NAME the knob");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void NonPositivePerWorldBudgetMs_Throws(int perWorldBudgetMs)
    {
        var act = () => FactoryWith(new ServerBotOptions
        {
            Strategy = "mcts",
            PerWorldBudgetMs = perWorldBudgetMs,
        });

        act.Should().Throw<ArgumentException>().WithMessage("*PerWorldBudgetMs*",
            "a nonsensical Bot__PerWorldBudgetMs must fail at registration and NAME the knob");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void NonPositiveSearchConcurrency_Throws(int searchConcurrency)
    {
        var act = () => FactoryWith(new ServerBotOptions
        {
            Strategy = "mcts",
            SearchConcurrency = searchConcurrency,
        });

        act.Should().Throw<ArgumentException>().WithMessage("*SearchConcurrency*");
    }

    // ── DI binding: env vars reach the installed bot ──────────────────────────

    private static ServerGameFactory ResolveFactory(params (string Key, string Value)[] config)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(config.ToDictionary(kv => kv.Key, kv => (string?)kv.Value))
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMajikEngine(configuration);
        return services.BuildServiceProvider().GetRequiredService<ServerGameFactory>();
    }

    [Fact]
    public void AddMajikEngine_NoBotSection_DefaultsToHeuristic()
    {
        var factory = ResolveFactory();

        factory.BuildBotConfig("Burn", decisionSink: null)
            .Strategy.Should().Be("heuristic");
    }

    [Fact]
    public void AddMajikEngine_BotSection_BindsStrategyAndBudgets()
    {
        var factory = ResolveFactory(
            ("Bot:Strategy", "mcts"),
            ("Bot:MaxMctsIterations", "100"),
            ("Bot:MaxMctsBudgetMs", "900"),
            ("Bot:InferOpponentArchetype", "false"));

        var cfg = factory.BuildBotConfig("Burn", decisionSink: null);

        cfg.Strategy.Should().Be("mcts");
        cfg.MaxMctsIterations.Should().Be(100);
        cfg.MaxMctsBudgetMs.Should().Be(900);
        cfg.InferOpponentArchetype.Should().BeFalse();
    }

    [Fact]
    public void AddMajikEngine_BotSection_BindsSearchConcurrency()
    {
        var factory = ResolveFactory(
            ("Bot:Strategy", "mcts"),
            ("Bot:SearchConcurrency", "2"));

        factory.BuildBotConfig("Burn", decisionSink: null)
            .SearchConcurrency.Should().Be(2,
                "env Bot__SearchConcurrency must reach the installed bot");
    }

    [Fact]
    public void AddMajikEngine_BotSection_BindsRolloutDepth()
    {
        var factory = ResolveFactory(
            ("Bot:Strategy", "mcts"),
            ("Bot:RolloutDepth", "EndOfTurn"));

        factory.BuildBotConfig("Burn", decisionSink: null)
            .RolloutDepth.Should().Be("EndOfTurn",
                "env Bot__RolloutDepth must reach the installed bot");
    }

    [Fact]
    public void AddMajikEngine_BotSection_BindsTreeStateReuse()
    {
        var factory = ResolveFactory(
            ("Bot:Strategy", "mcts"),
            ("Bot:TreeStateReuse", "true"));

        factory.BuildBotConfig("Burn", decisionSink: null)
            .TreeStateReuse.Should().BeTrue(
                "env Bot__TreeStateReuse must reach the installed bot");
    }

    [Fact]
    public void AddMajikEngine_BotSection_BindsRootBlockSearch()
    {
        var factory = ResolveFactory(
            ("Bot:Strategy", "mcts"),
            ("Bot:RootBlockSearch", "false"));

        factory.BuildBotConfig("Burn", decisionSink: null)
            .RootBlockSearch.Should().BeFalse(
                "env Bot__RootBlockSearch must reach the installed bot");
    }

    [Fact]
    public void AddMajikEngine_BotSection_BindsWorldSplitKnobs()
    {
        var factory = ResolveFactory(
            ("Bot:Strategy", "mcts"),
            ("Bot:MaxWorlds", "8"),
            ("Bot:PerWorldBudgetMs", "200"));

        var cfg = factory.BuildBotConfig("Burn", decisionSink: null);

        cfg.MaxWorlds.Should().Be(8,
            "env Bot__MaxWorlds must reach the installed bot");
        cfg.PerWorldBudgetMs.Should().Be(200,
            "env Bot__PerWorldBudgetMs must reach the installed bot");
    }

    [Fact]
    public void AddMajikEngine_NonPositivePerWorldBudgetMs_FailsFastAtRegistration()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Bot:Strategy"] = "mcts",
                ["Bot:PerWorldBudgetMs"] = "0",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        var act = () => services.AddMajikEngine(configuration);

        act.Should().Throw<ArgumentException>(
            "a zero Bot__PerWorldBudgetMs env var must crash the boot, not the first vs-bot match");
    }

    [Fact]
    public void AddMajikEngine_NonBooleanTreeStateReuse_FailsFastAtRegistration()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Bot:TreeStateReuse"] = "wat" })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        var act = () => services.AddMajikEngine(configuration);

        act.Should().Throw<InvalidOperationException>(
            "a typo'd Bot__TreeStateReuse env var must crash the boot (config-binder " +
            "conversion failure), not the first vs-bot match");
    }

    [Fact]
    public void AddMajikEngine_UnknownRolloutDepth_FailsFastAtRegistration()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Bot:RolloutDepth"] = "wat" })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        var act = () => services.AddMajikEngine(configuration);

        act.Should().Throw<ArgumentException>(
            "a typo'd Bot__RolloutDepth env var must crash the boot, not the first vs-bot match");
    }

    [Fact]
    public void AddMajikEngine_ProdShape_MctsOnly_KeepsProfiledDefaults()
    {
        // The exact prod shape: render.yaml sets ONLY Bot__Strategy=mcts and
        // the profiled 150it/1500ms + inference defaults ride along from code.
        var factory = ResolveFactory(("Bot:Strategy", "mcts"));

        var cfg = factory.BuildBotConfig("Burn", decisionSink: null);

        cfg.Strategy.Should().Be("mcts");
        cfg.MaxMctsIterations.Should().Be(150);
        cfg.MaxMctsBudgetMs.Should().Be(1500);
        cfg.InferOpponentArchetype.Should().BeTrue();
        cfg.SearchConcurrency.Should().Be(1,
            "the gate defaults ON (1 search at a time) when prod flips to mcts — " +
            "no extra env var needed");
        cfg.RolloutDepth.Should().Be("FullTurnPlus",
            "the rollout-depth default is today's full playout — the probe gate " +
            "flips it later via Bot__RolloutDepth only");
        cfg.TreeStateReuse.Should().BeFalse(
            "the tree-reuse default is today's root-replay loop — the probe gate " +
            "flips it later via Bot__TreeStateReuse only");
        cfg.RootBlockSearch.Should().BeTrue(
            "root block search ships ON under mcts — Bot__RootBlockSearch=false " +
            "is the kill switch back to the BlockCombatEval path");
        cfg.MaxWorlds.Should().BeNull(
            "the world-split defaults are today's 400 ms / kMax 8 — the probe gate " +
            "flips them later via Bot__MaxWorlds / Bot__PerWorldBudgetMs only");
        cfg.PerWorldBudgetMs.Should().BeNull(
            "the world-split defaults are today's 400 ms / kMax 8 — the probe gate " +
            "flips them later via Bot__MaxWorlds / Bot__PerWorldBudgetMs only");
    }

    [Fact]
    public void AddMajikEngine_UnknownStrategy_FailsFastAtRegistration()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Bot:Strategy"] = "wat" })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        var act = () => services.AddMajikEngine(configuration);

        act.Should().Throw<ArgumentException>(
            "a typo'd Bot__Strategy env var must crash the boot, not 500 the first vs-bot match");
    }
}
