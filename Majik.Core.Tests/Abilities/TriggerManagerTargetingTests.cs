using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.StateMachine;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.Abilities;

/// <summary>
/// Verifies that <see cref="TriggerManager.PutPendingTriggersOnStackAsync"/>
/// prompts the controller's agent for targets before pushing triggers that
/// declare <see cref="TriggeredAbility.TargetRequests"/> (Rule 603.3).
/// </summary>
public class TriggerManagerTargetingTests
{
    private readonly EventBus _bus = new();
    private readonly Majik.Core.Stack.Stack _stack;
    private readonly TriggerManager _manager;
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    public TriggerManagerTargetingTests()
    {
        _stack = new Majik.Core.Stack.Stack(_bus);
        _manager = new TriggerManager(_stack, _bus);
    }

    [Fact]
    public async Task PutPendingTriggersOnStackAsync_PromptsTargets_BeforePush()
    {
        // Triggered ability that requires one target.
        var req = new TargetRequest("target player", 1, 1, new object[] { _bob });
        var ability = BuildEtbAbilityWithTargetRequests(_alice, targetRequests: new[] { req });

        ((ICard)ability.Source).SetZone(ZoneType.Battlefield);
        _manager.RegisterTriggeredAbility(ability);
        _manager.EvaluateTriggers(
            new CardMovedEvent((ICard)ability.Source, ZoneType.Hand, ZoneType.Battlefield));

        _manager.PendingCount.Should().Be(1);

        // Agent picks Bob as the target.
        var agent = new ScriptedAgent();
        agent.QueueTriggerOrder(new ITriggeredAbility[] { ability });
        agent.QueueTargets(new object[] { _bob });

        var agents = new Dictionary<Player, IPlayerAgent> { [_alice] = agent };
        var ctx = NewContext();

        await _manager.PutPendingTriggersOnStackAsync(_alice, agents, ctx);

        // Ability is now on the stack with Bob as the chosen target.
        _stack.Count.Should().Be(1);
        ability.ChosenTargets.Should().HaveCount(1);
        ability.ChosenTargets[0].Should().ContainSingle().Which.Should().BeSameAs(_bob);
    }

    [Fact]
    public async Task PutPendingTriggersOnStackAsync_MultipleRequests_AllCollected()
    {
        var req1 = new TargetRequest("target player 1", 1, 1, new object[] { _bob });
        var req2 = new TargetRequest("target player 2", 1, 1, new object[] { _alice });
        var ability = BuildEtbAbilityWithTargetRequests(_alice,
            targetRequests: new[] { req1, req2 });

        ((ICard)ability.Source).SetZone(ZoneType.Battlefield);
        _manager.RegisterTriggeredAbility(ability);
        _manager.EvaluateTriggers(
            new CardMovedEvent((ICard)ability.Source, ZoneType.Hand, ZoneType.Battlefield));

        var agent = new ScriptedAgent();
        agent.QueueTriggerOrder(new ITriggeredAbility[] { ability });
        agent.QueueTargets(new object[] { _bob });
        agent.QueueTargets(new object[] { _alice });

        var agents = new Dictionary<Player, IPlayerAgent> { [_alice] = agent };

        await _manager.PutPendingTriggersOnStackAsync(_alice, agents, NewContext());

        ability.ChosenTargets.Should().HaveCount(2);
        ability.ChosenTargets[0].Should().ContainSingle().Which.Should().BeSameAs(_bob);
        ability.ChosenTargets[1].Should().ContainSingle().Which.Should().BeSameAs(_alice);
    }

    [Fact]
    public async Task PutPendingTriggersOnStackAsync_NoTargetRequests_SkipsPrompt()
    {
        // Standard trigger with no target requests — agent is NOT asked for targets.
        var source = new Creature("Bear", "1G", 2, 2)
            { Owner = _alice, Zone = ZoneType.Battlefield };
        var ability = new TriggeredAbility(source, _alice,
            Triggers.OnEnterBattlefieldSelf(source));

        _manager.RegisterTriggeredAbility(ability);
        _manager.EvaluateTriggers(
            new CardMovedEvent(source, ZoneType.Hand, ZoneType.Battlefield));

        // Agent has no targets queued; would throw if ChooseTargetsAsync were called.
        var agent = new ScriptedAgent();
        agent.QueueTriggerOrder(new ITriggeredAbility[] { ability });

        var agents = new Dictionary<Player, IPlayerAgent> { [_alice] = agent };

        await _manager.PutPendingTriggersOnStackAsync(_alice, agents, NewContext());

        _stack.Count.Should().Be(1);
        ability.ChosenTargets.Should().BeEmpty();
    }

    [Fact]
    public void TriggeredAbility_TargetRequests_DefaultsToEmpty()
    {
        var source = new Creature("Bear", "1G", 2, 2) { Owner = _alice };
        var ability = new TriggeredAbility(source, _alice,
            Triggers.OnEnterBattlefieldSelf(source));

        ability.TargetRequests.Should().BeEmpty();
        ability.ChosenTargets.Should().BeEmpty();
    }

    [Fact]
    public void TriggeredAbility_SetChosenTargets_NullArg_SetsEmpty()
    {
        var source = new Creature("Bear", "1G", 2, 2) { Owner = _alice };
        var ability = new TriggeredAbility(source, _alice,
            Triggers.OnEnterBattlefieldSelf(source));

        ability.SetChosenTargets(null!);

        ability.ChosenTargets.Should().BeEmpty();
    }

    [Fact]
    public void TriggeredAbility_TargetRequests_StoredFromConstructor()
    {
        var req = new TargetRequest("target creature", 1, 1, System.Array.Empty<object>());
        var source = new Creature("Bear", "1G", 2, 2) { Owner = _alice };
        var ability = new TriggeredAbility(source, _alice,
            Triggers.OnEnterBattlefieldSelf(source),
            targetRequests: new[] { req });

        ability.TargetRequests.Should().HaveCount(1);
        ability.TargetRequests[0].Should().BeSameAs(req);
    }

    // ---- helpers -----------------------------------------------------------

    private TriggeredAbility BuildEtbAbilityWithTargetRequests(
        Player controller,
        IEnumerable<TargetRequest> targetRequests)
    {
        var source = new Creature($"Bear-{Guid.NewGuid()}", "1G", 2, 2)
            { Owner = controller };
        return new TriggeredAbility(
            source, controller,
            Triggers.OnEnterBattlefieldSelf(source),
            targetRequests: targetRequests);
    }

    private GameContext NewContext() =>
        new(_alice, new[] { _alice, _bob }, _alice, 1, PhaseStateType.PreCombatMain, _stack);
}
