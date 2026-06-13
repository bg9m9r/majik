using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.Costs;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.StateMachine;
using Xunit;
using Creature = Majik.Core.Cards.Creature;
using ManaCost = Majik.Core.ValueObjects.ManaCost;

namespace Majik.Core.Tests.Game;

/// <summary>
/// GAP 2 — <see cref="Majik.Core.Game.AbilityActivationFlow"/> prompts for X on
/// a variable-X ({X}-cost) activated ability, pays the cost with {X} expanded to
/// the chosen generic amount (REUSING the spell path's
/// <see cref="ManaCost.AddGenericCost"/> machinery), records the chosen X on the
/// ability via <see cref="ActivatedAbility.SetChosenX"/>, and threads it to
/// resolution. A non-X ability must NOT prompt for X (unchanged behaviour).
/// </summary>
public class AbilityActivationFlowVariableXTests
{
    private readonly EventBus _bus = new();
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    private Majik.Core.Game.GameContext Ctx(Majik.Core.Stack.Stack stack) =>
        new(_alice, new[] { _alice, _bob }, _alice, 1, StepStateType.PreCombatMain, stack);

    [Fact]
    public async System.Threading.Tasks.Task VariableX_Ability_PromptsX_PaysExpandedCost_RecordsChosenX()
    {
        var stack = new Majik.Core.Stack.Stack(_bus);
        var flow = new Majik.Core.Game.AbilityActivationFlow(stack, _bus);
        var src = new Creature("Hydra", "{2}", 1, 1) { Owner = _alice, Controller = _alice };
        var ability = new ActivatedAbility(src, _alice);

        // Give Alice enough mana to pay {X}=3 plus a {1} base. Cost = {1}{X}.
        _alice.AddManaToPool(ManaCost.Parse("4"));

        var agent = new ScriptedAgent();
        agent.QueueX(3);                 // ChooseXAsync → 3
        agent.QueueMana(ManaPayment.Empty);

        var startTotal = _alice.ManaPool.Total;

        await flow.ActivateAsync(_alice, ability,
            targetRequests: System.Array.Empty<TargetRequest>(),
            cost: ManaCost.Parse("1X"),
            agent: agent, ctx: Ctx(stack));

        ability.ChosenX.Should().Be(3, "the flow records the agent's chosen X");
        // Paid {1} base + {3} for X = 4 generic mana drained.
        (startTotal - _alice.ManaPool.Total).Should().Be(4,
            "the {X} cost is expanded to {3} generic and paid alongside the {1} base (CR 601.2 analogue)");
        stack.Count.Should().Be(1);
    }

    [Fact]
    public async System.Threading.Tasks.Task NonX_Ability_DoesNotPromptForX()
    {
        var stack = new Majik.Core.Stack.Stack(_bus);
        var flow = new Majik.Core.Game.AbilityActivationFlow(stack, _bus);
        var src = new Creature("Pinger", "{2}", 1, 1) { Owner = _alice, Controller = _alice };
        var ability = new ActivatedAbility(src, _alice);
        _alice.AddManaToPool(ManaCost.Parse("2"));

        // No X queued — if the flow prompted for X, ScriptedAgent.ChooseXAsync
        // would throw on the empty queue.
        var agent = new ScriptedAgent();
        agent.QueueMana(ManaPayment.Empty);

        await flow.ActivateAsync(_alice, ability,
            targetRequests: System.Array.Empty<TargetRequest>(),
            cost: ManaCost.Parse("2"),
            agent: agent, ctx: Ctx(stack));

        ability.ChosenX.Should().BeNull("a non-X ability does not prompt for or record X");
        stack.Count.Should().Be(1);
    }
}
