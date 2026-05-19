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

public class PriorityLoopLandDropTests
{
    private readonly EventBus _bus = new();
    private readonly Majik.Core.Stack.Stack _stack;
    private readonly ZoneService _zones;
    private readonly TriggerManager _triggers;
    private readonly StackResolver _resolver;
    private readonly PriorityManager _priority;
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    public PriorityLoopLandDropTests()
    {
        _stack = new Majik.Core.Stack.Stack(_bus);
        _zones = new ZoneService(_bus);
        _triggers = new TriggerManager(_stack, _bus);
        _resolver = new StackResolver(_bus, _zones);
        _priority = new PriorityManager(new List<Player> { _alice, _bob }, _stack, _bus, _triggers);
    }

    [Fact]
    public async Task PlayLand_FirstDropOfTurn_Succeeds()
    {
        var land = NamedCardFactory.Create("Mountain", _alice);
        land.Zone = ZoneType.Hand;
        _alice.Zones.Hand.AddCard(land);
        var tracker = new LandDropTracker();

        var aliceAgent = new ScriptedAgent();
        aliceAgent.QueuePriority(new PriorityAction.PlayLand(land));
        aliceAgent.QueuePriority(PriorityAction.Pass);
        var bobAgent = new ScriptedAgent();
        bobAgent.QueuePriority(PriorityAction.Pass);
        bobAgent.QueuePriority(PriorityAction.Pass);

        var loop = new PriorityLoop(
            new[] { _alice, _bob }, _priority, _stack, _resolver, _zones,
            new Dictionary<Player, IPlayerAgent>
            { [_alice] = aliceAgent, [_bob] = bobAgent },
            () => 1, () => PhaseStateType.Main, tracker);

        await loop.RunUntilRoundEndsAsync(_alice);

        land.Zone.Should().Be(ZoneType.Battlefield);
        tracker.DropsUsedThisTurn(_alice).Should().Be(1);
    }

    [Fact]
    public async Task PlayLand_SecondDropOfTurn_Throws()
    {
        var l1 = NamedCardFactory.Create("Mountain", _alice);
        var l2 = NamedCardFactory.Create("Mountain", _alice);
        l1.Zone = ZoneType.Hand; l2.Zone = ZoneType.Hand;
        _alice.Zones.Hand.AddCard(l1);
        _alice.Zones.Hand.AddCard(l2);
        var tracker = new LandDropTracker();
        tracker.RecordLandPlayed(_alice); // pretend already played one

        var aliceAgent = new ScriptedAgent();
        aliceAgent.QueuePriority(new PriorityAction.PlayLand(l2));
        var bobAgent = new ScriptedAgent();

        var loop = new PriorityLoop(
            new[] { _alice, _bob }, _priority, _stack, _resolver, _zones,
            new Dictionary<Player, IPlayerAgent>
            { [_alice] = aliceAgent, [_bob] = bobAgent },
            () => 1, () => PhaseStateType.Main, tracker);

        var act = async () => await loop.RunUntilRoundEndsAsync(_alice);

        await act.Should().ThrowAsync<System.InvalidOperationException>()
            .WithMessage("*already played*");
    }
}
