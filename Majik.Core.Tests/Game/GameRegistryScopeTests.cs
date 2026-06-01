using System.IO;
using System.Text.RegularExpressions;
using FluentAssertions;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Random;
using Majik.Core.Services;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.Game;

/// <summary>
/// Covers the per-game ambient scoping of the process-level registries
/// (<see cref="AgentRegistry"/>, <see cref="GameRandomRegistry"/>,
/// <see cref="EventBusRegistry"/>, <see cref="ZoneServiceRegistry"/>) —
/// installed via <see cref="GameRegistryScope.PushForGame"/>, mirroring
/// <c>LogicalClockScope</c>.
///
/// These verify the three problems the de-static fix targets:
///   1. concurrent games no longer cross their RNG (correctness);
///   2. a game's entries are reclaimed when its scope ends (leak);
///   3. the AsyncLocal flows across await continuations (so live games keep
///      isolation even though resolution hops threadpool threads).
/// Plus a guard test that BANS <c>GameRandomRegistry.Default</c> under
/// <c>CardData/Factories/</c> (the shuffle footgun fixed in Atraxa / Knight-
/// Errant of Eos).
/// </summary>
[Collection(nameof(StaticRegistryCollection))]
public class GameRegistryScopeTests
{
    // ── Cross-game RNG isolation (the correctness bug) ──────────────────

    [Fact]
    public void ConcurrentScopes_DoNotCrossRng()
    {
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);

        // Two games, each with a distinct seeded RNG registered for its
        // controller. Under the OLD static registry both .Set calls landed in
        // ONE shared dictionary keyed by Player.Id and .Default was the most-
        // recently-constructed game's RNG, so a Get(controller) in game A could
        // resolve game B's RNG. With per-game ambient scopes the lookups stay
        // isolated.
        GameRandom seenInA;
        GameRandom seenInB;

        using (GameRegistryScope.PushForGame())
        {
            var rngA = new GameRandom(seed: 111);
            GameRandomRegistry.Set(alice, rngA);
            GameRandomRegistry.SetDefault(rngA);

            // Open game B's scope NESTED — emulating a second match constructed
            // while the first is live. Its registrations must not bleed into A.
            using (GameRegistryScope.PushForGame())
            {
                var rngB = new GameRandom(seed: 222);
                GameRandomRegistry.Set(bob, rngB);
                GameRandomRegistry.SetDefault(rngB);

                seenInB = GameRandomRegistry.Get(bob);
                // alice was never registered in B's scope → falls back to B's
                // default (rngB), NOT game A's rngA.
                GameRandomRegistry.Get(alice).Should().BeSameAs(rngB);
            }

            // Back in game A's scope, alice still resolves rngA — game B's
            // registrations were reclaimed when its scope ended.
            seenInA = GameRandomRegistry.Get(alice);
            GameRandomRegistry.Default.Should().BeSameAs(rngA);
        }

        seenInA.Seed.Should().Be(111);
        seenInB.Seed.Should().Be(222);
        seenInA.Should().NotBeSameAs(seenInB);
    }

    [Fact]
    public async Task ConcurrentGames_RunInParallel_KeepDistinctRng()
    {
        // Two independent async flows, each its own scope, racing on the
        // threadpool. Each must see ONLY its own RNG. This is the property the
        // AsyncLocal scope guarantees that a process-global static cannot.
        async Task<int> RunGame(int seed, Player controller)
        {
            using var _ = GameRegistryScope.PushForGame();
            var rng = new GameRandom(seed);
            GameRandomRegistry.Set(controller, rng);
            GameRandomRegistry.SetDefault(rng);

            // Hop threads a few times; the scope must flow across each await.
            for (var i = 0; i < 5; i++)
            {
                await Task.Yield();
                GameRandomRegistry.Get(controller).Seed.Should().Be(seed);
                GameRandomRegistry.Default.Seed.Should().Be(seed);
            }
            return GameRandomRegistry.Get(controller).Seed;
        }

        var a = RunGame(111, new Player("A", 20));
        var b = RunGame(222, new Player("B", 20));
        var results = await Task.WhenAll(a, b);
        results.Should().BeEquivalentTo(new[] { 111, 222 });
    }

    // ── Scope flows across await continuations (mirrors LogicalClock) ────

    [Fact]
    public async Task Scope_FlowsAcrossAwaitContinuations()
    {
        var alice = new Player("Alice", 20);
        var agent = new DeterministicBotAgent();

        using var _ = GameRegistryScope.PushForGame();
        AgentRegistry.Set(alice, agent);

        await Task.Yield();
        // After the continuation resumes (possibly on a different threadpool
        // thread) the scoped store is still active.
        AgentRegistry.Get(alice).Should().BeSameAs(agent);

        await Task.Run(() =>
        {
            // AsyncLocal flows into Task.Run's captured context too.
            AgentRegistry.Get(alice).Should().BeSameAs(agent);
        });
    }

    // ── Teardown / leak reclamation ─────────────────────────────────────

    [Fact]
    public void EntriesReclaimed_WhenScopeEnds()
    {
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);
        var bus = new EventBus();
        var rng = new GameRandom(7);

        using (GameRegistryScope.PushForGame())
        {
            AgentRegistry.Set(alice, new DeterministicBotAgent());
            GameRandomRegistry.Set(alice, rng);
            EventBusRegistry.Set(bob, bus);

            AgentRegistry.Get(alice).Should().NotBeNull();
            EventBusRegistry.Get(bob).Should().BeSameAs(bus);
        }

        // Outside the scope the fallback store is active — the per-game store
        // (and everything it held) was dropped, so none of those entries
        // survive. This is the leak fix: a finished match holds nothing.
        AgentRegistry.Get(alice).Should().BeNull();
        EventBusRegistry.Get(bob).Should().BeNull();
    }

    // ── Guard: ban GameRandomRegistry.Default under CardData/Factories ──

    [Fact]
    public void NoFactory_UsesGameRandomRegistryDefault()
    {
        var factoriesDir = Path.Combine(CoreProjectRoot(), "CardData", "Factories");
        Directory.Exists(factoriesDir).Should().BeTrue(
            $"expected factories dir at {factoriesDir}");

        var offenders = new List<string>();
        // Matches `GameRandomRegistry.Default` as a *member access* (the
        // shuffle footgun), not the word in a comment/string. We strip
        // line-comments first, then look for the bare `.Default` access on the
        // registry (i.e. not followed by `(`, which would be a method, and the
        // registry has no Default(...) method anyway).
        var pattern = new Regex(@"GameRandomRegistry\s*\.\s*Default\b");

        foreach (var file in Directory.EnumerateFiles(factoriesDir, "*.cs", SearchOption.AllDirectories))
        {
            foreach (var raw in File.ReadAllLines(file))
            {
                var idx = raw.IndexOf("//", StringComparison.Ordinal);
                var code = idx >= 0 ? raw[..idx] : raw;
                if (pattern.IsMatch(code))
                {
                    offenders.Add($"{Path.GetFileName(file)}: {raw.Trim()}");
                }
            }
        }

        offenders.Should().BeEmpty(
            "card factories must use GameRandomRegistry.Get(controller/owner) — " +
            "GameRandomRegistry.Default resolves the most-recently-constructed " +
            "game's RNG, which corrupts concurrent matches (the Atraxa / Knight-" +
            "Errant-of-Eos shuffle bug). Offenders:\n" + string.Join("\n", offenders));
    }

    private static string CoreProjectRoot()
    {
        // Walk up from the test bin dir to the test project root, then over to
        // the sibling Majik.Core project (same pattern as the parity tests).
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Majik.Core.Tests.csproj")))
        {
            dir = dir.Parent;
        }
        var testRoot = dir?.FullName
            ?? throw new InvalidOperationException("Could not locate Majik.Core.Tests project root.");
        return Path.Combine(Directory.GetParent(testRoot)!.FullName, "Majik.Core");
    }
}
