using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Rules;
using Majik.Core.Services;
using Majik.Core.StateMachine;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.Game;

/// <summary>
/// CR 704.4 — "Whenever a player would get priority, the game checks for any
/// of the listed [state-based action] conditions ... performs all applicable
/// state-based actions ... and then repeats this process until there are no
/// further state-based actions to be performed. Then the appropriate player
/// gets priority."
///
/// Regression coverage for the fuzz-found bug where a lethal spell resolved,
/// a player's life hit 0, but they were still offered priority and could
/// attempt to cast — driving mana into a lost player's pool
/// (Player.AddManaToPool's "Cannot add mana after losing the game" guard).
/// The fix runs SBAs before granting priority so the dead player is marked
/// lost (and the game ends) BEFORE they ever act.
/// </summary>
public class PriorityLoopSbaTests
{
    private readonly EventBus _bus = new();

    /// <summary>
    /// Direct reproduction of the fuzz crash: the active player has already lost
    /// (life at 0 from a lethal spell). Before the CR 704.4 fix, the priority
    /// loop offered that lost player priority and his queued cast attempt drove
    /// mana into a lost player's pool — Player.AddManaToPool throws
    /// "Cannot add mana after losing the game". With the fix, the SBA check ends
    /// the game before priority is granted, so the cast never runs and the loop
    /// returns cleanly for GameDriver to declare the winner.
    /// </summary>
    [Fact]
    public async Task LostActivePlayer_NotOfferedPriority_NoManaCrash()
    {
        var stack = new Majik.Core.Stack.Stack(_bus);
        var zones = new ZoneService(_bus);
        var triggers = new TriggerManager(stack, _bus);
        var resolver = new StackResolver(_bus, zones);
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);
        var priority = new PriorityManager(new List<Player> { alice, bob }, stack, _bus, triggers);

        // Bob took lethal — he is at 0 life. Per CR 704.5a loss is a STATE-
        // BASED action, so LoseLife does NOT eagerly flag him lost; the SBA
        // sweep that PriorityLoop runs before granting priority is what marks
        // him. He is the ACTIVE player, so the round would offer him priority
        // first if SBAs weren't checked first.
        bob.LoseLife(20);
        bob.LifeTotal.Should().Be(0);
        bob.HasLost.Should().BeFalse();

        var sba = new StateBasedActions(_bus, zones, triggers);

        // Bob's agent is scripted to CAST when offered priority — exactly the
        // fuzz path. The cast dispatcher mirrors the live mana-payment step that
        // crashed: AddManaToPool on a lost player throws. If the fix works, this
        // dispatcher is never reached.
        var bobCard = new Instant("Bolt", "R") { Owner = bob, Zone = ZoneType.Hand };
        bob.Zones.Hand.AddCard(bobCard);
        var bobAgent = new ScriptedAgent();
        for (var i = 0; i < 10; i++)
            bobAgent.QueuePriority(new PriorityAction.CastSpell(bobCard, System.Array.Empty<object>()));

        var aliceAgent = new ScriptedAgent();
        for (var i = 0; i < 10; i++) aliceAgent.QueuePriority(PriorityAction.Pass);

        var castDispatched = false;
        Func<Player, PriorityAction.CastSpell, GameContext, System.Threading.Tasks.Task<bool>> castDispatcher =
            (actor, cast, ctx) =>
            {
                castDispatched = true;
                actor.AddManaToPool(Majik.Core.ValueObjects.ManaCost.Parse("R"));
                return System.Threading.Tasks.Task.FromResult(true);
            };

        var loop = new PriorityLoop(
            new[] { alice, bob }, priority, stack, resolver, zones,
            new Dictionary<Player, IPlayerAgent>
            { [alice] = aliceAgent, [bob] = bobAgent },
            () => 1, () => StepStateType.PreCombatMain,
            new LandDropTracker(),
            castDispatcher: castDispatcher,
            // CR 704.4 — wire the SBA check the same way TurnDriver does in the
            // live engine (main's PriorityLoop takes an Action delegate, not the
            // StateBasedActions service directly): sweep loss/death before each
            // priority grant.
            checkStateBasedActions: () => sba.CheckStateBasedActions(
                new[] { alice, bob },
                System.Linq.Enumerable.ToList(
                    System.Linq.Enumerable.SelectMany(
                        new[] { alice, bob }, p => p.Zones.Battlefield.GetCards()))));

        // Should NOT throw — the lost player is never offered priority.
        await loop.RunUntilRoundEndsAsync(bob);

        castDispatched.Should().BeFalse("a lost player must not be offered priority (CR 704.4)");
        bob.HasLost.Should().BeTrue();
        alice.HasLost.Should().BeFalse();
    }

    /// <summary>
    /// Three players: an opponent dies to the SBA sweep but the game continues
    /// (2 alive). The dead opponent must never be offered priority; the round
    /// completes normally with the survivors passing.
    /// </summary>
    [Fact]
    public async Task DeadNonActivePlayer_Skipped_GameContinues()
    {
        var stack = new Majik.Core.Stack.Stack(_bus);
        var zones = new ZoneService(_bus);
        var triggers = new TriggerManager(stack, _bus);
        var resolver = new StackResolver(_bus, zones);
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);
        var carol = new Player("Carol", 20);
        var players = new List<Player> { alice, bob, carol };
        var priority = new PriorityManager(players, stack, _bus, triggers);

        bob.LoseLife(20); // Bob is at 0 life; the SBA sweep marks him lost.

        var sba = new StateBasedActions(_bus, zones, triggers);

        var aliceAgent = new ScriptedAgent();
        for (var i = 0; i < 10; i++) aliceAgent.QueuePriority(PriorityAction.Pass);
        var carolAgent = new ScriptedAgent();
        for (var i = 0; i < 10; i++) carolAgent.QueuePriority(PriorityAction.Pass);
        // Bob's agent has nothing queued — being offered priority would throw.
        var bobAgent = new ScriptedAgent();

        var loop = new PriorityLoop(
            players.ToArray(), priority, stack, resolver, zones,
            new Dictionary<Player, IPlayerAgent>
            { [alice] = aliceAgent, [bob] = bobAgent, [carol] = carolAgent },
            () => 1, () => StepStateType.PreCombatMain,
            new LandDropTracker(),
            // CR 704.4 — same Action-delegate wiring as TurnDriver / main.
            checkStateBasedActions: () => sba.CheckStateBasedActions(
                players,
                System.Linq.Enumerable.ToList(
                    System.Linq.Enumerable.SelectMany(
                        players, p => p.Zones.Battlefield.GetCards()))));

        await loop.RunUntilRoundEndsAsync(alice);

        bob.HasLost.Should().BeTrue();
        alice.HasLost.Should().BeFalse();
        carol.HasLost.Should().BeFalse();
        stack.IsEmpty.Should().BeTrue();
    }
}
