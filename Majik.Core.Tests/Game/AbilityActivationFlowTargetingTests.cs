using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.StateMachine;
using Majik.Core.ValueObjects;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.Game;

/// <summary>
/// Verifies that <see cref="AbilityActivationFlow"/> collects chosen targets
/// from the agent and stores them on the activated ability's
/// <see cref="ActivatedAbility.ChosenTargets"/> (Rule 602.2b).
/// </summary>
public class AbilityActivationFlowTargetingTests
{
    private readonly EventBus _bus = new();
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    [Fact]
    public async Task ActivateAsync_StoresChosenTargets_WhenOneRequest()
    {
        var stack = new Majik.Core.Stack.Stack(_bus);
        var flow = new AbilityActivationFlow(stack, _bus);

        var pinger = new Creature("Pinger", "T", 1, 1) { Owner = _alice, Controller = _alice };
        var ability = new ActivatedAbility(pinger, _alice);

        // One target request; scripted agent returns Bob as the target.
        var req = new TargetRequest("target player", 1, 1, new object[] { _bob });
        var agent = new ScriptedAgent();
        agent.QueueTargets(new object[] { _bob });

        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _alice,
            1, PhaseStateType.PreCombatMain, stack);

        await flow.ActivateAsync(_alice, ability,
            targetRequests: new[] { req },
            cost: null,
            agent: agent,
            ctx: ctx);

        ability.ChosenTargets.Should().HaveCount(1);
        ability.ChosenTargets[0].Should().ContainSingle().Which.Should().BeSameAs(_bob);
    }

    [Fact]
    public async Task ActivateAsync_StoresMultipleRequestsInOrder()
    {
        var stack = new Majik.Core.Stack.Stack(_bus);
        var flow = new AbilityActivationFlow(stack, _bus);

        var pinger = new Creature("Pinger", "T", 1, 1) { Owner = _alice, Controller = _alice };
        var ability = new ActivatedAbility(pinger, _alice);

        var req1 = new TargetRequest("target player", 1, 1, new object[] { _bob });
        var req2 = new TargetRequest("target player 2", 1, 1, new object[] { _alice });
        var agent = new ScriptedAgent();
        agent.QueueTargets(new object[] { _bob });
        agent.QueueTargets(new object[] { _alice });

        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _alice,
            1, PhaseStateType.PreCombatMain, stack);

        await flow.ActivateAsync(_alice, ability,
            targetRequests: new[] { req1, req2 },
            cost: null,
            agent: agent,
            ctx: ctx);

        ability.ChosenTargets.Should().HaveCount(2);
        ability.ChosenTargets[0].Should().ContainSingle().Which.Should().BeSameAs(_bob);
        ability.ChosenTargets[1].Should().ContainSingle().Which.Should().BeSameAs(_alice);
    }

    [Fact]
    public async Task ActivateAsync_NoRequests_ChosenTargetsIsEmpty()
    {
        var stack = new Majik.Core.Stack.Stack(_bus);
        var flow = new AbilityActivationFlow(stack, _bus);

        var pinger = new Creature("Pinger", "T", 1, 1) { Owner = _alice, Controller = _alice };
        var ability = new ActivatedAbility(pinger, _alice);

        var agent = new ScriptedAgent();
        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _alice,
            1, PhaseStateType.PreCombatMain, stack);

        await flow.ActivateAsync(_alice, ability,
            targetRequests: System.Array.Empty<TargetRequest>(),
            cost: null,
            agent: agent,
            ctx: ctx);

        ability.ChosenTargets.Should().BeEmpty();
    }

    [Fact]
    public void SetChosenTargets_ReplacesExisting()
    {
        var source = new Creature("Bear", "1G", 2, 2) { Owner = _alice };
        var ability = new ActivatedAbility(source, _alice);

        var first = new List<IReadOnlyList<object>> { new object[] { _bob } };
        ability.SetChosenTargets(first);
        ability.ChosenTargets.Should().HaveCount(1);

        var second = new List<IReadOnlyList<object>> { new object[] { _alice }, new object[] { _bob } };
        ability.SetChosenTargets(second);
        ability.ChosenTargets.Should().HaveCount(2);
    }

    [Fact]
    public void SetChosenTargets_NullArg_SetsEmpty()
    {
        var source = new Creature("Bear", "1G", 2, 2) { Owner = _alice };
        var ability = new ActivatedAbility(source, _alice);

        ability.SetChosenTargets(null!);

        ability.ChosenTargets.Should().BeEmpty();
    }
}
