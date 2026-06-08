using Majik.Bot;
using Majik.Core.Cards;
using Majik.Core.Players;
using Majik.Core.Random;
using Majik.Core.Simulation;
using Majik.Core.StateMachine;
using System.Diagnostics;
using Xunit;
using Xunit.Abstractions;

namespace Majik.Bot.Tests.Search;

/// <summary>
/// DIAGNOSTIC SPIKE — Phase 2A Task 1.
///
/// Root-causes why sandbox games are slow / livelock when both seats use
/// HeuristicStrategy (the condition that forced Phase 1 to disable priority
/// search in MCTS rollouts).
///
/// Classification evidence lives in the [SPIKE] lines written to the test
/// output via ITestOutputHelper.
///
/// Tests must build and pass in an acceptable time (they have generous
/// timeouts). They are NOT asserting gameplay correctness — only timing
/// and loop-safety observations.
/// </summary>
public sealed class Spike_SandboxLivelockTests
{
    private readonly ITestOutputHelper _out;
    public Spike_SandboxLivelockTests(ITestOutputHelper o) => _out = o;

    // ── Variant A: empty hands, just lands ──────────────────────────────────

    /// <summary>
    /// Baseline: both players have only lands in hand (no spells to cast).
    /// Pure land-drop + combat + pass decisions. Establishes how fast the
    /// engine runs when the priority policy can only propose land drops.
    /// </summary>
    [Fact(Timeout = 120_000)]
    public async Task Spike_EmptyHands_JustLands_4Turns()
    {
        var (alice, bob) = BuildBoard(spelledHands: false);
        var sw = Stopwatch.StartNew();
        var sandbox = SandboxGame.From(
            new[] { alice, bob },
            new GameRandom(1),
            p => new BotPlayerAgent(p, new BotConfig("Burn", Strategy: "heuristic", SimCombatBudgetMs: 20)));
        var result = await sandbox.ResumeAsync(
            PhaseStateType.PreCombatMain,
            sandbox.State.PlayerFor(alice),
            turnNumber: 3,
            maxTurns: 4,
            ct: default);
        sw.Stop();
        _out.WriteLine($"[SPIKE] EmptyHands/4t total={sw.ElapsedMilliseconds}ms  result={result}");
    }

    [Fact(Timeout = 120_000)]
    public async Task Spike_EmptyHands_JustLands_10Turns()
    {
        var (alice, bob) = BuildBoard(spelledHands: false);
        var sw = Stopwatch.StartNew();
        var sandbox = SandboxGame.From(
            new[] { alice, bob },
            new GameRandom(1),
            p => new BotPlayerAgent(p, new BotConfig("Burn", Strategy: "heuristic", SimCombatBudgetMs: 20)));
        var result = await sandbox.ResumeAsync(
            PhaseStateType.PreCombatMain,
            sandbox.State.PlayerFor(alice),
            turnNumber: 3,
            maxTurns: 10,
            ct: default);
        sw.Stop();
        _out.WriteLine($"[SPIKE] EmptyHands/10t total={sw.ElapsedMilliseconds}ms  result={result}");
    }

    // ── Variant B: hands WITH spells ────────────────────────────────────────

    /// <summary>
    /// With spells in hand the bot will try to cast them. If the priority-loop
    /// safety fires it means a spell cast is being proposed repeatedly (the
    /// pathological-round hypothesis). Timing should reveal whether per-turn
    /// cost scales linearly or explosively compared to the land-only baseline.
    /// </summary>
    [Fact(Timeout = 120_000)]
    public async Task Spike_SpelledHands_4Turns()
    {
        var (alice, bob) = BuildBoard(spelledHands: true);
        var sw = Stopwatch.StartNew();
        var sandbox = SandboxGame.From(
            new[] { alice, bob },
            new GameRandom(1),
            p => new BotPlayerAgent(p, new BotConfig("Burn", Strategy: "heuristic", SimCombatBudgetMs: 20)));
        var result = await sandbox.ResumeAsync(
            PhaseStateType.PreCombatMain,
            sandbox.State.PlayerFor(alice),
            turnNumber: 3,
            maxTurns: 4,
            ct: default);
        sw.Stop();
        _out.WriteLine($"[SPIKE] SpelledHands/4t total={sw.ElapsedMilliseconds}ms  result={result}");
    }

    [Fact(Timeout = 120_000)]
    public async Task Spike_SpelledHands_10Turns()
    {
        var (alice, bob) = BuildBoard(spelledHands: true);
        var sw = Stopwatch.StartNew();
        var sandbox = SandboxGame.From(
            new[] { alice, bob },
            new GameRandom(1),
            p => new BotPlayerAgent(p, new BotConfig("Burn", Strategy: "heuristic", SimCombatBudgetMs: 20)));
        var result = await sandbox.ResumeAsync(
            PhaseStateType.PreCombatMain,
            sandbox.State.PlayerFor(alice),
            turnNumber: 3,
            maxTurns: 10,
            ct: default);
        sw.Stop();
        _out.WriteLine($"[SPIKE] SpelledHands/10t total={sw.ElapsedMilliseconds}ms  result={result}");
    }

    // ── Variant C: per-turn breakdown ────────────────────────────────────────

    /// <summary>
    /// Runs one sandbox game per maxTurns value (4..10) and records elapsed
    /// time. Plots whether time is linear in turns (slow but O(n)) or if one
    /// turn explodes (pathological loop).
    /// </summary>
    [Fact(Timeout = 120_000)]
    public async Task Spike_PerTurnBreakdown_SpelledHands()
    {
        _out.WriteLine("[SPIKE] Per-turn timing breakdown (SpelledHands):");
        long prev = 0;
        for (var cap = 4; cap <= 10; cap++)
        {
            var (alice, bob) = BuildBoard(spelledHands: true);
            var sw = Stopwatch.StartNew();
            var sandbox = SandboxGame.From(
                new[] { alice, bob },
                new GameRandom(1),
                p => new BotPlayerAgent(p, new BotConfig("Burn", Strategy: "heuristic", SimCombatBudgetMs: 20)));
            await sandbox.ResumeAsync(
                PhaseStateType.PreCombatMain,
                sandbox.State.PlayerFor(alice),
                turnNumber: 3,
                maxTurns: cap,
                ct: default);
            sw.Stop();
            var delta = sw.ElapsedMilliseconds - prev;
            _out.WriteLine($"  maxTurns={cap:D2}  total={sw.ElapsedMilliseconds}ms  delta=+{delta}ms");
            prev = sw.ElapsedMilliseconds;
        }
    }

    // ── Board builder ─────────────────────────────────────────────────────────

    /// <summary>
    /// Builds a 2-player board for the spike. Both players start with:
    /// - 4 untapped basic lands on the battlefield (enough to cast 2-drops)
    /// - 20-card basic-land library (prevent draw-loss)
    /// - 2 vanilla creatures on battlefield (summoning-sickness cleared)
    ///
    /// If <paramref name="spelledHands"/> is true, both players also have
    /// 3 plain vanilla creatures in hand (2/2 for {1}{G} — an affordable,
    /// fully implemented card shape) and 3 basic lands in hand.
    /// If false, hand is only 3 basic lands.
    ///
    /// We deliberately avoid:
    /// - Unimplemented (vanilla-shell) named cards — creatures use the
    ///   inline <c>new Creature("...", cost, p, t)</c> form which does NOT
    ///   go through the factory dispatch, so IsVanillaShell is false.
    /// - Spells with complex resolution (instants/sorceries) — saves us
    ///   from needing a full cast dispatcher. v1 test beds creatures only.
    /// </summary>
    private static (Player alice, Player bob) BuildBoard(bool spelledHands)
    {
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);

        SetupPlayer(alice, spelledHands);
        SetupPlayer(bob, spelledHands);

        return (alice, bob);
    }

    private static void SetupPlayer(Player p, bool spelledHands)
    {
        // Battlefield: 4 untapped basics + 2 ready creatures.
        // Note: we do NOT call SetZone (internal to Majik.Core); AddCard on the
        // zone collection is sufficient for the sandbox cloner and engine to see
        // the card. The bot's LegalActionEnumerator reads Zones.Battlefield /
        // Zones.Hand directly and doesn't depend on the Zone property.
        for (var i = 0; i < 4; i++)
        {
            var land = new Land("Forest");
            land.ChangeOwner(p);
            land.ChangeController(p);
            p.Zones.Battlefield.AddCard(land);
            // Leave untapped so the bot can cast from hand
        }

        for (var i = 0; i < 2; i++)
        {
            var bear = new Creature($"Grizzly Bears {i}", "{1}{G}", 2, 2);
            bear.ChangeOwner(p);
            bear.ChangeController(p);
            p.Zones.Battlefield.AddCard(bear);
            bear.ClearSummoningSickness();
        }

        // Hand: always 3 land cards; optionally 3 castable creatures
        for (var i = 0; i < 3; i++)
        {
            var land = new Land("Forest");
            land.ChangeOwner(p);
            p.Zones.Hand.AddCard(land);
        }

        if (spelledHands)
        {
            // 2-drop vanilla creatures: CMC 2, 4 untapped lands available => affordable
            for (var i = 0; i < 3; i++)
            {
                var bear = new Creature($"Grizzly Bears Hand {i}", "{1}{G}", 2, 2);
                bear.ChangeOwner(p);
                p.Zones.Hand.AddCard(bear);
            }
        }

        // Library: 20 basic lands (prevent draw-loss for maxTurns=10)
        for (var i = 0; i < 20; i++)
        {
            var land = new Land("Forest");
            land.ChangeOwner(p);
            p.Zones.Library.AddCard(land);
        }
    }
}
