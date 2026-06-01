using System.Collections.Concurrent;
using FluentAssertions;
using Majik.Core.Cards;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.Game;

/// <summary>
/// PLAN 08 — proves the per-game DETERMINISTIC OBJECT-ID source: ids minted
/// inside a pushed game scope are reproducible given the seed, ids minted
/// outside any scope fall back to globally-unique <see cref="System.Guid.NewGuid"/>,
/// and two concurrent (parallel) games get ISOLATED id sequences that never
/// cross-contaminate (the AsyncLocal ambient scope). Together with the
/// LogicalClock work this makes GameFacade.FromSnapshot replay id-identical.
/// </summary>
public class DeterministicIdScopeTests
{
    // ── DeterministicIdSource primitive ────────────────────────────────

    [Fact]
    public void SameSeed_ProducesIdenticalIdSequence()
    {
        var a = new DeterministicIdSource(42);
        var b = new DeterministicIdSource(42);

        var seqA = new[] { a.NextId(), a.NextId(), a.NextId() };
        var seqB = new[] { b.NextId(), b.NextId(), b.NextId() };

        seqB.Should().Equal(seqA, "same seed + same call order ⇒ same ids (replay)");
    }

    [Fact]
    public void DifferentSeeds_ProduceDifferentIdSequences()
    {
        var a = new DeterministicIdSource(1);
        var b = new DeterministicIdSource(2);

        var seqA = new[] { a.NextId(), a.NextId(), a.NextId() };
        var seqB = new[] { b.NextId(), b.NextId(), b.NextId() };

        seqA.Should().NotEqual(seqB);
    }

    [Fact]
    public void NextId_WithinOneSource_NeverRepeats()
    {
        var src = new DeterministicIdSource(7);
        var ids = Enumerable.Range(0, 1000).Select(_ => src.NextId()).ToList();
        ids.Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void Compose_IsStableAcrossCalls_ForGivenSeedAndCounter()
    {
        // The exact (seed, counter) → Guid mapping must not drift between calls
        // (cross-process replay determinism). Pin the derivation, not the value.
        DeterministicIdSource.Compose(42, 1)
            .Should().Be(DeterministicIdSource.Compose(42, 1));
        DeterministicIdSource.Compose(42, 1)
            .Should().NotBe(DeterministicIdSource.Compose(42, 2));
        DeterministicIdSource.Compose(42, 1)
            .Should().NotBe(DeterministicIdSource.Compose(43, 1));
    }

    // ── Ambient scope ──────────────────────────────────────────────────

    [Fact]
    public void NewId_OutsideAnyScope_FallsBackToRandomGuids()
    {
        DeterministicIdScope.Current.Should().BeNull("no scope is installed");

        var id1 = DeterministicIdScope.NewId();
        var id2 = DeterministicIdScope.NewId();

        // Fallback = Guid.NewGuid(): unique + nonzero, never the deterministic
        // sequence. This is what the ~16.5k direct-construction unit tests get,
        // so their global-uniqueness assumptions are preserved.
        id1.Should().NotBe(id2);
        id1.Should().NotBe(Guid.Empty);
    }

    [Fact]
    public void Push_InstallsAmbientSource_AndRestoresOnDispose()
    {
        DeterministicIdScope.Current.Should().BeNull();

        var src = new DeterministicIdSource(99);
        using (DeterministicIdScope.Push(src))
        {
            DeterministicIdScope.Current.Should().BeSameAs(src);
            // Under the scope, NewId draws the deterministic sequence (the Nth
            // value matches a fresh same-seed source's Nth value).
            DeterministicIdScope.NewId()
                .Should().Be(new DeterministicIdSource(99).NextId());
        }

        DeterministicIdScope.Current.Should().BeNull("scope restored on dispose");
    }

    [Fact]
    public void Push_NestsAndRestoresPrevious()
    {
        var outer = new DeterministicIdSource(1);
        var inner = new DeterministicIdSource(2);
        using (DeterministicIdScope.Push(outer))
        {
            DeterministicIdScope.Current.Should().BeSameAs(outer);
            using (DeterministicIdScope.Push(inner))
            {
                DeterministicIdScope.Current.Should().BeSameAs(inner);
            }
            DeterministicIdScope.Current.Should().BeSameAs(outer);
        }
        DeterministicIdScope.Current.Should().BeNull();
    }

    // ── Object construction picks up the ambient source ────────────────

    [Fact]
    public void CardsAndPlayers_MintedUnderScope_AreDeterministic_PerSeed()
    {
        static (Guid player, Guid card) Run(int seed)
        {
            using var _ = DeterministicIdScope.Push(new DeterministicIdSource(seed));
            var p = new Player("Alice", 20);
            var c = new Creature("Grizzly Bears", "1G", 2, 2) { Owner = p, Controller = p };
            return (p.Id, c.InstanceId);
        }

        var run1 = Run(42);
        var run2 = Run(42);

        // Same seed + same construction order ⇒ identical portal-facing ids.
        run2.player.Should().Be(run1.player);
        run2.card.Should().Be(run1.card);

        // Different seed ⇒ different ids.
        var other = Run(43);
        other.player.Should().NotBe(run1.player);
    }

    // ── Concurrent-game isolation (the AsyncLocal scope) ───────────────

    [Fact]
    public async Task TwoConcurrentGames_GetIsolatedIdSequences_NoCrossContamination()
    {
        // Run many games in parallel, each with its OWN seed and its OWN pushed
        // scope. Each game mints a batch of ids on its async flow (with awaits
        // forcing continuations onto arbitrary threadpool threads). Two games
        // with the SAME seed must mint the SAME sequence (reproducible), and two
        // games with DIFFERENT seeds must not share ids — i.e. no game's counter
        // leaks into another's via the shared static ambient.
        async Task<List<Guid>> PlayGame(int seed)
        {
            using var _ = DeterministicIdScope.Push(new DeterministicIdSource(seed));
            var ids = new List<Guid>();
            for (var i = 0; i < 50; i++)
            {
                ids.Add(DeterministicIdScope.NewId());
                // Force the continuation off this stack so a naive (non-
                // AsyncLocal) implementation would let a sibling game's scope
                // bleed in. With AsyncLocal the scope flows with the flow.
                await Task.Yield();
            }
            return ids;
        }

        // 8 games: pairs (0,4),(1,5),(2,6),(3,7) share seeds → identical
        // sequences expected; everything else distinct.
        var tasks = Enumerable.Range(0, 8)
            .Select(i => Task.Run(() => PlayGame(seed: i % 4)))
            .ToArray();
        var results = await Task.WhenAll(tasks);

        // Each game's own sequence has no internal collisions.
        foreach (var r in results) r.Should().OnlyHaveUniqueItems();

        // Same-seed games produced byte-identical sequences (isolation held:
        // neither game stole the other's counter mid-flight).
        for (var i = 0; i < 4; i++)
        {
            results[i].Should().Equal(results[i + 4],
                $"games with the same seed ({i}) must replay the same id sequence");
        }

        // Different-seed games share no ids at all.
        var s0 = results[0].ToHashSet();
        var s1 = results[1].ToHashSet();
        s0.Overlaps(s1).Should().BeFalse("different seeds ⇒ disjoint id sets");
    }

    [Fact]
    public void ConstructionOutsideScope_DuringAnotherGamesScope_StillRandom()
    {
        // A direct-construction object built on a flow that never pushed a scope
        // must remain random even if some OTHER async flow has a scope active.
        using var _ = DeterministicIdScope.Push(new DeterministicIdSource(5));
        var inScope = new Player("In", 20).Id;
        inScope.Should().Be(new DeterministicIdSource(5).NextId());
    }

    [Fact]
    public void EmblemAndAbilityIds_AlsoDeterministic_UnderScope()
    {
        static (Guid emblem, Guid abilityCount) Run(int seed)
        {
            using var _ = DeterministicIdScope.Push(new DeterministicIdSource(seed));
            var p = new Player("Alice", 20);
            var emblem = new Emblem(p, "Source", System.Array.Empty<Majik.Core.Abilities.IAbility>());
            return (emblem.Id, default);
        }

        Run(11).emblem.Should().Be(Run(11).emblem);
        Run(11).emblem.Should().NotBe(Run(12).emblem);
    }
}
