using System.Diagnostics;
using FluentAssertions;
using Majik.Bot.Evaluation;
using Majik.Bot.Search;
using Majik.Core.CardData;
using Majik.Core.Cards;
using Majik.Core.Players;
using Majik.Core.Simulation;
using Majik.Core.StateMachine;
using Majik.Core.Zones;
using Xunit;
using Xunit.Abstractions;

namespace Majik.Bot.Tests.Search;

/// <summary>
/// Tree-state reuse Task 2: <see cref="EngineSimulator.AdvanceFrom"/> — the
/// spike's snapshot/restore machinery productized. Permanent equivalence tests:
/// advancing from a node snapshot with only the move SUFFIX must reach the
/// same decision and the same state as the full root-replay
/// <c>Advance(root, fullPath)</c>, on every spike-faithful board shape —
/// including the land-drop board that BROKE pre-seam (spike BREAK 2, fixed by
/// <see cref="ResumeCtx.LandDropsUsed"/> + the <c>SandboxGame.From</c> seeding
/// seam). Plus <see cref="SnapshotPolicy"/> eligibility unit tests and a
/// snapshot-capture cost sanity check.
/// </summary>
public sealed class AdvanceFromTests
{
    private static readonly EmbeddedCardRepository Repo = new();

    private readonly ITestOutputHelper _output;

    public AdvanceFromTests(ITestOutputHelper output) => _output = output;

    private static EngineSimulator NewSim() => new(ArchetypeWeights.ForArchetype("Burn"));

    // ── Board builders (mirrors TreeStateReuseSpikeTests) ─────────────────────

    private static void AddMountains(Player p, int count)
    {
        for (var i = 0; i < count; i++)
        {
            var m = (Land)NamedCardFactory.Create("Mountain", p);
            m.ChangeController(p);
            p.Zones.Battlefield.AddCard(m);
        }
    }

    private static void PadLibrary(Player p, int count = 20)
    {
        for (var i = 0; i < count; i++)
        {
            var l = new Land("Forest");
            l.ChangeOwner(p);
            p.Zones.GetZone(ZoneType.Library).AddCard(l);
        }
    }

    private static Creature AddReadyCreature(Player p, string name, string cost, int power, int toughness)
    {
        var c = new Creature(name, cost, power, toughness);
        c.ChangeOwner(p);
        p.Zones.Battlefield.AddCard(c);
        c.ClearSummoningSickness();
        return c;
    }

    private static void AddToHand(Player p, string repoCardName)
    {
        var card = new ScryfallCardFactory(Repo).Create(repoCardName, p);
        p.Zones.Hand.AddCard(card);
    }

    private static void AddLandToHand(Player p, string name = "Mountain")
    {
        var land = (Land)NamedCardFactory.Create(name, p);
        p.Zones.Hand.AddCard(land);
    }

    // ── Comparison helpers (the spike's fingerprint / state hash) ─────────────

    private static string Fingerprint(SimDecision d) =>
        $"{d.Kind}|{string.Join(",", d.LegalMoves.Select(m => m.Key).OrderBy(k => k, StringComparer.Ordinal))}";

    private static string StateHash(IReadOnlyList<Player> players)
    {
        static string Zone(Player p, ZoneType z)
        {
            var cards = p.Zones.GetZone(z).GetCards().ToList();
            var parts = cards
                .Select(c => z == ZoneType.Battlefield && c is Permanent perm
                    ? $"{c.Name}(t:{(perm.IsTapped ? 1 : 0)},{(c is Creature cr ? $"{cr.Power}/{cr.Toughness}" : "-")})"
                    : c.Name)
                .OrderBy(s => s, StringComparer.Ordinal);
            return $"{z}:{cards.Count}[{string.Join(",", parts)}]";
        }

        var zones = new[]
        {
            ZoneType.Hand, ZoneType.Battlefield, ZoneType.Graveyard,
            ZoneType.Library, ZoneType.Exile, ZoneType.Stack,
        };

        return string.Join(" || ", players
            .OrderBy(p => p.Name, StringComparer.Ordinal)
            .Select(p => $"{p.Name}=life:{p.LifeTotal},pool:{p.ManaPool.Total}," +
                         string.Join(";", zones.Select(z => Zone(p, z)))));
    }

    private static SimMove MoveByKey(SimDecision d, string key) =>
        d.LegalMoves.First(m => m.Key == key);

    // ════════════════════════════════════════════════════════════════════════
    // AdvanceFrom == full-root-replay equivalence (the spike's faithful shapes)
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Mid-phase, empty stack: snapshot after a land play, advance with only
    /// the cast suffix — decision fingerprint and state hash must equal the
    /// full root replay of [land, cast].
    /// </summary>
    [Fact(Timeout = 120_000)]
    public async Task AdvanceFrom_MidPhaseSuffix_EqualsFullRootReplay()
    {
        await Task.Yield();

        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 30);
        AddMountains(alice, 1);
        AddLandToHand(alice);
        AddToHand(alice, "Lightning Bolt");
        PadLibrary(alice);
        PadLibrary(bob);

        var root = SimState.Capture(
            new[] { alice, bob }, activePlayer: alice, turnNumber: 3,
            phase: PhaseStateType.PreCombatMain, searchedSeat: alice);
        var sim = NewSim();

        // Root cache = the root clone source (perfect-info: the live players).
        var rootCache = EngineSimulator.ResolveCloneSource(root);
        var rootCtx = ResumeCtx.ForRoot(root);

        var (n0, snap0) = sim.AdvanceFrom(rootCache, rootCtx, Array.Empty<SimMove>(), alice.Id);
        snap0.Should().NotBeNull("the root decision is a plain empty-stack main-phase window");
        var playLand = MoveByKey(n0, "Land:Mountain");

        var (n1, snap1) = sim.AdvanceFrom(rootCache, rootCtx, new[] { playLand }, alice.Id);
        snap1.Should().NotBeNull();
        snap1!.IsCacheEligible.Should().BeTrue();
        snap1.Ctx.SuffixFromParent.Should().BeEquivalentTo(new[] { playLand },
            "the snapshot records the suffix replayed from its cached ancestor");
        var castBolt = MoveByKey(n1, "Cast:Lightning Bolt");

        // Suffix-only advance from the cached node.
        var (reused, reusedSnap) = sim.AdvanceFrom(
            snap1.Players, snap1.Ctx, new[] { castBolt }, alice.Id);

        // Full root replay of the whole path.
        var (full, fullSandbox) = sim.AdvanceWithSandbox(root, new[] { playLand, castBolt });

        Fingerprint(reused).Should().Be(Fingerprint(full));
        reusedSnap.Should().NotBeNull();
        StateHash(reusedSnap!.Players).Should().Be(StateHash(fullSandbox.State.Players));
    }

    /// <summary>
    /// THE land-drop board (spike BREAK 2 — broke pre-seam, must now be
    /// FAITHFUL): the path plays one of TWO lands in hand; the original
    /// decision no longer offers the second land. The restored decision must
    /// agree — the snapshot's <see cref="ResumeCtx.LandDropsUsed"/> seeds the
    /// restored sandbox's tracker via <c>SandboxGame.From</c>.
    /// </summary>
    [Fact(Timeout = 120_000)]
    public async Task AdvanceFrom_LandDropBoard_RestoreNoLongerReoffersLand()
    {
        await Task.Yield();

        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 30);
        AddMountains(alice, 1);
        AddLandToHand(alice);
        AddLandToHand(alice);
        AddToHand(alice, "Lightning Bolt");
        PadLibrary(alice);
        PadLibrary(bob);

        var root = SimState.Capture(
            new[] { alice, bob }, activePlayer: alice, turnNumber: 3,
            phase: PhaseStateType.PreCombatMain, searchedSeat: alice);
        var sim = NewSim();

        var rootCache = EngineSimulator.ResolveCloneSource(root);
        var rootCtx = ResumeCtx.ForRoot(root);

        var (n0, _) = sim.AdvanceFrom(rootCache, rootCtx, Array.Empty<SimMove>(), alice.Id);
        var playLand = MoveByKey(n0, "Land:Mountain");

        var (n1, snap1) = sim.AdvanceFrom(rootCache, rootCtx, new[] { playLand }, alice.Id);
        n1.LegalMoves.Select(m => m.Key).Should().NotContain("Land:Mountain",
            "the original decision already consumed the turn's land drop");
        snap1.Should().NotBeNull();
        snap1!.Ctx.LandDropsUsed.Should().ContainKey(alice.Id)
            .WhoseValue.Should().Be(1, "the snapshot must carry the consumed drop");

        // RESTORE with an empty suffix: the decision must be re-reached
        // IDENTICALLY — in particular the land drop must NOT be re-offered.
        var (restored, _) = sim.AdvanceFrom(
            snap1.Players, snap1.Ctx, Array.Empty<SimMove>(), alice.Id);

        restored.LegalMoves.Select(m => m.Key).Should().NotContain("Land:Mountain",
            "the seeded LandDropTracker must remember the consumed drop (spike BREAK 2, fixed)");
        Fingerprint(restored).Should().Be(Fingerprint(n1));
    }

    /// <summary>
    /// Combat DeclareAttackers node (cache-eligible per policy) + a suffix
    /// drive that crosses a TURN boundary (the opponent's whole inline turn):
    /// AdvanceFrom must match the full root replay, and the new snapshot's
    /// resume context must sit in the later turn.
    /// </summary>
    [Fact(Timeout = 120_000)]
    public async Task AdvanceFrom_AttackNode_CrossTurnSuffix_EqualsFullRootReplay()
    {
        await Task.Yield();

        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 30);
        AddReadyCreature(alice, "Grizzly Bears", "{1}{G}", 2, 2);
        AddLandToHand(alice);
        PadLibrary(alice);
        PadLibrary(bob);

        var root = SimState.Capture(
            new[] { alice, bob }, activePlayer: alice, turnNumber: 3,
            phase: PhaseStateType.PreCombatMain, searchedSeat: alice);
        var sim = NewSim();

        var rootCache = EngineSimulator.ResolveCloneSource(root);
        var rootCtx = ResumeCtx.ForRoot(root);

        var (n0, _) = sim.AdvanceFrom(rootCache, rootCtx, Array.Empty<SimMove>(), alice.Id);
        var playLand = MoveByKey(n0, "Land:Mountain");

        var (n1, snap1) = sim.AdvanceFrom(rootCache, rootCtx, new[] { playLand }, alice.Id);
        n1.Kind.Should().Be(SimDecisionKind.DeclareAttackers);
        snap1.Should().NotBeNull("a DeclareAttackers ask at combat start is cache-eligible");
        snap1!.Ctx.Phase.Should().Be(PhaseStateType.Combat);

        var attack = MoveByKey(n1, "Attack:{Grizzly Bears}");

        var (reused, reusedSnap) = sim.AdvanceFrom(
            snap1.Players, snap1.Ctx, new[] { attack }, alice.Id);
        var (full, fullSandbox) = sim.AdvanceWithSandbox(root, new[] { playLand, attack });

        Fingerprint(reused).Should().Be(Fingerprint(full));
        reusedSnap.Should().NotBeNull();
        StateHash(reusedSnap!.Players).Should().Be(StateHash(fullSandbox.State.Players));
        reusedSnap.Ctx.TurnNumber.Should().BeGreaterThan(3,
            "the suffix drive crosses a turn boundary (the opponent's inline turn)");
    }

    /// <summary>
    /// Determinized world: the world's materialized base IS the root cache.
    /// AdvanceFrom on it (and on snapshots taken inside the world) must match
    /// the full per-world replay.
    /// </summary>
    [Fact(Timeout = 120_000)]
    public async Task AdvanceFrom_DeterminizedWorld_EqualsPerWorldReplay()
    {
        await Task.Yield();

        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 30);
        AddMountains(alice, 1);
        AddLandToHand(alice);
        AddToHand(alice, "Lightning Bolt");
        PadLibrary(alice);
        for (var i = 0; i < 3; i++) AddLandToHand(bob, "Mountain");
        PadLibrary(bob);

        var deck = Enumerable.Repeat("Forest", 30)
            .Concat(Enumerable.Repeat("Lightning Bolt", 10))
            .ToList();

        var root = SimState.Capture(
                new[] { alice, bob }, activePlayer: alice, turnNumber: 3,
                phase: PhaseStateType.PreCombatMain, searchedSeat: alice)
            .WithDeterminization(deck, worldSeed: 7);
        var sim = NewSim();

        // The root cache of a determinized world is the MATERIALIZED base
        // (cached on the SimState, so the full-replay drives below share it).
        var worldBase = EngineSimulator.ResolveCloneSource(root);
        var rootCtx = ResumeCtx.ForRoot(root);

        var (n0, _) = sim.AdvanceFrom(worldBase, rootCtx, Array.Empty<SimMove>(), alice.Id);
        var playLand = MoveByKey(n0, "Land:Mountain");

        var (n1, snap1) = sim.AdvanceFrom(worldBase, rootCtx, new[] { playLand }, alice.Id);
        snap1.Should().NotBeNull();
        var castBolt = MoveByKey(n1, "Cast:Lightning Bolt");

        var (reused, reusedSnap) = sim.AdvanceFrom(
            snap1!.Players, snap1.Ctx, new[] { castBolt }, alice.Id);
        var (full, fullSandbox) = sim.AdvanceWithSandbox(root, new[] { playLand, castBolt });

        Fingerprint(reused).Should().Be(Fingerprint(full));
        reusedSnap.Should().NotBeNull();
        StateHash(reusedSnap!.Players).Should().Be(StateHash(fullSandbox.State.Players));
    }

    // ════════════════════════════════════════════════════════════════════════
    // Eligibility — engine-level (the BREAK boards yield NO snapshot)
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Spike BREAK 1 policy: a decision reached with a spell ON the stack
    /// (second bolt castable while the first is unresolved) is NOT cached.
    /// </summary>
    [Fact(Timeout = 120_000)]
    public async Task AdvanceFrom_StackLadenDecision_YieldsNoSnapshot()
    {
        await Task.Yield();

        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 30);
        AddMountains(alice, 2);
        AddToHand(alice, "Lightning Bolt");
        AddToHand(alice, "Lightning Bolt");
        PadLibrary(alice);
        PadLibrary(bob);

        var root = SimState.Capture(
            new[] { alice, bob }, activePlayer: alice, turnNumber: 3,
            phase: PhaseStateType.PreCombatMain, searchedSeat: alice);
        var sim = NewSim();

        var rootCache = EngineSimulator.ResolveCloneSource(root);
        var rootCtx = ResumeCtx.ForRoot(root);

        var (n0, _) = sim.AdvanceFrom(rootCache, rootCtx, Array.Empty<SimMove>(), alice.Id);
        var castBolt = MoveByKey(n0, "Cast:Lightning Bolt");

        var (n1, snap1) = sim.AdvanceFrom(rootCache, rootCtx, new[] { castBolt }, alice.Id);

        n1.LegalMoves.Select(m => m.Key).Should().Contain("Cast:Lightning Bolt",
            "the second bolt is castable while the first sits on the stack");
        snap1.Should().BeNull("a stack-laden position must not be cached (spike BREAK 1)");
    }

    /// <summary>
    /// Spike BREAK 3 policy: a DeclareBlockers decision (mid-combat sub-state
    /// lives in CombatFlow) is NOT cached.
    /// </summary>
    [Fact(Timeout = 120_000)]
    public async Task AdvanceFrom_DeclareBlockersDecision_YieldsNoSnapshot()
    {
        await Task.Yield();

        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 30);
        AddReadyCreature(alice, "Grizzly Bears", "{1}{G}", 2, 2);
        AddReadyCreature(bob, "Craw Wurm", "{4}{G}{G}", 5, 5);
        PadLibrary(alice);
        PadLibrary(bob);

        var root = SimState.Capture(
            new[] { alice, bob }, activePlayer: alice, turnNumber: 3,
            phase: PhaseStateType.PreCombatMain, searchedSeat: alice);
        var sim = NewSim();

        var rootCache = EngineSimulator.ResolveCloneSource(root);
        var rootCtx = ResumeCtx.ForRoot(root);

        var (n0, _) = sim.AdvanceFrom(rootCache, rootCtx, Array.Empty<SimMove>(), alice.Id);
        n0.Kind.Should().Be(SimDecisionKind.DeclareAttackers);
        var noAttack = n0.LegalMoves.First(m => m.IsEmptyAttack);

        var (n1, snap1) = sim.AdvanceFrom(rootCache, rootCtx, new[] { noAttack }, alice.Id);

        n1.Kind.Should().Be(SimDecisionKind.DeclareBlockers,
            "the sandbox opponent attacks with his 5/5 into a 2/2 board");
        snap1.Should().BeNull("mid-combat nodes must not be cached (spike BREAK 3)");
    }

    // ════════════════════════════════════════════════════════════════════════
    // Eligibility — SnapshotPolicy unit tests (pure predicate)
    // ════════════════════════════════════════════════════════════════════════

    private static SimDecision PriorityDecision() =>
        new(SimDecisionKind.Priority, new[] { SimMove.ForTest("Pass"), SimMove.ForTest("Cast:X") });

    [Fact]
    public void Eligibility_PlainPriority_EmptyStack_MainPhase_IsEligible()
    {
        var players = new[] { new Player("A", 20), new Player("B", 20) };
        SnapshotPolicy.IsCacheEligible(players, PriorityDecision(), PhaseStateType.PreCombatMain)
            .Should().BeTrue();
    }

    [Fact]
    public void Eligibility_TerminalDecision_IsNotEligible()
    {
        var players = new[] { new Player("A", 20), new Player("B", 20) };
        SnapshotPolicy.IsCacheEligible(players, SimDecision.Terminal(1.0), PhaseStateType.PreCombatMain)
            .Should().BeFalse();
    }

    [Fact]
    public void Eligibility_NonEmptyStackZone_OnAnyPlayer_IsNotEligible()
    {
        var a = new Player("A", 20);
        var b = new Player("B", 20);
        var onStack = new Instant("Shock", "{R}");
        onStack.ChangeOwner(b);
        b.Zones.GetZone(ZoneType.Stack).AddCard(onStack);

        SnapshotPolicy.IsCacheEligible(new[] { a, b }, PriorityDecision(), PhaseStateType.PreCombatMain)
            .Should().BeFalse("spike BREAK 1 — a spell on the stack is lost by a players-only snapshot");
    }

    [Fact]
    public void Eligibility_DeclareBlockers_IsNotEligible()
    {
        var players = new[] { new Player("A", 20), new Player("B", 20) };
        var decision = new SimDecision(
            SimDecisionKind.DeclareBlockers, new[] { SimMove.ForTest("Block:{}") });
        SnapshotPolicy.IsCacheEligible(players, decision, PhaseStateType.Combat)
            .Should().BeFalse("spike BREAK 3 — the block ask sits after the attack declaration");
    }

    [Fact]
    public void Eligibility_MidCombatPriority_IsNotEligible()
    {
        var players = new[] { new Player("A", 20), new Player("B", 20) };
        SnapshotPolicy.IsCacheEligible(players, PriorityDecision(), PhaseStateType.Combat)
            .Should().BeFalse(
                "spike BREAK 3 — a priority window inside combat sits after declarations " +
                "that live in CombatFlow, not on the players");
    }

    [Fact]
    public void Eligibility_DeclareAttackersAsk_IsEligible()
    {
        var players = new[] { new Player("A", 20), new Player("B", 20) };
        var decision = new SimDecision(
            SimDecisionKind.DeclareAttackers, new[] { SimMove.ForTest("Attack:{}") });
        SnapshotPolicy.IsCacheEligible(players, decision, PhaseStateType.Combat)
            .Should().BeTrue(
                "the attack ask is the START of combat — resuming at the Combat phase " +
                "re-reaches it (spike-proven faithful)");
    }

    // ════════════════════════════════════════════════════════════════════════
    // Capture-cost sanity
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Snapshot capture = one <see cref="GameStateCloner.Clone"/> of the paused
    /// players — the spike measured ~0.12 ms (Release) on a realistic mid-game
    /// board. Sanity-assert it stays in that class (generous bound: this also
    /// runs in Debug); the measured median is written to the test output.
    /// </summary>
    [Fact(Timeout = 120_000)]
    public async Task SnapshotCapture_CostSanity()
    {
        await Task.Yield();

        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 30);
        AddMountains(alice, 3);
        AddReadyCreature(alice, "Grizzly Bears", "{1}{G}", 2, 2);
        AddToHand(alice, "Lightning Bolt");
        AddLandToHand(alice);
        PadLibrary(alice);
        AddReadyCreature(bob, "Craw Wurm", "{4}{G}{G}", 5, 5);
        PadLibrary(bob);

        var players = new[] { alice, bob };

        // Warmup (JIT + caches), then median of 9.
        GameStateCloner.Clone(players);
        var times = new List<double>(9);
        for (var i = 0; i < 9; i++)
        {
            var sw = Stopwatch.StartNew();
            GameStateCloner.Clone(players);
            sw.Stop();
            times.Add(sw.Elapsed.TotalMilliseconds);
        }
        times.Sort();
        var median = times[times.Count / 2];

        _output.WriteLine($"snapshot capture (GameStateCloner.Clone) median: {median:F3} ms");
        median.Should().BeLessThan(10.0,
            "snapshot capture must stay in the sub-millisecond class the spike measured " +
            "(~0.12 ms Release); 10 ms is the generous Debug/CI sanity bound");
    }
}
