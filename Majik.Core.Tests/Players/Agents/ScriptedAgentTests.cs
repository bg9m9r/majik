using FluentAssertions;
using Majik.Core.Cards;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.StateMachine;
using Majik.Core.ValueObjects;
using Xunit;

namespace Majik.Core.Tests.Players.Agents;

public class ScriptedAgentTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    [Fact]
    public async Task PriorityActions_DeliveredInFifoOrder()
    {
        var land = new Land("Mountain") { Owner = _alice };
        var agent = new ScriptedAgent();
        agent.QueuePriority(PriorityAction.Pass);
        agent.QueuePriority(new PriorityAction.PlayLand(land));
        agent.QueuePriority(PriorityAction.Pass);

        var ctx = NewContext();

        (await agent.ChoosePriorityActionAsync(ctx)).Should().Be(PriorityAction.Pass);
        var second = await agent.ChoosePriorityActionAsync(ctx);
        second.Should().BeOfType<PriorityAction.PlayLand>()
            .Which.Land.Should().BeSameAs(land);
        (await agent.ChoosePriorityActionAsync(ctx)).Should().Be(PriorityAction.Pass);
    }

    [Fact]
    public async Task Priority_Underflow_Throws()
    {
        var agent = new ScriptedAgent();
        var ctx = NewContext();

        var act = async () => await agent.ChoosePriorityActionAsync(ctx);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*priority*");
    }

    [Fact]
    public async Task Mulligan_DeliveredInOrder()
    {
        var agent = new ScriptedAgent();
        agent.QueueMulligan(MulliganDecision.Mulligan);
        agent.QueueMulligan(MulliganDecision.Keep);
        var ctx = NewContext();

        (await agent.ChooseMulliganAsync(ctx, Array.Empty<ICard>(), 0)).Should().Be(MulliganDecision.Mulligan);
        (await agent.ChooseMulliganAsync(ctx, Array.Empty<ICard>(), 1)).Should().Be(MulliganDecision.Keep);
    }

    [Fact]
    public async Task Targets_DeliveredInOrder()
    {
        var bear = new Creature("Bear", "1G", 2, 2) { Owner = _alice };
        var agent = new ScriptedAgent();
        agent.QueueTargets(new[] { (object)bear });

        var ctx = NewContext();
        var picked = await agent.ChooseTargetsAsync(
            ctx, new TargetRequest("any creature", 1, 1, new[] { (object)bear }));

        picked.Should().ContainSingle().Which.Should().BeSameAs(bear);
    }

    [Fact]
    public async Task X_Mode_Mana_OrderTriggers_AllScriptable()
    {
        var agent = new ScriptedAgent();
        agent.QueueX(3);
        agent.QueueMode(1);
        agent.QueueMana(ManaPayment.Empty);
        agent.QueueTriggerOrder(Array.Empty<Majik.Core.Abilities.ITriggeredAbility>());

        var ctx = NewContext();
        var bolt = new Instant("Fireball", "X{R}") { Owner = _alice };

        (await agent.ChooseXAsync(ctx, bolt)).Should().Be(3);
        (await agent.ChooseModeAsync(ctx, new[] { "a", "b" })).Should().Be(1);
        (await agent.ChooseManaSourcesAsync(ctx, ManaCost.Parse("R"))).Should().Be(ManaPayment.Empty);
        (await agent.OrderTriggersAsync(ctx, Array.Empty<Majik.Core.Abilities.ITriggeredAbility>()))
            .Should().BeEmpty();
    }

    [Fact]
    public async Task Combat_DeliveredInOrder()
    {
        var attacker = new Creature("Bear", "1G", 2, 2) { Owner = _alice };
        var agent = new ScriptedAgent();
        agent.QueueAttackers(new CombatPlan(
            new[] { new AttackerDeclaration(attacker, _bob) }));
        agent.QueueBlockers(BlockPlan.None);
        var ctx = NewContext();

        var plan = await agent.DeclareAttackersAsync(ctx, new[] { attacker });
        plan.Attackers.Should().HaveCount(1);

        (await agent.DeclareBlockersAsync(ctx, Array.Empty<Creature>(), Array.Empty<Creature>()))
            .Should().Be(BlockPlan.None);
    }

    private GameContext NewContext()
    {
        var stack = new Majik.Core.Stack.Stack();
        return new GameContext(_alice, new[] { _alice, _bob }, _alice, 1, PhaseStateType.Main, stack);
    }
}
