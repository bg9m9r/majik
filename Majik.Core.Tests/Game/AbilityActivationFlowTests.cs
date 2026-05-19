using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.StateMachine;
using Majik.Core.ValueObjects;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

public class AbilityActivationFlowTests
{
    private readonly EventBus _bus = new();
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    [Fact]
    public async Task Activate_PushesAbilityOntoStack_FiresEvent()
    {
        var stack = new Majik.Core.Stack.Stack(_bus);
        var flow = new AbilityActivationFlow(stack, _bus);
        var pinger = new Creature("Pinger", "2U", 1, 1) { Owner = _alice, Controller = _alice };
        var ability = new ActivatedAbility(pinger, _alice);

        AbilityActivatedEvent? fired = null;
        _bus.Subscribe<AbilityActivatedEvent>(e => fired = e);

        var agent = new ScriptedAgent();
        agent.QueueMana(ManaPayment.Empty);

        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _alice,
            1, PhaseStateType.Main, stack);

        await flow.ActivateAsync(_alice, ability,
            targetRequests: System.Array.Empty<TargetRequest>(),
            cost: ManaCost.Parse("1"),
            agent: agent, ctx: ctx);

        stack.Count.Should().Be(1);
        fired.Should().NotBeNull();
        fired!.Ability.Should().BeSameAs(ability);
    }

    [Fact]
    public async Task Activate_NoCost_SkipsManaPrompt()
    {
        var stack = new Majik.Core.Stack.Stack(_bus);
        var flow = new AbilityActivationFlow(stack, _bus);
        var pinger = new Creature("Pinger", "2U", 1, 1) { Owner = _alice, Controller = _alice };
        var ability = new ActivatedAbility(pinger, _alice);

        var agent = new ScriptedAgent(); // no mana queued
        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _alice,
            1, PhaseStateType.Main, stack);

        await flow.ActivateAsync(_alice, ability,
            targetRequests: System.Array.Empty<TargetRequest>(),
            cost: null,
            agent: agent, ctx: ctx);

        stack.Count.Should().Be(1);
    }
}
