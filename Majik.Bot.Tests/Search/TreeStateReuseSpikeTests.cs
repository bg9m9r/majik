using System.Diagnostics;
using FluentAssertions;
using Majik.Bot.Evaluation;
using Majik.Bot.Search;
using Majik.Core.CardData;
using Majik.Core.Cards;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Simulation;
using Majik.Core.StateMachine;
using Majik.Core.Zones;
using Xunit;
using Xunit.Abstractions;

namespace Majik.Bot.Tests.Search;

/// <summary>
/// THE SPIKE for MCTS tree-state reuse (plan
/// <c>2026-06-11-tree-state-reuse.md</c>, Task 1 — GO/NO-GO gate).
///
/// <para>
/// <b>Question:</b> can a tree node's reached position be SNAPSHOT (clone the
/// paused sandbox's players + record turn/phase/active seat) and later RESTORED
/// (clone the snapshot → fresh sandbox → resume at the recorded phase → replay
/// only the move suffix) so the restored drive is byte-identical to the full
/// root-replay <c>Advance(root, path)</c>?
/// </para>
///
/// <para>
/// <b>Mechanism under test:</b> restore is expressed through the EXISTING
/// pipeline — a snapshot becomes a new <see cref="SimState"/> whose
/// <c>LivePlayers</c> are the frozen clones, and <see cref="EngineSimulator.Advance"/>
/// on that state IS the restore (clone-of-clone is the proven determinization
/// world-base pattern). The only seam is
/// <see cref="EngineSimulator.AdvanceWithSandbox"/>, which exposes the paused
/// sandbox so the snapshot can be taken.
/// </para>
///
/// <para>
/// <b>Findings encoded here</b> (each "diverges" test PASSES by asserting the
/// divergence — they are findings, not failures):
/// <list type="bullet">
///   <item>FAITHFUL: mid-phase priority decisions (empty stack), combat
///     DeclareAttackers decisions, cross-phase and cross-turn suffix drives,
///     determinized (materialized-world) roots.</item>
///   <item>BREAKS — engine state living OUTSIDE the players:
///     (1) the Stack subsystem (a spell ON the stack at snapshot time is lost;
///     its card is cloned into the stack ZONE as an orphan),
///     (2) <c>LandDropTracker</c> (driver-level per-turn tally — a land played
///     earlier in the snapshot turn is forgotten, so restore re-offers the
///     land drop),
///     (3) mid-combat sub-state (declared attackers are tapped in the snapshot,
///     so resuming at the Combat phase start cannot re-declare them).</item>
///   <item>BREAK #4 — NOT a snapshot problem but a PRE-EXISTING script-replay
///     window-alignment artifact, since FIXED: <see cref="SearchAgent"/> used to
///     consume a scripted priority move at the NEXT priority ask, which after a
///     cast is the mid-stack re-ask. A scripted sorcery there was rejected by
///     the engine and "treated as a pass" (PriorityLoop) — silently WASTED,
///     while capture mode pauses at the post-resolution window instead. Script
///     consumption is now aligned to substantive windows (consume only where
///     capture mode would pause — see <c>ScriptWindowAlignmentTests</c>), so
///     full root-replay agrees with the tree's own node model and with a
///     suffix-restore (the Task 3 equivalence precondition).</item>
/// </list>
/// </para>
/// </summary>
public sealed class TreeStateReuseSpikeTests
{
    private static readonly EmbeddedCardRepository Repo = new();

    private readonly ITestOutputHelper _output;

    public TreeStateReuseSpikeTests(ITestOutputHelper output) => _output = output;

    private static EngineSimulator NewSim() => new(ArchetypeWeights.ForArchetype("Burn"));

    // ════════════════════════════════════════════════════════════════════════
    // Board builders
    // ════════════════════════════════════════════════════════════════════════

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

    /// <summary>
    /// Library padding that never creates a priority decision when drawn
    /// (unaffordable 6-drops — no land windows, no cast windows), so the only
    /// substantive decision on each later turn is the attack ask. Used by the
    /// cost path to deepen turn-by-turn without tripping BREAK #4.
    /// </summary>
    private static void PadLibraryWithUncastables(Player p, int count = 20)
    {
        for (var i = 0; i < count; i++)
        {
            var c = new Creature("Craw Wurm", "{4}{G}{G}", 6, 4);
            c.ChangeOwner(p);
            p.Zones.GetZone(ZoneType.Library).AddCard(c);
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

    // ════════════════════════════════════════════════════════════════════════
    // Drive + snapshot + comparison helpers
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>Resume context observed during a drive — what a restore needs.</summary>
    private sealed class ResumeCtx
    {
        public int TurnNumber;
        public PhaseStateType Phase;
        public Guid ActivePlayerId;
    }

    /// <summary>
    /// Drive <c>Advance(root, path)</c> via the spike seam, tracking the
    /// turn / phase / active seat the sandbox is at when it pauses — the
    /// resume context a node snapshot must record.
    /// </summary>
    private static (SimDecision Decision, SandboxGame Sandbox, ResumeCtx Ctx) TrackedAdvance(
        EngineSimulator sim, SimState root, IReadOnlyList<SimMove> path)
    {
        var ctx = new ResumeCtx
        {
            TurnNumber = root.TurnNumber,
            Phase = root.Phase,
            ActivePlayerId = root.ActivePlayer.Id,
        };

        var (decision, sandbox) = sim.AdvanceWithSandbox(root, path, sb =>
        {
            sb.Bus.Subscribe<TurnStartedEvent>(e =>
            {
                ctx.TurnNumber = e.TurnNumber;
                ctx.ActivePlayerId = e.Player.Id;
            });
            sb.Bus.Subscribe<PhaseStateChangedEvent>(e => ctx.Phase = e.CurrentState);
        });

        return (decision, sandbox, ctx);
    }

    /// <summary>
    /// SNAPSHOT: freeze the paused sandbox's players (one defensive clone, so
    /// later engine activity can never mutate the snapshot) and wrap them in a
    /// new <see cref="SimState"/> carrying the recorded resume context. A later
    /// <see cref="EngineSimulator.Advance"/> on this state IS the restore:
    /// clone the frozen players → fresh sandbox → resume at the node's phase →
    /// replay the supplied suffix.
    /// </summary>
    private static SimState Snapshot(SandboxGame sandbox, Guid searchedSeatId, ResumeCtx ctx)
    {
        var frozen = GameStateCloner.Clone(sandbox.State.Players).Players;
        var searched = frozen.First(p => p.Id == searchedSeatId);
        var active = frozen.First(p => p.Id == ctx.ActivePlayerId);
        return SimState.Capture(frozen, active, ctx.TurnNumber, ctx.Phase, searched);
    }

    /// <summary>Decision identity: kind + the multiset of legal-move keys.</summary>
    private static string Fingerprint(SimDecision d) =>
        $"{d.Kind}|{string.Join(",", d.LegalMoves.Select(m => m.Key).OrderBy(k => k, StringComparer.Ordinal))}";

    /// <summary>
    /// State hash over everything the spike cares about: per player (ordered by
    /// name) life, mana pool, and per zone the sorted card names — battlefield
    /// cards additionally carry tapped + P/T so combat damage / tap state shows.
    /// </summary>
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
    // FIDELITY — faithful cases
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Mid-phase, empty stack: after one cast in the pre-combat main, the node's
    /// decision (a second castable spell pending) restores byte-identically —
    /// same decision fingerprint AND same state hash at the pause.
    /// </summary>
    [Fact(Timeout = 120_000)]
    public async Task MidPhaseDecision_EmptyStack_RestoresIdentically()
    {
        await Task.Yield();

        // Alice: 2 Mountains; hand Bolt (instant) + Lava Spike (sorcery). The
        // sorcery is NOT castable while the bolt is on the stack, so the first
        // post-cast pause happens AFTER the bolt resolves: mid-phase, empty stack.
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 30);
        AddMountains(alice, 2);
        AddToHand(alice, "Lightning Bolt");
        AddToHand(alice, "Lava Spike");
        PadLibrary(alice);
        PadLibrary(bob);

        var root = SimState.Capture(
            new[] { alice, bob }, activePlayer: alice, turnNumber: 3,
            phase: PhaseStateType.PreCombatMain, searchedSeat: alice);
        var sim = NewSim();

        var (n0, _, _) = TrackedAdvance(sim, root, Array.Empty<SimMove>());
        n0.Kind.Should().Be(SimDecisionKind.Priority);
        var castBolt = MoveByKey(n0, "Cast:Lightning Bolt");

        // Drive to N1 = the mid-phase decision after the bolt resolved.
        var (n1, sandbox1, ctx1) = TrackedAdvance(sim, root, new[] { castBolt });
        n1.IsTerminal.Should().BeFalse();
        n1.LegalMoves.Select(m => m.Key).Should().Contain("Cast:Lava Spike");
        ctx1.Phase.Should().Be(PhaseStateType.PreCombatMain, "the decision sits mid-phase");

        // SNAPSHOT at N1, then RESTORE with an EMPTY suffix: the first decision
        // the restored sandbox reaches must BE N1 again.
        var snap = Snapshot(sandbox1, alice.Id, ctx1);
        var snapHash = StateHash(snap.LivePlayers);

        var (restored, restoredSandbox, restoredCtx) = TrackedAdvance(sim, snap, Array.Empty<SimMove>());

        Fingerprint(restored).Should().Be(Fingerprint(n1));
        StateHash(restoredSandbox.State.Players).Should().Be(snapHash,
            "no state may mutate between resume and the re-reached decision");
        StateHash(restoredSandbox.State.Players).Should().Be(StateHash(sandbox1.State.Players));
        restoredCtx.TurnNumber.Should().Be(ctx1.TurnNumber);
        restoredCtx.Phase.Should().Be(ctx1.Phase);
    }

    /// <summary>
    /// Suffix-advance equivalence: restoring N1's snapshot and replaying ONLY
    /// the next move reaches the SAME decision and SAME state as the full
    /// root-replay of the whole path. This is the exact <c>AdvanceFrom</c>
    /// contract Task 2 productizes. The suffix here (a cast after a land play)
    /// is consumed at the SAME window by both drives — see
    /// <see cref="CastThenSorcerySuffix_FullReplayMatchesRestore_AfterAlignment"/>
    /// for the path shape where the full replay used to misconsume (BREAK #4,
    /// fixed).
    /// </summary>
    [Fact(Timeout = 120_000)]
    public async Task SuffixAdvance_FromSnapshot_EqualsFullRootReplay()
    {
        await Task.Yield();

        // 1 Mountain on board; hand: Mountain + Bolt. Path plays the land,
        // suffix casts the bolt — both windows are sorcery-speed, empty-stack,
        // so script consumption aligns with the captured decisions.
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

        var (n0, _, _) = TrackedAdvance(sim, root, Array.Empty<SimMove>());
        var playLand = MoveByKey(n0, "Land:Mountain");

        var (n1, sandbox1, ctx1) = TrackedAdvance(sim, root, new[] { playLand });
        var castBolt = MoveByKey(n1, "Cast:Lightning Bolt");
        var snap = Snapshot(sandbox1, alice.Id, ctx1);

        // FULL root replay: clone root, resume, replay BOTH moves.
        var (full, fullSandbox, fullCtx) = TrackedAdvance(sim, root, new[] { playLand, castBolt });

        // RESTORE: clone snapshot, resume at N1's phase, replay ONLY the suffix.
        var (rest, restSandbox, restCtx) = TrackedAdvance(sim, snap, new[] { castBolt });

        Fingerprint(rest).Should().Be(Fingerprint(full));
        StateHash(restSandbox.State.Players).Should().Be(StateHash(fullSandbox.State.Players));
        restCtx.TurnNumber.Should().Be(fullCtx.TurnNumber);
        restCtx.Phase.Should().Be(fullCtx.Phase);
    }

    /// <summary>
    /// BREAK #4 — FIXED (the alignment prerequisite this spike demanded): in a
    /// [cast, cast-sorcery] path, the full root-replay used to consume the
    /// second scripted move at the MID-STACK priority re-ask that follows the
    /// first cast — where a sorcery is illegal, so <c>PriorityLoop</c> rejected
    /// it and "treated it as a pass": the move was silently WASTED, divergent
    /// from the restore (which replays the suffix at the CAPTURED empty-stack
    /// decision, where it legally resolves).
    ///
    /// <para><see cref="SearchAgent"/> script consumption is now aligned to
    /// substantive windows (consume only where capture mode would pause — see
    /// <c>ScriptWindowAlignmentTests</c>). The new expectation is the provably
    /// correct one: the tree's node model pauses at the post-resolution
    /// empty-stack window, so BOTH the full replay and the suffix-restore
    /// consume the spike there and BOTH resolve it. Full replay == restore —
    /// exactly the per-iteration equivalence Task 3's gate requires.</para>
    /// </summary>
    [Fact(Timeout = 120_000)]
    public async Task CastThenSorcerySuffix_FullReplayMatchesRestore_AfterAlignment()
    {
        await Task.Yield();

        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 30);
        AddMountains(alice, 2);
        AddToHand(alice, "Lightning Bolt");
        AddToHand(alice, "Lava Spike");
        PadLibrary(alice);
        PadLibrary(bob);

        var root = SimState.Capture(
            new[] { alice, bob }, activePlayer: alice, turnNumber: 3,
            phase: PhaseStateType.PreCombatMain, searchedSeat: alice);
        var sim = NewSim();

        var (n0, _, _) = TrackedAdvance(sim, root, Array.Empty<SimMove>());
        var castBolt = MoveByKey(n0, "Cast:Lightning Bolt");

        var (n1, sandbox1, ctx1) = TrackedAdvance(sim, root, new[] { castBolt });
        var castSpike = MoveByKey(n1, "Cast:Lava Spike");
        var snap = Snapshot(sandbox1, alice.Id, ctx1);

        var (full, fullSandbox, _) = TrackedAdvance(sim, root, new[] { castBolt, castSpike });
        var (rest, restSandbox, _) = TrackedAdvance(sim, snap, new[] { castSpike });

        // The full replay no longer wastes the spike: it is consumed at the
        // post-resolution empty-stack window (the captured decision) and
        // resolves — Bob takes bolt (3) + spike (3).
        fullSandbox.State.Players.First(p => p.Id == bob.Id).LifeTotal.Should().Be(24,
            "the scripted sorcery must resolve, not be misconsumed at the mid-stack re-ask");
        full.LegalMoves.Select(m => m.Key).Should().NotContain("Cast:Lava Spike",
            "the path's child decision must not re-offer the spell the path cast");

        // The restore consumed the suffix at the same captured decision:
        // full replay and restore now agree (Task 3 equivalence precondition).
        restSandbox.State.Players.First(p => p.Id == bob.Id).LifeTotal.Should().Be(24);
        Fingerprint(rest).Should().Be(Fingerprint(full));
        StateHash(restSandbox.State.Players).Should().Be(StateHash(fullSandbox.State.Players));
    }

    /// <summary>
    /// Combat-step decision (DeclareAttackers) after a path that crossed the
    /// main→combat phase boundary (and used the turn's land drop BEFORE the
    /// snapshot phase): restores identically, and the suffix drive from it —
    /// which crosses a TURN boundary including the opponent's whole inline
    /// turn — matches the full root replay.
    /// </summary>
    [Fact(Timeout = 120_000)]
    public async Task CombatAttackDecision_AndCrossTurnSuffix_RestoreFaithfully()
    {
        await Task.Yield();

        // Alice: a ready bear + exactly one land in hand (played on the path —
        // hand is empty after it, so the forgotten-land-drop class can't skew
        // this board). Bob: empty board.
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

        var (n0, _, _) = TrackedAdvance(sim, root, Array.Empty<SimMove>());
        var playLand = MoveByKey(n0, "Land:Mountain");

        // N1 = DeclareAttackers — the path crossed main → combat.
        var (n1, sandbox1, ctx1) = TrackedAdvance(sim, root, new[] { playLand });
        n1.Kind.Should().Be(SimDecisionKind.DeclareAttackers);
        ctx1.Phase.Should().Be(PhaseStateType.Combat);

        var snap = Snapshot(sandbox1, alice.Id, ctx1);

        // Restore with empty suffix re-reaches the attack decision identically.
        var (restored, restoredSandbox, _) = TrackedAdvance(sim, snap, Array.Empty<SimMove>());
        Fingerprint(restored).Should().Be(Fingerprint(n1));
        StateHash(restoredSandbox.State.Players).Should().Be(StateHash(sandbox1.State.Players));

        // Suffix drive: attack → combat damage → opponent's whole inline turn →
        // Alice's next decision (turn 5). Full replay vs restore must agree.
        var attack = MoveByKey(n1, "Attack:{Grizzly Bears}");

        var (full, fullSandbox, fullCtx) = TrackedAdvance(sim, root, new[] { playLand, attack });
        var (rest, restSandbox, restCtx) = TrackedAdvance(sim, snap, new[] { attack });

        Fingerprint(rest).Should().Be(Fingerprint(full));
        StateHash(restSandbox.State.Players).Should().Be(StateHash(fullSandbox.State.Players));
        restCtx.TurnNumber.Should().Be(fullCtx.TurnNumber);
        restCtx.Phase.Should().Be(fullCtx.Phase);
        fullCtx.TurnNumber.Should().BeGreaterThan(3, "the suffix drive crosses a turn boundary");
    }

    /// <summary>
    /// Determinized world: the root is opted into determinization (opponent's
    /// hidden zones resampled into the materialized world base). A node snapshot
    /// taken inside that world restores identically and its suffix drive matches
    /// the full per-world replay.
    /// </summary>
    [Fact(Timeout = 120_000)]
    public async Task DeterminizedWorld_SnapshotRestore_EqualsFullReplay()
    {
        await Task.Yield();

        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 30);
        AddMountains(alice, 1);
        AddLandToHand(alice);
        AddToHand(alice, "Lightning Bolt");
        PadLibrary(alice);
        // Bob's hidden zones get RESAMPLED from his decklist in this world.
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

        var (n0, _, _) = TrackedAdvance(sim, root, Array.Empty<SimMove>());
        var playLand = MoveByKey(n0, "Land:Mountain");

        var (n1, sandbox1, ctx1) = TrackedAdvance(sim, root, new[] { playLand });
        n1.IsTerminal.Should().BeFalse();
        var snap = Snapshot(sandbox1, alice.Id, ctx1);

        // Empty-suffix restore re-reaches N1. NOTE: the snapshot SimState is
        // perfect-info by construction — the world is already materialized in
        // the frozen players, exactly what a per-world node cache would hold.
        var (restored, restoredSandbox, _) = TrackedAdvance(sim, snap, Array.Empty<SimMove>());
        Fingerprint(restored).Should().Be(Fingerprint(n1));
        StateHash(restoredSandbox.State.Players).Should().Be(StateHash(sandbox1.State.Players));

        // Suffix equivalence inside the world (aligned suffix — see BREAK #4).
        var castBolt = MoveByKey(n1, "Cast:Lightning Bolt");
        var (full, fullSandbox, _) = TrackedAdvance(sim, root, new[] { playLand, castBolt });
        var (rest, restSandbox, _) = TrackedAdvance(sim, snap, new[] { castBolt });

        Fingerprint(rest).Should().Be(Fingerprint(full));
        StateHash(restSandbox.State.Players).Should().Be(StateHash(fullSandbox.State.Players));
    }

    // ════════════════════════════════════════════════════════════════════════
    // FIDELITY — structural breaks (findings; each test PASSES by pinning the
    // divergence)
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// BREAK #1 — the Stack subsystem. A node whose decision has a spell ON the
    /// stack cannot be restored from a players-only snapshot: the Spell object
    /// lives on the sandbox's Stack (not in any player zone), so the restored
    /// sandbox starts with an EMPTY stack and the spell's card sits orphaned in
    /// the stack ZONE — it never resolves. The full replay deals 6 to Bob; the
    /// restore deals only 3.
    ///
    /// <para><b>Workaround that sidesteps it:</b> only cache nodes whose
    /// decision has an empty stack (CR 500.2: phases/steps only END with an
    /// empty stack, and post-resolution priority windows are empty-stack too,
    /// so such nodes are plentiful). Stack-laden nodes fall back to the nearest
    /// empty-stack ancestor and are re-reached by suffix replay.</para>
    /// </summary>
    [Fact(Timeout = 120_000)]
    public async Task StackLadenDecision_RestoreLosesTheStack_Divergence()
    {
        await Task.Yield();

        // Two bolts: the second is castable while the first is on the stack,
        // so N1 pauses with a NON-empty stack.
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

        var (n0, _, _) = TrackedAdvance(sim, root, Array.Empty<SimMove>());
        var castBolt1 = MoveByKey(n0, "Cast:Lightning Bolt");

        var (n1, sandbox1, ctx1) = TrackedAdvance(sim, root, new[] { castBolt1 });
        // Bolt #1 is ON the stack at this decision (its card is in the stack zone).
        alice.Id.Should().NotBeEmpty();
        var aliceAtN1 = sandbox1.State.Players.First(p => p.Id == alice.Id);
        aliceAtN1.Zones.GetZone(ZoneType.Stack).GetCards()
            .Should().ContainSingle(c => c.Name == "Lightning Bolt",
                "the first bolt must be on the stack at the captured decision");

        var castBolt2 = MoveByKey(n1, "Cast:Lightning Bolt");
        var snap = Snapshot(sandbox1, alice.Id, ctx1);

        var (_, fullSandbox, _) = TrackedAdvance(sim, root, new[] { castBolt1, castBolt2 });
        var (_, restSandbox, _) = TrackedAdvance(sim, snap, new[] { castBolt2 });

        var fullBob = fullSandbox.State.Players.First(p => p.Id == bob.Id);
        var restBob = restSandbox.State.Players.First(p => p.Id == bob.Id);
        var restAlice = restSandbox.State.Players.First(p => p.Id == alice.Id);

        // FINDING: the restore lost bolt #1 — 3 damage short, orphan card in the
        // stack zone. (If this ever starts matching, the stack break is gone —
        // re-evaluate the caching policy.)
        fullBob.LifeTotal.Should().Be(24, "full replay resolves BOTH bolts");
        restBob.LifeTotal.Should().Be(27, "the restored sandbox lost the on-stack bolt");
        restAlice.Zones.GetZone(ZoneType.Stack).GetCards()
            .Should().ContainSingle(c => c.Name == "Lightning Bolt",
                "the lost bolt's card stays orphaned in the stack zone");
        StateHash(restSandbox.State.Players).Should().NotBe(StateHash(fullSandbox.State.Players));
    }

    /// <summary>
    /// BREAK #2 — <c>LandDropTracker</c>. The per-turn land-drop tally lives on
    /// the DRIVER (fresh per sandbox), not on the players. A snapshot taken
    /// after the path played a land IN THE SNAPSHOT'S TURN restores with a
    /// fresh tracker, so the restored decision re-offers the land drop the
    /// original decision had already consumed.
    ///
    /// <para><b>Workaround:</b> the resume context must carry per-seat
    /// land-drops-used and seed the restored driver's tracker (small additive
    /// seam in <c>SandboxGame.From</c> / <c>TurnDriver</c>) — Task 2 scope.</para>
    /// </summary>
    [Fact(Timeout = 120_000)]
    public async Task LandDropUsedBeforeSnapshot_RestoreReoffersLand_Divergence()
    {
        await Task.Yield();

        // Hand: TWO mountains + a bolt. Path plays mountain #1; at N1 the land
        // drop is used so mountain #2 is NOT offered. The restore forgets that.
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

        var (n0, _, _) = TrackedAdvance(sim, root, Array.Empty<SimMove>());
        var playLand = MoveByKey(n0, "Land:Mountain");

        var (n1, sandbox1, ctx1) = TrackedAdvance(sim, root, new[] { playLand });
        n1.LegalMoves.Select(m => m.Key).Should().NotContain("Land:Mountain",
            "the original decision already consumed the turn's land drop");

        var snap = Snapshot(sandbox1, alice.Id, ctx1);
        var (restored, _, _) = TrackedAdvance(sim, snap, Array.Empty<SimMove>());

        // FINDING: the restored decision re-offers the land drop.
        restored.LegalMoves.Select(m => m.Key).Should().Contain("Land:Mountain",
            "the restored sandbox's fresh LandDropTracker forgot the consumed drop");
        Fingerprint(restored).Should().NotBe(Fingerprint(n1));
    }

    /// <summary>
    /// BREAK #3 — mid-combat sub-state. A DeclareBlockers node sits AFTER the
    /// opponent declared attackers: the attacker is tapped (CR 508.1f) in the
    /// snapshot, but the attack declaration itself lives in the sandbox's
    /// CombatFlow. Resuming at the Combat phase start re-runs declare-attackers
    /// — and the tapped attacker can no longer attack, so the block decision is
    /// never re-reached.
    ///
    /// <para><b>Workaround:</b> never cache mid-combat nodes; restore them from
    /// the nearest pre-combat ancestor — the deterministic opponent re-declares
    /// the same attack during the suffix replay (exactly what full root-replay
    /// relies on today).</para>
    /// </summary>
    [Fact(Timeout = 120_000)]
    public async Task DeclareBlockersNode_PostAttackSnapshot_CannotRestore_Divergence()
    {
        await Task.Yield();

        // Alice: a ready bear (potential blocker), nothing castable.
        // Bob: a big ready attacker. Path: Alice declines her own attack; the
        // drive crosses into Bob's turn where his 5/5 attacks → blocks decision.
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

        var (n0, _, _) = TrackedAdvance(sim, root, Array.Empty<SimMove>());
        n0.Kind.Should().Be(SimDecisionKind.DeclareAttackers);
        var noAttack = n0.LegalMoves.First(m => m.IsEmptyAttack);

        var (n1, sandbox1, ctx1) = TrackedAdvance(sim, root, new[] { noAttack });
        n1.Kind.Should().Be(SimDecisionKind.DeclareBlockers,
            "the sandbox opponent must attack with his 5/5 into a 2/2 board");
        ctx1.Phase.Should().Be(PhaseStateType.Combat);
        ctx1.ActivePlayerId.Should().Be(bob.Id);

        // The attacker is tapped in the snapshot (CR 508.1f) — but the attack
        // DECLARATION lives in CombatFlow, which the snapshot cannot carry.
        var bobAtN1 = sandbox1.State.Players.First(p => p.Id == bob.Id);
        bobAtN1.Zones.Battlefield.GetCards().OfType<Creature>()
            .First(c => c.Name == "Craw Wurm").IsTapped.Should().BeTrue();

        var snap = Snapshot(sandbox1, alice.Id, ctx1);
        var (restored, _, _) = TrackedAdvance(sim, snap, Array.Empty<SimMove>());

        // FINDING: the restored drive never re-reaches the blocks decision —
        // the tapped 5/5 cannot be re-declared, combat fizzles, and the next
        // substantive decision is something else entirely.
        Fingerprint(restored).Should().NotBe(Fingerprint(n1),
            "mid-combat sub-state (the declared attack) is lost by a players-only snapshot");
    }

    // ════════════════════════════════════════════════════════════════════════
    // COST — restore (clone + resume + 1-move suffix) vs full root replay at
    // tree-typical depths. Run in Release for the numbers that gate GO/NO-GO.
    // ════════════════════════════════════════════════════════════════════════

    [Fact(Timeout = 300_000)]
    public async Task Cost_RestoreVsFullReplay_AtDepths_2_4_6()
    {
        await Task.Yield();

        // A realistic 6-decision tree path starting at turn 3 in the pre-combat
        // main: land + bolt in one window-chain, the bear attack, then whatever
        // the tree surfaces next (later-turn land windows / attacks). The path
        // is authored ADAPTIVELY from each reached decision — exactly how the
        // tree builds paths — because beyond a pass-only window today's replay
        // misconsumes scripted priority moves (BREAK #4); the engine still
        // drives the same game span, which is what the cost gate measures.
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 30);
        AddReadyCreature(alice, "Grizzly Bears", "{1}{G}", 2, 2);
        AddMountains(alice, 1);
        AddLandToHand(alice);
        AddToHand(alice, "Lightning Bolt");
        // Later-turn draws must not open land/cast windows (BREAK #4 would eat
        // the scripted moves) — pad with unaffordable 6-drops so each later turn
        // has exactly one substantive decision: the attack ask. The path then
        // deepens turn-by-turn: t3 (land, bolt, attack), t5/t7/t9 (attacks).
        PadLibraryWithUncastables(alice);
        PadLibrary(bob);

        var root = SimState.Capture(
            new[] { alice, bob }, activePlayer: alice, turnNumber: 3,
            phase: PhaseStateType.PreCombatMain, searchedSeat: alice);
        var sim = NewSim();

        // ── Author the path move-by-move (the way the tree does) ────────────
        var path = new List<SimMove>();
        var snapshots = new List<SimState>(); // snapshot of N_d after d moves
        var ctxs = new List<ResumeCtx>();

        var (decision, sandbox, ctx) = TrackedAdvance(sim, root, path);
        snapshots.Add(Snapshot(sandbox, alice.Id, ctx));
        ctxs.Add(ctx);

        // Adaptive move picker, mirroring tree behaviour: prefer an attack,
        // then a land, then a cast, then any non-pass move, then pass.
        static SimMove PickMove(SimDecision d) =>
            d.LegalMoves.FirstOrDefault(m => m.CombatPlan is { } p && p.Attackers.Count > 0)
            ?? d.LegalMoves.FirstOrDefault(m => m.Key.StartsWith("Land:", StringComparison.Ordinal))
            ?? d.LegalMoves.FirstOrDefault(m => m.Key.StartsWith("Cast:", StringComparison.Ordinal))
            ?? d.LegalMoves.FirstOrDefault(m => !m.IsPass && !m.IsEmptyAttack)
            ?? d.LegalMoves[0];

        for (var depth = 1; depth <= 6; depth++)
        {
            path.Add(PickMove(decision));
            (decision, sandbox, ctx) = TrackedAdvance(sim, root, path);
            decision.IsTerminal.Should().BeFalse($"the cost path must stay live at depth {depth}");
            snapshots.Add(Snapshot(sandbox, alice.Id, ctx));
            ctxs.Add(ctx);
        }

        _output.WriteLine("path: " + string.Join(" -> ", path.Select(m => m.Key)));

        // ── Measure ──────────────────────────────────────────────────────────
        static double MedianMs(Action act)
        {
            act(); // warmup (JIT + caches)
            var times = new List<double>(9);
            for (var i = 0; i < 9; i++)
            {
                var sw = Stopwatch.StartNew();
                act();
                sw.Stop();
                times.Add(sw.Elapsed.TotalMilliseconds);
            }
            times.Sort();
            return times[times.Count / 2];
        }

        _output.WriteLine($"build: {(IsRelease() ? "Release" : "DEBUG — rerun in Release for gate numbers")}");
        _output.WriteLine("depth | full replay ms | restore ms (clone+resume+1-move suffix) | node turn/phase");

        foreach (var depth in new[] { 2, 4, 6 })
        {
            var fullPath = path.Take(depth).ToArray();
            var suffix = new[] { path[depth - 1] };
            var snapState = snapshots[depth - 1];

            var fullMs = MedianMs(() => sim.Advance(root, fullPath));
            var restoreMs = MedianMs(() => sim.Advance(snapState, suffix));

            _output.WriteLine(
                $"  {depth}   | {fullMs,8:F2}      | {restoreMs,8:F2} | t{ctxs[depth].TurnNumber} {ctxs[depth].Phase}");
        }

        // Snapshot-capture cost (paid once per expanded node in Task 2+).
        var capture = MedianMs(() => GameStateCloner.Clone(sandbox.State.Players));
        _output.WriteLine($"snapshot capture (GameStateCloner.Clone): {capture:F2} ms");
    }

    private static bool IsRelease()
    {
#if DEBUG
        return false;
#else
        return true;
#endif
    }
}
