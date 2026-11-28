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

namespace Majik.Core.Tests.Game;

/// <summary>
/// LoopSafetyNet: a hard cap, not CR 732. A step that never ends (a runaway
/// activate-resolve loop, or a stack that only grows) ends the game as a draw
/// (CR 104.4b) and is published as a LoopSafetyNetTrippedEvent. An ordinary
/// step does not trip it.
/// </summary>
public class LoopSafetyNetTests
{
    private readonly EventBus _bus = new();
    private readonly Majik.Core.Stack.Stack _stack;
    private readonly PriorityManager _priority;
    private readonly StackResolver _resolver;
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);
    private readonly Creature _source;
    private int _resolved;

    public LoopSafetyNetTests()
    {
        _stack = new Majik.Core.Stack.Stack(_bus);
        var triggers = new TriggerManager(_stack, _bus);
        _priority = new PriorityManager(new List<Player> { _alice, _bob }, _stack, _bus, triggers);
        _resolver = new StackResolver(_bus);
        _source = new Creature("Looper", "1G", 2, 2) { Owner = _alice, Zone = ZoneType.Battlefield };
    }

    // Each activation puts a fresh ability on the stack.
    private Task PushAbility(Player p, PriorityAction.ActivateAbility a, GameContext ctx)
    {
        _stack.Push(new TriggeredAbility(_source, _alice, Triggers.OnEnterBattlefieldSelf(_source),
            effects: new IEffect[] { new Effect("tick", () => _resolved++) }));
        return Task.CompletedTask;
    }

    private static PriorityAction.ActivateAbility Activate() => new(null!, Array.Empty<object>());

    private PriorityLoop NewLoop(IPlayerAgent alice, IPlayerAgent bob) => new(
        players: new[] { _alice, _bob },
        priority: _priority,
        stack: _stack,
        stackResolver: _resolver,
        zoneService: new ZoneService(_bus),
        agents: new Dictionary<Player, IPlayerAgent> { [_alice] = alice, [_bob] = bob },
        turnNumberAccessor: () => 7,
        phaseAccessor: () => StepStateType.PreCombatMain,
        landDropTracker: new LandDropTracker(),
        activateDispatcher: PushAbility,
        eventBus: _bus);

    [Fact]
    public async Task ARunawayActivateResolveLoop_EndsTheGameAsADraw_AndIsReported()
    {
        // Alice activates, then passes; Bob passes; the ability resolves; again.
        // The per-round 500-action cap never fires: each resolution starts a
        // new round.
        var alice = new ScriptedAgent();
        var bob = new ScriptedAgent();
        for (var i = 0; i < 200; i++)
        {
            alice.QueuePriority(Activate());
            alice.QueuePriority(PriorityAction.Pass);
            bob.QueuePriority(PriorityAction.Pass);
        }
        var tripped = new List<LoopSafetyNetTrippedEvent>();
        _bus.Subscribe<LoopSafetyNetTrippedEvent>(tripped.Add);
        using var _ = LoopSafetyNet.OverrideForTest(actionLimit: 40, stackDepthLimit: null);

        await NewLoop(alice, bob).RunUntilRoundEndsAsync(_alice);

        _resolved.Should().BeLessThan(40, "the net stopped the loop");
        (_alice.HasLost, _bob.HasLost).Should().Be((true, true), "a draw: everyone still in the game loses at once (CR 104.4a)");
        var e = tripped.Should().ContainSingle().Subject;
        e.Message.Should().Contain("CR 104.4b").And.Contain("safety net").And.Contain("turn 7");
        e.ActionsThisStep.Should().Be(41);
    }

    [Fact]
    public async Task AStackThatOnlyGrows_EndsTheGameAsADraw_AndIsReported()
    {
        var alice = new ScriptedAgent();
        for (var i = 0; i < 50; i++) alice.QueuePriority(Activate());
        var tripped = new List<LoopSafetyNetTrippedEvent>();
        _bus.Subscribe<LoopSafetyNetTrippedEvent>(tripped.Add);
        using var _ = LoopSafetyNet.OverrideForTest(actionLimit: null, stackDepthLimit: 5);

        await NewLoop(alice, new ScriptedAgent()).RunUntilRoundEndsAsync(_alice);

        (_alice.HasLost, _bob.HasLost).Should().Be((true, true));
        tripped.Should().ContainSingle().Which.StackDepth.Should().Be(6);
        _resolved.Should().Be(0, "nothing resolves into a game that has ended");
    }

    [Fact]
    public async Task AnOrdinaryStep_DoesNotTripTheNet()
    {
        // Three activations, each resolved, then both pass on an empty stack.
        var alice = new ScriptedAgent();
        var bob = new ScriptedAgent();
        for (var i = 0; i < 3; i++)
        {
            alice.QueuePriority(Activate());
            alice.QueuePriority(PriorityAction.Pass);
            bob.QueuePriority(PriorityAction.Pass);
        }
        alice.QueuePriority(PriorityAction.Pass);
        bob.QueuePriority(PriorityAction.Pass);
        var tripped = new List<LoopSafetyNetTrippedEvent>();
        _bus.Subscribe<LoopSafetyNetTrippedEvent>(tripped.Add);

        await NewLoop(alice, bob).RunUntilRoundEndsAsync(_alice);

        _resolved.Should().Be(3);
        tripped.Should().BeEmpty();
        (_alice.HasLost, _bob.HasLost).Should().Be((false, false));
    }

    [Fact]
    public void TheCaps_AreAtLeastTwentyTimesTheGatesMeasuredMaxima()
    {
        // Measured over the full-game gate (1250 games, 3 runs): at most 26
        // actions + resolutions in one step, and a stack 12 deep.
        LoopSafetyNet.MaxActionsPerStep.Should().BeGreaterThanOrEqualTo(26 * 20);
        LoopSafetyNet.MaxStackDepth.Should().BeGreaterThanOrEqualTo(12 * 20);
    }

    [Fact]
    public async Task ATrippedNet_PublishesALossForEachPlayer_AndWritesNothingToStderr()
    {
        // CR 104.4a — a draw: every player still in the game loses at once, so
        // each gets the same PlayerLostEvent any other loss publishes (the
        // facade's game-over gates and the wire both read it).
        var alice = new ScriptedAgent();
        for (var i = 0; i < 50; i++) alice.QueuePriority(Activate());
        var lost = new List<Player>();
        _bus.Subscribe<PlayerLostEvent>(e => lost.Add(e.Player));
        using var _ = LoopSafetyNet.OverrideForTest(actionLimit: null, stackDepthLimit: 5);
        var stderr = System.Console.Error;
        var captured = new System.IO.StringWriter();
        System.Console.SetError(captured);
        try
        {
            await NewLoop(alice, new ScriptedAgent()).RunUntilRoundEndsAsync(_alice);
        }
        finally
        {
            System.Console.SetError(stderr);
        }

        lost.Should().BeEquivalentTo(new[] { _alice, _bob });
        captured.ToString().Should().NotContain("safety net", "the LoopSafetyNetTrippedEvent is the report");
    }
}