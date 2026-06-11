using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Primitives;
using Majik.Core.Rules;
using Majik.Core.Services;
using Majik.Core.StateMachine;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.Game;

/// <summary>
/// CR 104.1 / 104.2a — "A game ends immediately when a player wins"; "A player
/// still in the game wins the game if that player's opponents have all left
/// the game. This happens immediately."
///
/// Regression for the dominant live-match crash after the MCTS bot went live:
/// in a Burn matchup two burn spells sit on the stack, the first resolves for
/// lethal, the CR 704.5a SBA sweep marks the target lost — and the priority
/// loop USED TO march on and resolve the second spell into a player who had
/// already left the game (CR 800.4a), crashing the match with
/// "Cannot lose life after losing the game" (Player.LoseLife's guard; ~150
/// call sites reach it, many not routed through the guarded
/// OracleSpellBinder.DealDamage funnel).
///
/// The fix is layered:
///   (a) PRIMARY — PriorityLoop halts after each resolution + SBA sweep when
///       at most one player remains (these tests pin the halt: the second
///       spell must still be ON the stack, unresolved), and TurnDriver stops
///       advancing phases for a finished game.
///   (b) BACKSTOP — Player.LoseLife / GainLife on an already-lost player are
///       graceful no-ops instead of throws (see PlayerTests).
/// </summary>
public class GameOverHaltsResolutionTests
{
    private readonly EventBus _bus = new();

    /// <summary>
    /// The exact live-crash shape: two burn spells on the stack, the first is
    /// lethal. After it resolves and the SBA sweep marks the defender lost,
    /// the game is over (CR 104.1 / 104.2a) — the loop must return without
    /// resolving the second spell (which would otherwise call LoseLife on a
    /// player who has left the game, CR 800.4a).
    /// </summary>
    [Fact]
    public async Task SecondBurnSpell_AfterLethalFirst_DoesNotResolve_NoCrash()
    {
        var stack = new Majik.Core.Stack.Stack(_bus);
        var zones = new ZoneService(_bus);
        var triggers = new TriggerManager(stack, _bus);
        var resolver = new StackResolver(_bus, zones);
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 3);
        var players = new List<Player> { alice, bob };
        var priority = new PriorityManager(players, stack, _bus, triggers);
        var sba = new StateBasedActions(_bus, zones, triggers);

        // Two burn spells, both Alice's, both calling Player.LoseLife
        // DIRECTLY — deliberately NOT via the (guarded)
        // OracleSpellBinder.DealDamage funnel, modelling the many factory /
        // trigger call sites that hit LoseLife straight.
        var secondCard = new Instant("Bolt (second)", "R") { Owner = alice, Zone = ZoneType.Stack };
        var secondResolved = false;
        var second = new Majik.Core.Spells.Spell(
            secondCard, alice,
            effects: new[]
            {
                Fx.Inline("3 damage to Bob", () => { secondResolved = true; bob.LoseLife(3); }),
            });

        var lethalCard = new Instant("Bolt (lethal)", "R") { Owner = alice, Zone = ZoneType.Stack };
        var lethal = new Majik.Core.Spells.Spell(
            lethalCard, alice,
            effects: new[] { Fx.Inline("3 damage to Bob", () => bob.LoseLife(3)) });

        // LIFO — push the second spell first so the lethal one resolves first.
        stack.Push(second);
        stack.Push(lethal);

        var aliceAgent = new ScriptedAgent();
        var bobAgent = new ScriptedAgent();
        for (var i = 0; i < 20; i++)
        {
            aliceAgent.QueuePriority(PriorityAction.Pass);
            bobAgent.QueuePriority(PriorityAction.Pass);
        }

        var loop = new PriorityLoop(
            players, priority, stack, resolver, zones,
            new Dictionary<Player, IPlayerAgent>
            { [alice] = aliceAgent, [bob] = bobAgent },
            () => 1, () => StepStateType.PreCombatMain,
            new LandDropTracker(),
            // CR 704.1 / 704.4 — same wiring TurnDriver uses in the live
            // engine: sweep SBAs before each priority grant and after each
            // resolution, so the lethal spell's life loss formally marks Bob
            // lost (CR 704.5a) before anything else happens.
            checkStateBasedActions: () => sba.CheckStateBasedActions(
                players,
                players.SelectMany(p => p.Zones.Battlefield.GetCards()).ToList()));

        var act = async () => await loop.RunUntilRoundEndsAsync(alice);

        await act.Should().NotThrowAsync(
            "the game ends immediately when Bob loses (CR 104.1) — nothing may " +
            "resolve into a departed player (CR 800.4a)");

        bob.HasLost.Should().BeTrue("the lethal bolt + SBA sweep ended the game");
        bob.LifeTotal.Should().Be(0, "only the lethal bolt's 3 damage applied");
        secondResolved.Should().BeFalse(
            "CR 104.1 — the game ended before the second spell could resolve");
        stack.IsEmpty.Should().BeFalse(
            "the halt is the fix: the second spell stays on the stack, unresolved, " +
            "when the game ends (CR 104.1) — it must not be popped and run");
    }

    /// <summary>
    /// Control: with a NON-lethal first spell the round proceeds normally and
    /// both spells resolve — the game-over halt fires only when the game is
    /// actually over.
    /// </summary>
    [Fact]
    public async Task BothSpells_Resolve_WhenNeitherIsLethal()
    {
        var stack = new Majik.Core.Stack.Stack(_bus);
        var zones = new ZoneService(_bus);
        var triggers = new TriggerManager(stack, _bus);
        var resolver = new StackResolver(_bus, zones);
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);
        var players = new List<Player> { alice, bob };
        var priority = new PriorityManager(players, stack, _bus, triggers);
        var sba = new StateBasedActions(_bus, zones, triggers);

        var firstCard = new Instant("Bolt A", "R") { Owner = alice, Zone = ZoneType.Stack };
        var first = new Majik.Core.Spells.Spell(
            firstCard, alice,
            effects: new[] { Fx.Inline("3 damage to Bob", () => bob.LoseLife(3)) });
        var secondCard = new Instant("Bolt B", "R") { Owner = alice, Zone = ZoneType.Stack };
        var second = new Majik.Core.Spells.Spell(
            secondCard, alice,
            effects: new[] { Fx.Inline("3 damage to Bob", () => bob.LoseLife(3)) });

        stack.Push(first);
        stack.Push(second);

        var aliceAgent = new ScriptedAgent();
        var bobAgent = new ScriptedAgent();
        for (var i = 0; i < 20; i++)
        {
            aliceAgent.QueuePriority(PriorityAction.Pass);
            bobAgent.QueuePriority(PriorityAction.Pass);
        }

        var loop = new PriorityLoop(
            players, priority, stack, resolver, zones,
            new Dictionary<Player, IPlayerAgent>
            { [alice] = aliceAgent, [bob] = bobAgent },
            () => 1, () => StepStateType.PreCombatMain,
            new LandDropTracker(),
            checkStateBasedActions: () => sba.CheckStateBasedActions(
                players,
                players.SelectMany(p => p.Zones.Battlefield.GetCards()).ToList()));

        await loop.RunUntilRoundEndsAsync(alice);

        bob.HasLost.Should().BeFalse();
        bob.LifeTotal.Should().Be(14, "both bolts resolved normally (20 - 3 - 3)");
        stack.IsEmpty.Should().BeTrue("the round only ends when the stack is empty");
    }
}
