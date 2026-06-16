using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Primitives;
using Majik.Core.StateMachine;
using Majik.Core.ValueObjects;
using Xunit;

namespace Majik.Core.Tests.Primitives;

/// <summary>
/// CR 603.1 / CR 117.1 — the "pay {cost} unless you {consequence}" upkeep /
/// delayed-pact resolution primitive (<see cref="UpkeepPayUnlessConsequence"/>).
/// These tests pin the new <b>real agent prompt</b> at trigger resolution: the
/// controller's agent is asked "Pay {cost}?"; on "no" the consequence fires even
/// though they could afford it, on "yes" + affordable the cost is paid and the
/// consequence is skipped. The legacy / shape-only synchronous path with no live
/// decision surface keeps the deterministic "pay if able" posture so the
/// pre-existing factory-direct pact-cycle / Stasis / Kataki / Mana Vault tests
/// stay green.
/// </summary>
public class UpkeepPayUnlessConsequenceTests : IDisposable
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    public UpkeepPayUnlessConsequenceTests() => AgentRegistry.Clear();
    public void Dispose() => AgentRegistry.Clear();

    private GameContext Ctx(Majik.Core.Stack.Stack? stack = null)
        => new(_alice, new[] { _alice, _bob }, _alice, 1, StepStateType.Upkeep,
               stack ?? new Majik.Core.Stack.Stack());

    private ResolutionContext LiveCtx()
        => ResolutionContext.For(
            _alice, agent: new ScriptedAgent(), game: Ctx(), chosenTargets: null);

    // -----------------------------------------------------------------------
    // NEW behaviour: a real agent prompt routed to the paying controller.
    // -----------------------------------------------------------------------

    [Fact]
    public async Task AsyncResolve_ControllerDeclinesPrompt_ConsequenceFires_EvenThoughAffordable()
    {
        // Alice CAN pay {2}{B} but her agent says no → consequence fires.
        _alice.AddManaToPool(ManaCost.Parse("{2}{B}"));

        var aliceAgent = new ScriptedAgent();
        aliceAgent.QueueYesNo(false);                 // "No, don't pay."
        AgentRegistry.Set(_alice, aliceAgent);

        var consequenceFired = false;
        var effect = UpkeepPayUnlessConsequence.Build(
            "pact: pay {2}{B} or lose", _alice, ManaCost.Parse("{2}{B}"),
            consequence: () => consequenceFired = true);

        await effect.ExecuteAsync(LiveCtx());

        consequenceFired.Should().BeTrue("Alice declined to pay, so the 'if you don't' tail runs");
        _alice.ManaPool.Total.Should().Be(3, "no mana was spent because Alice declined");
    }

    [Fact]
    public async Task AsyncResolve_ControllerAcceptsPrompt_PaysAndConsequenceSkipped()
    {
        _alice.AddManaToPool(ManaCost.Parse("{2}{B}"));

        var aliceAgent = new ScriptedAgent();
        aliceAgent.QueueYesNo(true);                  // "Yes, pay {2}{B}."
        AgentRegistry.Set(_alice, aliceAgent);

        var consequenceFired = false;
        var effect = UpkeepPayUnlessConsequence.Build(
            "pact: pay {2}{B} or lose", _alice, ManaCost.Parse("{2}{B}"),
            consequence: () => consequenceFired = true);

        await effect.ExecuteAsync(LiveCtx());

        consequenceFired.Should().BeFalse("Alice paid, so the consequence is skipped");
        _alice.ManaPool.Total.Should().Be(0, "PayMana({2}{B}) consumed the pool");
    }

    [Fact]
    public async Task AsyncResolve_ControllerWantsToPayButCannotAfford_ConsequenceFiresWithoutPrompting()
    {
        // Alice has NO mana — even a "yes" agent can't pay. The affordability
        // probe must short-circuit BEFORE the prompt (ScriptedAgent.Pop unhit).
        var aliceAgent = new ScriptedAgent();         // no yes/no queued on purpose
        AgentRegistry.Set(_alice, aliceAgent);

        var consequenceFired = false;
        var effect = UpkeepPayUnlessConsequence.Build(
            "pact: pay {2}{B} or lose", _alice, ManaCost.Parse("{2}{B}"),
            consequence: () => consequenceFired = true);

        await effect.ExecuteAsync(LiveCtx());

        consequenceFired.Should().BeTrue("can't afford ⇒ consequence fires, no prompt");
    }

    [Fact]
    public async Task AsyncResolve_GuardFalse_NoOps_NeitherPaysNorConsequence()
    {
        // Mana Vault's "if this is tapped" intervening-if (CR 603.4) fails.
        _alice.AddManaToPool(ManaCost.Parse("{4}"));
        var aliceAgent = new ScriptedAgent();
        AgentRegistry.Set(_alice, aliceAgent);

        var consequenceFired = false;
        var effect = UpkeepPayUnlessConsequence.Build(
            "vault: pay {4} or take 1", _alice, ManaCost.Parse("{4}"),
            consequence: () => consequenceFired = true,
            guard: () => false);

        await effect.ExecuteAsync(LiveCtx());

        consequenceFired.Should().BeFalse("guard false ⇒ effect no-ops");
        _alice.ManaPool.Total.Should().Be(4, "no payment when the guard fails");
    }

    // -----------------------------------------------------------------------
    // PRESERVED behaviour: legacy synchronous path = "pay if able".
    // -----------------------------------------------------------------------

    [Fact]
    public void SyncExecute_NoAgentNoGame_AutoPaysWhenAble()
    {
        _alice.AddManaToPool(ManaCost.Parse("{2}{B}"));

        var consequenceFired = false;
        var effect = UpkeepPayUnlessConsequence.Build(
            "pact: pay {2}{B} or lose", _alice, ManaCost.Parse("{2}{B}"),
            consequence: () => consequenceFired = true);

        effect.Execute();   // legacy sync — no agent, no game

        consequenceFired.Should().BeFalse("shape-only path auto-pays when able");
        _alice.ManaPool.Total.Should().Be(0);
    }

    [Fact]
    public void SyncExecute_NoAgentNoGame_ConsequenceWhenUnable()
    {
        // Alice has no mana.
        var consequenceFired = false;
        var effect = UpkeepPayUnlessConsequence.Build(
            "pact: pay {2}{B} or lose", _alice, ManaCost.Parse("{2}{B}"),
            consequence: () => consequenceFired = true);

        effect.Execute();

        consequenceFired.Should().BeTrue("shape-only path runs the consequence when it can't pay");
    }
}
