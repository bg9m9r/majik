using FluentAssertions;
using Majik.Core.Cards;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.StateMachine;
using Majik.Core.ValueObjects;
using Xunit;

namespace Majik.Core.Tests.Players.Agents;

public class DeterministicBotAgentTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);
    private readonly DeterministicBotAgent _bot = new();

    [Fact]
    public async Task Priority_AlwaysPasses()
    {
        var action = await _bot.ChoosePriorityActionAsync(NewContext());

        action.Should().Be(PriorityAction.Pass);
    }

    [Fact]
    public async Task Mulligan_AlwaysKeeps()
    {
        var d = await _bot.ChooseMulliganAsync(NewContext(), Array.Empty<ICard>(), 0);

        d.Should().Be(MulliganDecision.Keep);
    }

    [Fact]
    public async Task Targets_PicksMinFromCandidates()
    {
        var bear = new Creature("Bear", "1G", 2, 2) { Owner = _alice };
        var wolf = new Creature("Wolf", "2G", 3, 3) { Owner = _alice };
        var req = new TargetRequest("any", 2, 3, new[] { (object)bear, wolf });

        var picked = await _bot.ChooseTargetsAsync(NewContext(), req);

        picked.Should().HaveCount(2);
        picked.Should().Equal(bear, wolf);
    }

    [Fact]
    public async Task X_DefaultsToZero()
    {
        var card = new Instant("Fireball", "X{R}") { Owner = _alice };

        (await _bot.ChooseXAsync(NewContext(), card)).Should().Be(0);
    }

    [Fact]
    public async Task Mode_DefaultsToZero()
    {
        (await _bot.ChooseModeAsync(NewContext(), new[] { "a", "b" })).Should().Be(0);
    }

    [Fact]
    public async Task TriggerOrder_PreservesInputOrder()
    {
        var ordered = await _bot.OrderTriggersAsync(
            NewContext(), Array.Empty<Majik.Core.Abilities.ITriggeredAbility>());

        ordered.Should().BeEmpty();
    }

    [Fact]
    public async Task Mana_ReturnsEmpty_UsesFloatingOnly()
    {
        (await _bot.ChooseManaSourcesAsync(NewContext(), ManaCost.Parse("R")))
            .Should().Be(ManaPayment.Empty);
    }

    [Fact]
    public async Task Attackers_None()
    {
        var bear = new Creature("Bear", "1G", 2, 2) { Owner = _alice };

        var plan = await _bot.DeclareAttackersAsync(NewContext(), new[] { bear });

        plan.Should().Be(CombatPlan.None);
    }

    [Fact]
    public async Task Blockers_None()
    {
        var plan = await _bot.DeclareBlockersAsync(
            NewContext(), Array.Empty<Creature>(), Array.Empty<Creature>());

        plan.Should().Be(BlockPlan.None);
    }

    private GameContext NewContext()
    {
        var stack = new Majik.Core.Stack.Stack();
        return new GameContext(_alice, new[] { _alice, _bob }, _alice, 1, PhaseStateType.PreCombatMain, stack);
    }
}
