using System.Text.Json;
using FluentAssertions;
using Majik.Bot.Probes;
using Xunit;

namespace Majik.Bot.Tests.Probes;

/// <summary>
/// Probe library contract: a 2-game micro head runs end-to-end and is
/// deterministic for a fixed seed block; the FB1 ladder panel has the pinned
/// 13-cell composition; the JSON results schema is pinned.
/// </summary>
public class ProbeLibraryTests
{
    /// <summary>Heuristic-vs-FB1 Burn mirror micro head: cheap (no search)
    /// and exercises the full game loop. Seed block far outside every real
    /// family block.</summary>
    private static ProbeHead MicroHead(int seedBlock = 990_000) => new(
        Name: "micro-burn-mirror",
        DeckA: "Burn",
        DeckB: "Burn",
        StrategyA: seed => new Majik.Bot.BotConfig("Burn", Strategy: "heuristic", RandomSeed: seed),
        StrategyB: seed => new Majik.Bot.BotConfig("Burn", Strategy: "frozen-fb1", RandomSeed: seed),
        SeedBlock: seedBlock,
        Games: 2,
        MaxTurns: 30);

    [Fact]
    public async Task MicroHead_RunsTwoGames_WithFiniteWinRate_AndPerGameSeeds()
    {
        var lines = new List<string>();

        var result = await ProbeRunner.RunAsync(MicroHead(), lines.Add);

        result.GamesPlayed.Should().Be(2);
        result.Games.Should().HaveCount(2);
        result.Games.Select(g => g.Seed).Should().Equal(990_000, 990_001);
        result.Games.Select(g => g.AIsAlice).Should().Equal(true, false);
        (result.AWins + result.BWins + result.Draws + result.Inconclusive).Should().Be(2);
        result.WinRate.Should().BeInRange(0.0, 1.0);
        double.IsFinite(result.WinRate).Should().BeTrue();

        // Per-game progress lines carry the seed.
        lines.Where(l => l.Contains("seed=")).Should().HaveCountGreaterThanOrEqualTo(2);
        lines.Should().Contain(l => l.Contains("seed=990000"));
        lines.Should().Contain(l => l.Contains("seed=990001"));
    }

    [Fact]
    public async Task MicroHead_SameSeedBlock_IsDeterministic()
    {
        var first  = await ProbeRunner.RunAsync(MicroHead());
        var second = await ProbeRunner.RunAsync(MicroHead());

        second.Games.Select(g => g.Outcome).Should().Equal(first.Games.Select(g => g.Outcome));
        second.AWins.Should().Be(first.AWins);
        second.Draws.Should().Be(first.Draws);
        second.Inconclusive.Should().Be(first.Inconclusive);
    }

    [Fact]
    public void LadderPanel_FB1_Has13Cells_5Mirrors_8Asym_2Canaries()
    {
        var panel = LadderPanel.FB1;

        panel.Should().HaveCount(13);
        panel.Where(h => h.Name.StartsWith("mirror-")).Should().HaveCount(5);
        panel.Where(h => h.Name.StartsWith("asym-")).Should().HaveCount(8);

        // Mirrors play the same deck both seats, one per panel archetype.
        panel.Where(h => h.Name.StartsWith("mirror-"))
            .Should().OnlyContain(h => h.DeckA == h.DeckB);
        panel.Where(h => h.Name.StartsWith("mirror-")).Select(h => h.DeckA)
            .Should().BeEquivalentTo(LadderPanel.Archetypes);

        // The Prowess/Burn pair is the canary (both seat assignments).
        panel.Where(h => h.Canary).Select(h => h.Name)
            .Should().BeEquivalentTo(new[] { "asym-prowess-vs-burn", "asym-burn-vs-prowess" });
    }

    [Fact]
    public void LadderPanel_SeedBlocks_AreDistinct_AndCollideWithNoHarnessFamily()
    {
        var blocks = LadderPanel.FB1.Select(h => h.SeedBlock).ToList();

        blocks.Should().OnlyHaveUniqueItems();
        blocks.Should().Equal(Enumerable.Range(0, 13).Select(i => 90_000 + 1000 * i));

        // The xUnit ProbeHarness family blocks (existing instrument constants).
        var harnessBlocks = new[] { 5000, 7000, 20000, 30000, 40000, 50000, 60000, 70000, 80000 };
        // Each family spans block..block+999 (+1000 per head inside a family);
        // the panel starts at 90000 — strictly above every existing family.
        blocks.Min().Should().BeGreaterThan(harnessBlocks.Max() + 1000);
    }

    [Fact]
    public void LadderPanel_SeatConfigs_AreLiveShape_Versus_FrozenFb1()
    {
        foreach (var head in LadderPanel.FB1)
        {
            var a = head.StrategyA(123);
            a.Strategy.Should().Be("mcts");
            a.ArchetypeName.Should().Be(head.DeckA);
            a.MaxMctsIterations.Should().Be(800);
            a.MaxMctsBudgetMs.Should().Be(1500);
            a.TreeStateReuse.Should().BeTrue();
            a.InferOpponentArchetype.Should().BeTrue();
            a.OpponentArchetype.Should().BeNull();
            a.RandomSeed.Should().Be(123);

            var b = head.StrategyB(456);
            b.Strategy.Should().Be("frozen-fb1");
            b.ArchetypeName.Should().Be(head.DeckB);
            b.RandomSeed.Should().Be(456);
        }
    }

    [Fact]
    public void PanelResult_HeadlineMean_ExcludesCanaries()
    {
        static ProbeResult Cell(string name, int aWins, int bWins, bool canary) => new(
            HeadName: name, DeckA: "X", DeckB: "Y",
            SeatALabel: "mcts(live)", SeatBLabel: "frozen-fb1",
            Canary: canary, SeedBlock: 0, GamesPlayed: aWins + bWins,
            AWins: aWins, BWins: bWins, Draws: 0, Inconclusive: 0,
            Games: Array.Empty<ProbeGameRecord>());

        var panel = new PanelResult(
            new[]
            {
                Cell("c1", 8, 2, canary: false),  // 80%
                Cell("c2", 4, 6, canary: false),  // 40%
                Cell("canary", 0, 10, canary: true), // 0% — must not drag the mean
            },
            CommitHash: "abc1234",
            GeneratedUtc: DateTime.UtcNow);

        panel.HeadlineMeanWinRate.Should().BeApproximately(0.6, 1e-9);
    }

    [Fact]
    public void WriteJson_SchemaPinned()
    {
        var result = new ProbeResult(
            HeadName: "micro", DeckA: "Burn", DeckB: "Burn",
            SeatALabel: "mcts(live)", SeatBLabel: "frozen-fb1",
            Canary: false, SeedBlock: 990_000, GamesPlayed: 2,
            AWins: 1, BWins: 1, Draws: 0, Inconclusive: 0,
            Games: new[]
            {
                new ProbeGameRecord(0, 990_000, true,  ProbeOutcome.SeatA),
                new ProbeGameRecord(1, 990_001, false, ProbeOutcome.SeatB),
            },
            Iterations: 800, BudgetMs: 1500);
        var panel = new PanelResult(
            new[] { result }, CommitHash: "abc1234",
            GeneratedUtc: new DateTime(2026, 6, 12, 0, 0, 0, DateTimeKind.Utc));

        var path = Path.Combine(Path.GetTempPath(), $"probe-schema-{Guid.NewGuid():N}.json");
        try
        {
            ProbeResults.WriteJson(panel, path);

            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            var root = doc.RootElement;
            root.GetProperty("commitHash").GetString().Should().Be("abc1234");
            root.GetProperty("headlineMeanWinRate").GetDouble().Should().BeApproximately(0.5, 1e-9);
            root.TryGetProperty("generatedUtc", out _).Should().BeTrue();

            var cell = root.GetProperty("cells")[0];
            cell.GetProperty("head").GetString().Should().Be("micro");
            cell.GetProperty("deckA").GetString().Should().Be("Burn");
            cell.GetProperty("deckB").GetString().Should().Be("Burn");
            cell.GetProperty("canary").GetBoolean().Should().BeFalse();
            cell.GetProperty("seedBlock").GetInt32().Should().Be(990_000);
            cell.GetProperty("gamesPlayed").GetInt32().Should().Be(2);
            cell.GetProperty("aWins").GetInt32().Should().Be(1);
            cell.GetProperty("decided").GetInt32().Should().Be(2);
            cell.GetProperty("winRate").GetDouble().Should().BeApproximately(0.5, 1e-9);
            cell.GetProperty("iterations").GetInt32().Should().Be(800);
            cell.GetProperty("budgetMs").GetInt32().Should().Be(1500);

            var game = cell.GetProperty("games")[0];
            game.GetProperty("index").GetInt32().Should().Be(0);
            game.GetProperty("seed").GetInt32().Should().Be(990_000);
            game.GetProperty("aIsAlice").GetBoolean().Should().BeTrue();
            game.GetProperty("outcome").GetString().Should().Be("SeatA");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void WriteMarkdownSummary_HasHeadlineAndTable()
    {
        var panel = new PanelResult(
            new[]
            {
                new ProbeResult(
                    "mirror-burn", "Burn", "Burn", "mcts(live)", "frozen-fb1",
                    Canary: false, SeedBlock: 91_000, GamesPlayed: 2,
                    AWins: 2, BWins: 0, Draws: 0, Inconclusive: 0,
                    Games: Array.Empty<ProbeGameRecord>(),
                    Iterations: 800, BudgetMs: 1500),
            },
            CommitHash: "abc1234",
            GeneratedUtc: DateTime.UtcNow);

        var path = Path.Combine(Path.GetTempPath(), $"probe-md-{Guid.NewGuid():N}.md");
        try
        {
            ProbeResults.WriteMarkdownSummary(panel, path);
            var md = File.ReadAllText(path);
            md.Should().Contain("Headline mean win-rate");
            md.Should().Contain("mirror-burn");
            md.Should().Contain("abc1234");
            md.Should().Contain("| cell |");
        }
        finally
        {
            File.Delete(path);
        }
    }
}
