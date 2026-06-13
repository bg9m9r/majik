using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.StateMachine;
using Xunit;

namespace Majik.Core.Tests.Combo;

/// <summary>
/// Phase B1 (plan 2026-06-13) — proves the <see cref="ScriptedLineAgent"/>
/// contract: it answers only the prompts a line scripts, and ANY unscripted
/// prompt throws loudly (the DelegatingAgent default). The negative test
/// deliberately under-scripts a target prompt and asserts the throw, so a real
/// combo line that reaches an unanticipated decision fails the test instead of
/// silently picking a default.
/// </summary>
public sealed class ScriptedLineAgentTests
{
    [Fact]
    public async Task UnscriptedTargetPrompt_Throws()
    {
        // No OnChooseTargets supplied → the base DelegatingAgent throws.
        var agent = new ScriptedLineAgent();

        var act = async () => await agent.ChooseTargetsAsync(
            ctx: null!,
            request: new TargetRequest(
                Description: "any target",
                MinTargets: 1,
                MaxTargets: 1,
                LegalCandidates: System.Array.Empty<object>()));

        await act.Should().ThrowAsync<NotSupportedException>(
            "an unscripted target prompt must surface loudly, not pick a default");
    }

    [Fact]
    public async Task UnscriptedChoicePrompt_Throws()
    {
        var agent = new ScriptedLineAgent();

        var act = async () => await agent.ChooseAsync(
            ctx: null!,
            req: new ChoiceRequest(
                ChoiceKind.PickOne, "pick a face", Min: 1, Max: 1,
                Candidates: System.Array.Empty<object>()));

        await act.Should().ThrowAsync<NotSupportedException>();
    }

    [Fact]
    public async Task EmptyScript_PassesPriority()
    {
        // With no scripted action the agent idles by passing — passing is the
        // natural idle, never a "decision the line needs".
        var agent = new ScriptedLineAgent();
        var action = await agent.ChoosePriorityActionAsync(OwnMainWindow());
        action.Should().BeOfType<PriorityAction.PassAction>();
    }

    [Fact]
    public async Task OutsideOwnMainPhase_HoldsScript_AndPasses()
    {
        // The script must not fire in upkeep / draw / on the opponent's turn —
        // it holds for the controller's own sorcery-speed main window (CR
        // 116.2a). A scripted action stays queued while passing in upkeep.
        var fired = 0;
        var agent = new ScriptedLineAgent();
        agent.Then(_ => { fired++; return PriorityAction.Pass; });

        var upkeep = OwnWindow(StepStateType.Upkeep);
        var action = await agent.ChoosePriorityActionAsync(upkeep);

        action.Should().BeOfType<PriorityAction.PassAction>();
        fired.Should().Be(0, "the scripted step must not fire outside the main phase");
        agent.PendingSteps.Should().Be(1, "the step stays queued for the main window");
    }

    [Fact]
    public async Task ScriptedActions_DequeueInOrder_ThenPass()
    {
        var agent = new ScriptedLineAgent();
        agent.Then(PriorityAction.Pass)
             .Then(_ => PriorityAction.Pass);

        agent.PendingSteps.Should().Be(2);
        await agent.ChoosePriorityActionAsync(OwnMainWindow());
        await agent.ChoosePriorityActionAsync(OwnMainWindow());
        agent.ScriptExhausted.Should().BeTrue();

        // Drained → idles by passing.
        var idle = await agent.ChoosePriorityActionAsync(OwnMainWindow());
        idle.Should().BeOfType<PriorityAction.PassAction>();
    }

    // A GameContext where Self is the active player, in a main phase, empty
    // stack — the sorcery-speed window the scripted line runs in.
    private static GameContext OwnMainWindow() => OwnWindow(StepStateType.PreCombatMain);

    private static GameContext OwnWindow(StepStateType step)
    {
        var me = new Player("Me", 20);
        var opp = new Player("Opp", 20);
        return new GameContext(
            self: me,
            allPlayers: new[] { me, opp },
            activePlayer: me,
            turnNumber: 1,
            currentPhase: step,
            stack: new Majik.Core.Stack.Stack());
    }
}
