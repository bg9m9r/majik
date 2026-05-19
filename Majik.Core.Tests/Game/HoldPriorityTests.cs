using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.Cards;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.StateMachine;
using Majik.Core.Zones;
using Xunit;

/// <summary>
/// CR 117.3c — a player who casts/activates may opt to hold priority.
/// Phase 15 wires the flag through <see cref="PriorityAction"/> sub-records
/// and <see cref="PriorityLoop"/> honors it without reissuing priority to
/// the opponent in between.
/// </summary>
public class HoldPriorityTests
{
    private readonly EventBus _bus = new();

    [Fact]
    public async Task ActorHolds_PlayLand_TwiceInOneSlot_AllowedViaChainedActions()
    {
        // Land drop tracker max=2 to allow consecutive plays.
        var stack = new Majik.Core.Stack.Stack(_bus);
        var zones = new ZoneService(_bus);
        var triggers = new TriggerManager(stack, _bus);
        var resolver = new StackResolver(_bus, zones);
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);
        var priority = new PriorityManager(new List<Player> { alice, bob }, stack, _bus, triggers);
        var tracker = new LandDropTracker();
        tracker.SetMaxLandDropsThisTurn(alice, 2);

        var l1 = NamedCardFactory.Create("Mountain", alice);
        var l2 = NamedCardFactory.Create("Mountain", alice);
        l1.Zone = ZoneType.Hand; l2.Zone = ZoneType.Hand;
        alice.Zones.Hand.AddCard(l1);
        alice.Zones.Hand.AddCard(l2);

        var aliceAgent = new ScriptedAgent();
        aliceAgent.QueuePriority(new PriorityAction.PlayLand(l1, HoldPriority: true));
        aliceAgent.QueuePriority(new PriorityAction.PlayLand(l2)); // no hold this time
        aliceAgent.QueuePriority(PriorityAction.Pass);
        var bobAgent = new ScriptedAgent();
        bobAgent.QueuePriority(PriorityAction.Pass);
        bobAgent.QueuePriority(PriorityAction.Pass);

        var loop = new PriorityLoop(
            new[] { alice, bob }, priority, stack, resolver, zones,
            new Dictionary<Player, IPlayerAgent>
            { [alice] = aliceAgent, [bob] = bobAgent },
            () => 1, () => PhaseStateType.Main, tracker);

        await loop.RunUntilRoundEndsAsync(alice);

        l1.Zone.Should().Be(ZoneType.Battlefield);
        l2.Zone.Should().Be(ZoneType.Battlefield);
    }
}
