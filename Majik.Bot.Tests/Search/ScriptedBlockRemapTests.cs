using FluentAssertions;
using Majik.Bot.Evaluation;
using Majik.Bot.Search;
using Majik.Core.Cards;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Simulation;
using Majik.Core.StateMachine;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Bot.Tests.Search;

/// <summary>
/// Sampled-card fidelity — scripted-block remap.
///
/// <para>
/// The MCTS search replays scripted move paths into fresh sandboxes. Attacks
/// were already remapped (<see cref="SearchAgent.DeclareAttackersAsync"/> →
/// <c>RemapCombatPlan</c>, InstanceId-based), but scripted <see cref="BlockPlan"/>s
/// carried creature objects from a DIFFERENT sandbox. The engine groups blockers
/// by attacker REFERENCE (<c>CombatFlow.ExecuteCombat</c>:
/// <c>GroupBy(b =&gt; b.Attacker)</c>), so foreign objects never match and the
/// block silently no-ops — in-tree block moves were byte-identical to no-block.
/// </para>
///
/// <para>
/// These tests pin <see cref="SearchAgent.RemapBlockPlan"/>: per (Blocker,
/// Attacker) pair, BOTH ends are resolved against THIS sandbox by
/// <see cref="Card.InstanceId"/> (stable across <see cref="GameStateCloner"/>
/// clones); unmappable pairs are dropped (same graceful degradation as the
/// attack remap); all-dropped yields <see cref="BlockPlan.None"/>. The
/// end-to-end test inverts the original probe: two sim paths differing only in
/// a block move must now produce DIFFERENT outcomes.
/// </para>
/// </summary>
public class ScriptedBlockRemapTests
{
    // ── Board-builder helpers ─────────────────────────────────────────────────

    private static Creature AddReadyCreature(Player owner, string name, int power, int toughness)
    {
        var c = new Creature(name, manaCost: string.Empty, power: power, toughness: toughness);
        c.ChangeOwner(owner);
        owner.Zones.Battlefield.AddCard(c);
        c.ClearSummoningSickness();
        return c;
    }

    private static void PadLibraries(Player a, Player b, int count = 15)
    {
        for (int i = 0; i < count; i++)
        {
            var fa = new Land("Forest");
            fa.ChangeOwner(a);
            a.Zones.GetZone(ZoneType.Library).AddCard(fa);

            var fb = new Land("Forest");
            fb.ChangeOwner(b);
            b.Zones.GetZone(ZoneType.Library).AddCard(fb);
        }
    }

    /// <summary>Finds the cloned counterpart of <paramref name="original"/> on a cloned battlefield.</summary>
    private static Creature CloneOf(ClonedGame clone, Player owner, Creature original) =>
        clone.Players.First(p => p.Id == owner.Id)
            .Zones.Battlefield.GetCards()
            .OfType<Creature>()
            .Single(c => c.InstanceId == original.InstanceId);

    // ── Unit: remap resolves by InstanceId against THIS sandbox ──────────────

    [Fact]
    public void ScriptedBlockPlan_RemapsToThisSandbox_ByInstanceId()
    {
        var opp = new Player("Opp", 20);
        var bot = new Player("Bot", 20);
        var attacker = AddReadyCreature(opp, "Attacker33", 3, 3);
        var blocker = AddReadyCreature(bot, "Blocker23", 2, 3);

        // Clone TWICE: same InstanceIds, different object identities.
        var sandbox1 = GameStateCloner.Clone(new[] { opp, bot });
        var sandbox2 = GameStateCloner.Clone(new[] { opp, bot });

        var s1Attacker = CloneOf(sandbox1, opp, attacker);
        var s1Blocker = CloneOf(sandbox1, bot, blocker);
        var s2Attacker = CloneOf(sandbox2, opp, attacker);
        var s2Blocker = CloneOf(sandbox2, bot, blocker);

        // Sanity: distinct objects across the two sandboxes.
        ReferenceEquals(s1Attacker, s2Attacker).Should().BeFalse();
        ReferenceEquals(s1Blocker, s2Blocker).Should().BeFalse();

        // Scripted plan built from SANDBOX1's creatures, remapped against SANDBOX2.
        var scripted = new BlockPlan(new[] { new BlockerDeclaration(s1Blocker, s1Attacker) });

        var remapped = SearchAgent.RemapBlockPlan(
            scripted,
            attackers: new[] { s2Attacker },
            eligibleBlockers: new[] { s2Blocker });

        remapped.Blockers.Should().HaveCount(1);
        remapped.Blockers[0].Blocker.Should().BeSameAs(s2Blocker,
            because: "the remap must return THIS sandbox's blocker object, not the foreign one");
        remapped.Blockers[0].Attacker.Should().BeSameAs(s2Attacker,
            because: "the remap must return THIS sandbox's attacker object, not the foreign one");
        remapped.Blockers[0].Blocker.Should().NotBeSameAs(s1Blocker);
        remapped.Blockers[0].Attacker.Should().NotBeSameAs(s1Attacker);
    }

    [Fact]
    public void UnmappableBlockPair_IsDropped_NotThrown()
    {
        var opp = new Player("Opp", 20);
        var bot = new Player("Bot", 20);
        var attacker = AddReadyCreature(opp, "Attacker33", 3, 3);
        var blocker = AddReadyCreature(bot, "Blocker23", 2, 3);

        var sandbox2 = GameStateCloner.Clone(new[] { opp, bot });
        var s2Attacker = CloneOf(sandbox2, opp, attacker);
        var s2Blocker = CloneOf(sandbox2, bot, blocker);

        // A "stranger" blocker whose InstanceId exists in no sandbox.
        var stranger = new Creature("Stranger", manaCost: string.Empty, power: 1, toughness: 1);

        var scripted = new BlockPlan(new[]
        {
            new BlockerDeclaration(stranger, attacker), // unmappable blocker → dropped
            new BlockerDeclaration(blocker, attacker),  // mappable → kept (remapped)
        });

        var act = () => SearchAgent.RemapBlockPlan(
            scripted,
            attackers: new[] { s2Attacker },
            eligibleBlockers: new[] { s2Blocker });

        var remapped = act.Should().NotThrow().Subject;
        remapped.Blockers.Should().HaveCount(1,
            because: "the unmappable pair is dropped while the valid pair survives");
        remapped.Blockers[0].Blocker.Should().BeSameAs(s2Blocker);
        remapped.Blockers[0].Attacker.Should().BeSameAs(s2Attacker);
    }

    [Fact]
    public void AllUnmappable_YieldsNoBlock()
    {
        var opp = new Player("Opp", 20);
        var bot = new Player("Bot", 20);
        var attacker = AddReadyCreature(opp, "Attacker33", 3, 3);
        var blocker = AddReadyCreature(bot, "Blocker23", 2, 3);

        var sandbox2 = GameStateCloner.Clone(new[] { opp, bot });
        var s2Attacker = CloneOf(sandbox2, opp, attacker);
        var s2Blocker = CloneOf(sandbox2, bot, blocker);

        var strangerBlocker = new Creature("StrangerB", manaCost: string.Empty, power: 1, toughness: 1);
        var strangerAttacker = new Creature("StrangerA", manaCost: string.Empty, power: 2, toughness: 2);

        var scripted = new BlockPlan(new[]
        {
            new BlockerDeclaration(strangerBlocker, s2Attacker), // foreign blocker
            new BlockerDeclaration(s2Blocker, strangerAttacker), // foreign attacker
        });

        var act = () => SearchAgent.RemapBlockPlan(
            scripted,
            attackers: new[] { s2Attacker },
            eligibleBlockers: new[] { s2Blocker });

        var remapped = act.Should().NotThrow().Subject;
        remapped.Blockers.Should().BeEmpty(
            because: "every pair was unmappable, so the plan degrades to no-block");
    }

    // ── End-to-end: in-tree block moves actually replay (inverted probe) ─────

    /// <summary>
    /// The payoff test — inverts the original probe that proved block lines were
    /// byte-identical to no-block.
    ///
    /// <para>
    /// Setup: opponent (active) has a ready 4/4; the searched bot is at 3 life
    /// with a lone 1/1. The sandbox opponent (heuristic) swings the 4/4 — lethal
    /// if unblocked. <c>Advance</c> captures the bot's DeclareBlockers decision
    /// in ONE sandbox; each <c>Rollout</c> replays the chosen move into a FRESH
    /// sandbox, so the scripted BlockPlan crosses a clone boundary — exactly the
    /// path that used to no-op.
    /// </para>
    ///
    /// <para>
    /// No-block: bot takes 4 ≥ 3 life → dies → terminal loss value.
    /// Chump block: 1/1 eats the 4/4, bot survives at 3 → board-eval leaf,
    /// far above the loss value. If the scripted block still no-opped, both
    /// rollouts would be byte-identical (fixed sandbox seed) and the values
    /// equal — the assertion below would fail.
    /// </para>
    /// </summary>
    [Fact]
    public void InTreeBlockMove_ReplaysIntoFreshSandbox_OutcomeDiffersFromNoBlock()
    {
        var opp = new Player("Opp", 20);  // active player, attacking seat
        var bot = new Player("Bot", 3);   // searched seat — 4/4 hit is lethal

        AddReadyCreature(opp, "Brute44", 4, 4);
        var chump = AddReadyCreature(bot, "Chump11", 1, 1);

        PadLibraries(bot, opp);

        var root = SimState.Capture(
            new[] { opp, bot },
            activePlayer: opp,
            turnNumber: 3,
            phase: PhaseStateType.Combat,
            searchedSeat: bot);
        var sim = new EngineSimulator(ArchetypeWeights.ForArchetype("Burn"));

        // Advance to the bot's DeclareBlockers decision (captured in sandbox #1).
        var decision = sim.Advance(root, Array.Empty<SimMove>());
        decision.IsTerminal.Should().BeFalse();
        decision.Kind.Should().Be(SimDecisionKind.DeclareBlockers,
            because: "the heuristic sandbox opponent swings its lethal 4/4, putting the searched bot to a block decision");

        // The chump-block move and the no-block move, exactly as MCTS would
        // hold them in the tree: BlockPlans referencing sandbox #1's creatures.
        var blockMove = decision.LegalMoves.First(m =>
            m.BlockPlan?.Blockers.Count == 1
            && m.BlockPlan.Blockers[0].Blocker.InstanceId == chump.InstanceId);
        var noBlockMove = decision.LegalMoves.First(m => m.BlockPlan?.Blockers.Count == 0);

        // Replay each into a FRESH sandbox (clone boundary crossed).
        var blockValue = sim.Rollout(root, new[] { blockMove }, depthTurns: 0);
        var noBlockValue = sim.Rollout(root, new[] { noBlockMove }, depthTurns: 0);

        // Inverted probe: the two paths differ ONLY in the block move, so the
        // outcomes must now genuinely differ — chumping saves the bot's life.
        blockValue.Should().NotBe(noBlockValue,
            because: "a replayed in-tree block must change the sim outcome — identical values mean the block silently no-opped");
        blockValue.Should().BeGreaterThan(noBlockValue,
            because: "chump-blocking the lethal 4/4 keeps the bot alive; taking it unblocked is a terminal loss");
    }
}
