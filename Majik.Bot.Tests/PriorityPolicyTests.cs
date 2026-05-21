using FluentAssertions;
using Majik.Bot.Evaluation;
using Majik.Bot.Heuristic;
using Majik.Bot.Tests.Helpers;
using Majik.Core.Cards;
using Majik.Core.Players.Agents;
using Xunit;

namespace Majik.Bot.Tests;

public class PriorityPolicyTests
{
    [Fact]
    public void PicksPlayLand_WhenLandInHand_AndLandDropAvailable()
    {
        var s = new BotTestScenario();
        var land = new Land("Mountain");
        s.AddCardToHand(s.Self, land);
        var pol = new PriorityPolicy(ArchetypeWeights.Burn);

        var action = pol.Pick(s.Context, s.Self);
        action.Should().BeOfType<PriorityAction.PlayLand>();
    }

    [Fact]
    public void Passes_WhenNothingPlayable()
    {
        var s = new BotTestScenario();
        var pol = new PriorityPolicy(ArchetypeWeights.Burn);
        var action = pol.Pick(s.Context, s.Self);
        action.Should().BeOfType<PriorityAction.PassAction>();
    }

    [Fact]
    public void Passes_WhenOpponentsTurn_AndNoInstantSpeedPlay()
    {
        var s = new BotTestScenario();
        var land = new Land("Mountain");
        s.AddCardToHand(s.Self, land);
        var oppCtx = new Majik.Core.Game.GameContext(
            s.Self, new[] { s.Self, s.Opponent }, activePlayer: s.Opponent,
            turnNumber: 1, currentPhase: Majik.Core.StateMachine.PhaseStateType.Main, stack: s.Stack);
        var pol = new PriorityPolicy(ArchetypeWeights.Burn);
        pol.Pick(oppCtx, s.Self).Should().BeOfType<PriorityAction.PassAction>();
    }
}
