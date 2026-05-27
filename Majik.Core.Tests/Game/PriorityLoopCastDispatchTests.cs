using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.StateMachine;
using Majik.Core.Zones;
using Xunit;

public class PriorityLoopCastDispatchTests
{
    private readonly EventBus _bus = new();

    [Fact]
    public async Task CastSpellAction_RoutedToDispatcher()
    {
        var stack = new Majik.Core.Stack.Stack(_bus);
        var zones = new ZoneService(_bus);
        var triggers = new TriggerManager(stack, _bus);
        var resolver = new StackResolver(_bus, zones);
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);
        var priority = new PriorityManager(new List<Player> { alice, bob }, stack, _bus, triggers);

        var bolt = new Instant("Bolt", "R") { Owner = alice, Zone = ZoneType.Hand };
        alice.Zones.Hand.AddCard(bolt);

        var dispatcherCalled = false;
        var castFlow = new SpellCastFlow(stack, zones, _bus);
        Func<Player, PriorityAction.CastSpell, GameContext, Task> dispatcher = async (player, cast, ctx) =>
        {
            dispatcherCalled = true;
            var sub = new ScriptedAgent();
            sub.QueueMana(ManaPayment.Empty);
            await castFlow.CastAsync(player, cast.Card,
                SpellDefinition.Vanilla(_ => System.Array.Empty<IEffect>()),
                sub, ctx);
        };

        var aliceAgent = new ScriptedAgent();
        aliceAgent.QueuePriority(new PriorityAction.CastSpell(bolt, System.Array.Empty<object>()));
        for (var i = 0; i < 5; i++) aliceAgent.QueuePriority(PriorityAction.Pass);
        var bobAgent = new ScriptedAgent();
        for (var i = 0; i < 5; i++) bobAgent.QueuePriority(PriorityAction.Pass);

        var loop = new PriorityLoop(
            new[] { alice, bob }, priority, stack, resolver, zones,
            new Dictionary<Player, IPlayerAgent>
            { [alice] = aliceAgent, [bob] = bobAgent },
            () => 1, () => PhaseStateType.PreCombatMain,
            new LandDropTracker(),
            castDispatcher: dispatcher);

        await loop.RunUntilRoundEndsAsync(alice);

        dispatcherCalled.Should().BeTrue();
        bolt.Zone.Should().Be(ZoneType.Graveyard); // pushed to stack + resolved
    }

    [Fact]
    public async Task CastSpellAction_NoDispatcher_Throws()
    {
        var stack = new Majik.Core.Stack.Stack(_bus);
        var zones = new ZoneService(_bus);
        var triggers = new TriggerManager(stack, _bus);
        var resolver = new StackResolver(_bus, zones);
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);
        var priority = new PriorityManager(new List<Player> { alice, bob }, stack, _bus, triggers);

        var bolt = new Instant("Bolt", "R") { Owner = alice };
        var aliceAgent = new ScriptedAgent();
        aliceAgent.QueuePriority(new PriorityAction.CastSpell(bolt, System.Array.Empty<object>()));

        var loop = new PriorityLoop(
            new[] { alice, bob }, priority, stack, resolver, zones,
            new Dictionary<Player, IPlayerAgent>
            { [alice] = aliceAgent, [bob] = new ScriptedAgent() },
            () => 1, () => PhaseStateType.PreCombatMain,
            new LandDropTracker());

        var act = async () => await loop.RunUntilRoundEndsAsync(alice);

        await act.Should().ThrowAsync<System.InvalidOperationException>()
            .WithMessage("*castDispatcher*");
    }
}
