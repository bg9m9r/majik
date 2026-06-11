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

    // ── Fail fast on a bad knob ────────────────────────────────────────────────

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
