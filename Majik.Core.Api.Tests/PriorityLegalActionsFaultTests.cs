using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.Api;
using Majik.Core.Cards;
using Majik.Core.Costs;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.StateMachine;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Api.Tests;

/// <summary>
/// <see cref="PriorityLegalActions"/> is a hint. It calls card-supplied gates
/// (activate-only-if predicates, cost checks, cost reduction, mana abilities),
/// so a throwing card must only drop itself from the list — never fault the
/// prompt or the game — and the fault is reported to
/// <see cref="GameFacade.OnEngineDiagnostic"/>.
/// </summary>
[Collection(EngineDiagnosticSinkCollection.Name)] // mutates the static sink
public class PriorityLegalActionsFaultTests
{
    private readonly EventBus _bus = new();
    private readonly Majik.Core.Stack.Stack _stack;
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    public PriorityLegalActionsFaultTests() => _stack = new Majik.Core.Stack.Stack(_bus);

    private GameContext Ctx() =>
        new(_alice, new[] { _alice, _bob }, activePlayer: _alice,
            turnNumber: 3, StepStateType.PreCombatMain, _stack, landPlayAvailable: true);

    private Creature Ready(string name)
    {
        var creature = new Creature(name, "{1}{G}", 2, 2);
        creature.SetOwner(_alice); creature.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(creature); creature.SetZone(ZoneType.Battlefield);
        creature.HasSummoningSickness = false;
        return creature;
    }

    [Fact]
    public async Task AThrowingActivationGate_DropsOnlyThatAbility_PromptStillRaised_FaultReported()
    {
        var broken = Ready("Broken Gate");
        var bad = new ActivatedAbility(broken, _alice, costs: new ICost[] { AdditionalCost.Tap(broken) },
            canActivateCheckCtx: _ => throw new NullReferenceException("card bug"));
        broken.AddAbility(bad);
        var fine = Ready("Fine Elf");
        var good = new ActivatedAbility(fine, _alice, costs: new ICost[] { AdditionalCost.Tap(fine) });
        fine.AddAbility(good);
        var land = new Land("Forest");
        land.SetOwner(_alice); land.SetController(_alice);
        _alice.Zones.Hand.AddCard(land); land.SetZone(ZoneType.Hand);

        var reported = new List<(Exception Ex, string Context)>();
        var previous = GameFacade.OnEngineDiagnostic;
        GameFacade.OnEngineDiagnostic = (ex, context) => { lock (reported) reported.Add((ex, context)); };
        try
        {
            var agent = new RemoteAgent(_alice);
            var raised = 0;
            agent.PromptRequested += _ => raised++;

            var task = agent.ChoosePriorityActionAsync(Ctx());

            raised.Should().Be(1, "the prompt is raised even though one card's gate threw");
            var legal = agent.PendingPayload!.LegalActions!;
            legal.ActivatableAbilities.Should().Contain(a => a.AbilityId == good.Id);
            legal.ActivatableAbilities.Should().NotContain(a => a.AbilityId == bad.Id);
            legal.PlayableLandIds.Should().Contain(land.InstanceId);

            reported.Should().ContainSingle().Which.Ex.Should().BeOfType<NullReferenceException>();
            reported[0].Context.Should().Contain("Broken Gate");

            agent.Submit(new Majik.Core.Api.Commands.PassPriorityCommand { PlayerId = _alice.Id });
            await task;
        }
        finally
        {
            GameFacade.OnEngineDiagnostic = previous;
        }
    }
}

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class EngineDiagnosticSinkCollection
{
    public const string Name = "EngineDiagnosticSink";
}
