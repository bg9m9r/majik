using FluentAssertions;
using Majik.Bot.Evaluation;
using Majik.Bot.Search;
using Majik.Core.CardData;
using Majik.Core.Cards;
using Majik.Core.Players;
using Majik.Core.StateMachine;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Bot.Tests.Search;

/// <summary>
/// BREAK-4 fix (tree-state-reuse plan, prerequisite slice): <see cref="SearchAgent"/>
/// must consume a scripted priority move only at SUBSTANTIVE windows — the same
/// windows where capture mode would pause (i.e. where
/// <see cref="LegalActionEnumerator.ForPriority"/> offers more than Pass; pass-only
/// capture decisions are auto-drained by <c>EngineSimulator.DriveToDecisionUnsafe</c>
/// and never become tree nodes).
///
/// <para>
/// Pre-fix, the agent consumed the next scripted priority move at the NEXT priority
/// ask, whatever it was:
/// <list type="bullet">
///   <item>after a cast, that is the mid-stack re-ask — a scripted sorcery there is
///     rejected by the engine (CR 117.1 sorcery-speed gate) and treated as a pass:
///     the move was silently WASTED;</item>
///   <item>after any pass-only window (e.g. the searched seat's priority asks during
///     its own combat) the queued move was likewise misconsumed.</item>
/// </list>
/// Consequence: full root-replay diverged from the tree's own node model on
/// multi-window paths — the search evaluated lines as if later moves never happened.
/// </para>
/// </summary>
public sealed class ScriptWindowAlignmentTests
{
    private static readonly EmbeddedCardRepository Repo = new();

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

    private static void AddToHand(Player p, string repoCardName)
    {
        var card = new ScryfallCardFactory(Repo).Create(repoCardName, p);
        p.Zones.Hand.AddCard(card);
    }

    private static Creature AddReadyCreature(Player p, string name, string cost, int power, int toughness)
    {
        var c = new Creature(name, cost, power, toughness);
        c.ChangeOwner(p);
        p.Zones.Battlefield.AddCard(c);
        c.ClearSummoningSickness();
        return c;
    }

    private static SimMove MoveByKey(SimDecision d, string key) =>
        d.LegalMoves.First(m => m.Key == key);

    // ── The mid-stack re-ask path ─────────────────────────────────────────────

    /// <summary>
    /// Path [cast Lightning Bolt, cast Lava Spike]: after the bolt is cast the
    /// engine re-asks priority while the bolt is still ON the stack. That window
    /// is pass-only for Alice (a sorcery is not castable mid-stack, CR 117.1),
    /// so capture mode would auto-drain it — the scripted spike must NOT be
    /// consumed there. It must be consumed at the post-resolution empty-stack
    /// window (the window the tree's own node model pauses at), where it
    /// legally resolves: BOTH spells deal damage.
    ///
    /// <para>Pre-fix this failed: the spike was consumed at the mid-stack
    /// re-ask, rejected, and wasted — Bob only took bolt damage (27) and the
    /// reached decision re-offered the very spell the path had supposedly
    /// cast.</para>
    /// </summary>
    [Fact(Timeout = 120_000)]
    public async Task CastThenSorcery_ScriptConsumedAtSubstantiveWindow_BothResolve()
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

        var (n0, _) = sim.AdvanceWithSandbox(root, Array.Empty<SimMove>());
        var castBolt = MoveByKey(n0, "Cast:Lightning Bolt");

        var (n1, _) = sim.AdvanceWithSandbox(root, new[] { castBolt });
        var castSpike = MoveByKey(n1, "Cast:Lava Spike");

        var (n2, sandbox) = sim.AdvanceWithSandbox(root, new[] { castBolt, castSpike });

        var bobAtN2 = sandbox.State.Players.First(p => p.Id == bob.Id);
        var aliceAtN2 = sandbox.State.Players.First(p => p.Id == alice.Id);

        bobAtN2.LifeTotal.Should().Be(24,
            "both the bolt (3) and the spike (3) must resolve — the scripted spike " +
            "may not be wasted at the mid-stack re-ask");
        aliceAtN2.Zones.GetZone(ZoneType.Graveyard).GetCards()
            .Select(c => c.Name).Should().Contain("Lava Spike");
        n2.IsTerminal.Should().BeFalse();
        n2.LegalMoves.Select(m => m.Key).Should().NotContain("Cast:Lava Spike",
            "the path's own child decision must not re-offer the spell the path cast");
    }

    // ── The pass-only-window path ─────────────────────────────────────────────

    /// <summary>
    /// Path [Pass (pre-combat main), attack with the bear, cast Lava Spike]:
    /// after the attack is declared, Alice receives several pass-only priority
    /// asks inside her own combat (a sorcery is not castable there). The queued
    /// scripted spike must ride THROUGH those windows unconsumed and be spent at
    /// the post-combat main window — the next window capture mode would pause at.
    ///
    /// <para>Pre-fix this failed: the spike was consumed at the first combat
    /// priority ask, rejected by the CR 117.1 sorcery-speed gate, and wasted —
    /// Bob only took the bear's 2 (28) and the spike stayed in hand.</para>
    /// </summary>
    [Fact(Timeout = 120_000)]
    public async Task ScriptedSorcery_RidesThroughPassOnlyCombatWindows_ResolvesPostCombat()
    {
        await Task.Yield();

        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 30);
        AddMountains(alice, 2);
        AddReadyCreature(alice, "Grizzly Bears", "{1}{G}", 2, 2);
        AddToHand(alice, "Lava Spike");
        PadLibrary(alice);
        PadLibrary(bob);

        var root = SimState.Capture(
            new[] { alice, bob }, activePlayer: alice, turnNumber: 3,
            phase: PhaseStateType.PreCombatMain, searchedSeat: alice);
        var sim = NewSim();

        // N0: the pre-combat main priority window (substantive: spike castable).
        var (n0, _) = sim.AdvanceWithSandbox(root, Array.Empty<SimMove>());
        n0.Kind.Should().Be(SimDecisionKind.Priority);
        var pass = n0.LegalMoves.First(m => m.IsPass);

        // N1: the attack ask.
        var (n1, _) = sim.AdvanceWithSandbox(root, new[] { pass });
        n1.Kind.Should().Be(SimDecisionKind.DeclareAttackers);
        var attack = MoveByKey(n1, "Attack:{Grizzly Bears}");

        // N2: the post-combat main window where the spike is castable again.
        var (n2, _) = sim.AdvanceWithSandbox(root, new[] { pass, attack });
        n2.Kind.Should().Be(SimDecisionKind.Priority);
        n2.LegalMoves.Select(m => m.Key).Should().Contain("Cast:Lava Spike",
            "the spike must still be in hand at the post-combat window — not " +
            "wasted inside combat");
        var castSpike = MoveByKey(n2, "Cast:Lava Spike");

        // Full path: pass, attack (bear connects for 2), spike resolves for 3.
        var (_, sandbox) = sim.AdvanceWithSandbox(root, new[] { pass, attack, castSpike });

        var bobAtEnd = sandbox.State.Players.First(p => p.Id == bob.Id);
        var aliceAtEnd = sandbox.State.Players.First(p => p.Id == alice.Id);

        bobAtEnd.LifeTotal.Should().Be(25,
            "bear (2, unblocked) + Lava Spike (3) must both connect — the scripted " +
            "spike may not be consumed at a pass-only combat window");
        aliceAtEnd.Zones.GetZone(ZoneType.Graveyard).GetCards()
            .Select(c => c.Name).Should().Contain("Lava Spike");
    }
}
