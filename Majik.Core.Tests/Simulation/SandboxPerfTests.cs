using FluentAssertions;
using Majik.Core.Counters;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Random;
using Majik.Core.Simulation;
using Majik.Core.Zones;
using Xunit;
using Xunit.Abstractions;

namespace Majik.Core.Tests.Simulation;

/// <summary>
/// Phase-0 performance gate: measures clone cost (µs) and rollout cost (ms)
/// for a representative mid-game board, then reports implied rollouts/3-second
/// decision window. The printed [PERF] line is the real deliverable; the
/// assertions are loose ceilings only.
///
/// Board: each player has 5 creatures (mix of tapped/untapped, with
/// damage/counters), 5 lands, 3 hand cards, and a 20-card basic-land library
/// so the draw step doesn't immediately deck them out.
/// </summary>
public sealed class SandboxPerfTests
{
    private readonly ITestOutputHelper _out;
    public SandboxPerfTests(ITestOutputHelper o) => _out = o;

    [Fact]
    public void Bench_CloneAndRollout_ReportsCost()
    {
        var (alice, bob) = BuildRepresentativeMidGameBoard();
        var players = new[] { alice, bob };

        // ── Clone benchmark ─────────────────────────────────────────────────
        // Warm up the JIT before timing.
        for (var i = 0; i < 20; i++) _ = GameStateCloner.Clone(players);

        var sw = System.Diagnostics.Stopwatch.StartNew();
        const int N = 500;
        for (var i = 0; i < N; i++) _ = GameStateCloner.Clone(players);
        sw.Stop();
        var perCloneUs = sw.Elapsed.TotalMilliseconds * 1000.0 / N;

        // ── Rollout benchmark ───────────────────────────────────────────────
        // Warm up one full rollout first (JIT + first-run allocation).
        RunOneRollout(players, seed: 999);

        sw.Restart();
        const int R = 30;
        for (var i = 0; i < R; i++) RunOneRollout(players, seed: i);
        sw.Stop();
        var perRolloutMs = sw.Elapsed.TotalMilliseconds / R;

        // ── Report ──────────────────────────────────────────────────────────
        var rolloutsPer3s = 3000.0 / perRolloutMs;
        _out.WriteLine(
            $"[PERF] clone={perCloneUs:F1}us  rollout={perRolloutMs:F1}ms  => ~{rolloutsPer3s:F0} rollouts / 3s decision");
        _out.WriteLine(
            $"[PERF] board: 2 players × (5 creatures + 5 lands + 3 hand + 20-card library), maxTurns=6");

        // ── Loose ceilings — the printed number is the real output ──────────
        perCloneUs.Should().BeLessThan(5_000,  "clone should complete in <5 ms even on slow CI hardware");
        perRolloutMs.Should().BeLessThan(500,  "<500 ms per shallow rollout (maxTurns=6, all-pass agent)");
    }

    // ── Board builder ────────────────────────────────────────────────────────

    /// <summary>
    /// Builds a representative mid-game board. No spells on the stack and no
    /// abilities wired — the creatures are plain vanilla permanents with mixed
    /// tap/damage/counter state. This keeps RunGameAsync safe with
    /// DeterministicBotAgent (which always passes priority) while being
    /// substantially heavier than an empty board.
    /// </summary>
    private static (Player alice, Player bob) BuildRepresentativeMidGameBoard()
    {
        var alice = new Player("Alice", 20);
        var bob   = new Player("Bob",   14);   // 14 life = mid-game feel

        alice.GainEnergy(2);
        alice.AddPoisonCounters(1);

        // ── Alice battlefield: 5 creatures + 5 lands ──────────────────────

        // 1. Tapped bear
        var a1 = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        a1.ChangeOwner(alice); a1.ChangeController(alice);
        alice.Zones.Battlefield.AddCard(a1);
        a1.SetZone(ZoneType.Battlefield);
        a1.Tap();

        // 2. Untapped creature with 1 damage
        var a2 = new Creature("Hill Giant", "{3}{R}", 3, 3);
        a2.ChangeOwner(alice); a2.ChangeController(alice);
        alice.Zones.Battlefield.AddCard(a2);
        a2.SetZone(ZoneType.Battlefield);
        a2.TakeDamage(1);

        // 3. Creature with a +1/+1 counter, no summoning sickness
        var a3 = new Creature("Llanowar Elves", "{G}", 1, 1);
        a3.ChangeOwner(alice); a3.ChangeController(alice);
        alice.Zones.Battlefield.AddCard(a3);
        a3.SetZone(ZoneType.Battlefield);
        a3.ClearSummoningSickness();
        a3.Counters.Add(CounterType.PlusOnePlusOne, 1);

        // 4. Fresh untapped creature
        var a4 = new Creature("Craw Wurm", "{4}{G}{G}", 6, 4);
        a4.ChangeOwner(alice); a4.ChangeController(alice);
        alice.Zones.Battlefield.AddCard(a4);
        a4.SetZone(ZoneType.Battlefield);

        // 5. Creature with 2 damage + tapped
        var a5 = new Creature("Serra Angel", "{3}{W}{W}", 4, 4);
        a5.ChangeOwner(alice); a5.ChangeController(alice);
        alice.Zones.Battlefield.AddCard(a5);
        a5.SetZone(ZoneType.Battlefield);
        a5.TakeDamage(2);
        a5.Tap();

        // Alice lands: 3 tapped + 2 untapped
        AddLands(alice, "Forest",   tapped: true,  count: 3);
        AddLands(alice, "Mountain", tapped: false, count: 2);

        // ── Alice hand + library ───────────────────────────────────────────
        AddHand(alice, "Counterspell",    "{U}{U}");
        AddHand(alice, "Lightning Bolt",  "{R}");
        AddHand(alice, "Giant Growth",    "{G}");
        SeedLibrary(alice, count: 20);

        // ── Bob battlefield: 5 creatures + 5 lands ────────────────────────

        var b1 = new Creature("Goblin Guide", "{R}", 2, 2);
        b1.ChangeOwner(bob); b1.ChangeController(bob);
        bob.Zones.Battlefield.AddCard(b1);
        b1.SetZone(ZoneType.Battlefield);
        b1.Tap();

        var b2 = new Creature("Memnite", "{0}", 1, 1);
        b2.ChangeOwner(bob); b2.ChangeController(bob);
        bob.Zones.Battlefield.AddCard(b2);
        b2.SetZone(ZoneType.Battlefield);

        var b3 = new Creature("Savannah Lions", "{W}", 2, 1);
        b3.ChangeOwner(bob); b3.ChangeController(bob);
        bob.Zones.Battlefield.AddCard(b3);
        b3.SetZone(ZoneType.Battlefield);
        b3.ClearSummoningSickness();
        b3.Counters.Add(CounterType.PlusOnePlusOne, 2);

        var b4 = new Creature("Shivan Dragon", "{4}{R}{R}", 5, 5);
        b4.ChangeOwner(bob); b4.ChangeController(bob);
        bob.Zones.Battlefield.AddCard(b4);
        b4.SetZone(ZoneType.Battlefield);
        b4.TakeDamage(3);

        var b5 = new Creature("Stone Golem", "{5}", 4, 4);
        b5.ChangeOwner(bob); b5.ChangeController(bob);
        bob.Zones.Battlefield.AddCard(b5);
        b5.SetZone(ZoneType.Battlefield);

        // Bob lands: 2 tapped + 3 untapped
        AddLands(bob, "Mountain", tapped: true,  count: 2);
        AddLands(bob, "Plains",   tapped: false, count: 3);

        // ── Bob hand + library ─────────────────────────────────────────────
        AddHand(bob, "Swords to Plowshares", "{W}");
        AddHand(bob, "Terror",               "{1}{B}");
        AddHand(bob, "Shock",                "{R}");
        SeedLibrary(bob, count: 20);

        return (alice, bob);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static void AddLands(Player owner, string name, bool tapped, int count)
    {
        for (var i = 0; i < count; i++)
        {
            var land = new Land(name);
            land.ChangeOwner(owner);
            land.ChangeController(owner);
            owner.Zones.Battlefield.AddCard(land);
            land.SetZone(ZoneType.Battlefield);
            if (tapped) land.Tap();
        }
    }

    private static void AddHand(Player owner, string name, string cost)
    {
        var card = new Instant(name, cost);
        card.ChangeOwner(owner);
        owner.Zones.Hand.AddCard(card);
        card.SetZone(ZoneType.Hand);
    }

    private static void SeedLibrary(Player player, int count)
    {
        for (var i = 0; i < count; i++)
        {
            var land = new Land("Forest");
            land.ChangeOwner(player);
            player.Zones.Library.AddCard(land);
            land.SetZone(ZoneType.Library);
        }
    }

    private static void RunOneRollout(IReadOnlyList<Player> players, int seed)
    {
        var sb = SandboxGame.From(players, new GameRandom(seed), _ => new DeterministicBotAgent());
        sb.Driver.RunGameAsync(maxTurns: 6, startingPlayerIndex: 0, System.Threading.CancellationToken.None)
          .GetAwaiter().GetResult();
    }
}
