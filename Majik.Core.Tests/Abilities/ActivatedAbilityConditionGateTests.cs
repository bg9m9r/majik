using System.Linq;
using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Players;
using Majik.Core.Rules;
using Majik.Core.Services;
using Xunit;

namespace Majik.Core.Tests.Abilities;

/// <summary>
/// CR 602.5c / 605.1a — general "Activate only if &lt;condition&gt;"
/// activation restriction on an ordinary activated ability. Distinct from
/// the timing-only <see cref="IActivatedAbility.IsSorcerySpeed"/> rider:
/// this gates on an arbitrary game-state predicate (Mox Opal Metalcraft,
/// Shifting Woodland's Delirium, Nettle Sentinel's untap condition, …).
/// </summary>
public class ActivatedAbilityConditionGateTests
{
    private readonly Player _alice = new("Alice", 20);

    private ActivatedAbility GatedAbility(Func<bool> gate)
    {
        var land = new Land("Gated Land");
        land.SetOwner(_alice);
        land.SetController(_alice);
        return new ActivatedAbility(
            source: land,
            controller: _alice,
            canActivateCheck: gate);
    }

    [Fact]
    public void NoGate_DefaultsToActivatable()
    {
        var ab = new ActivatedAbility(source: new Land("L"), controller: _alice);
        ab.CanActivateNow().Should().BeTrue();
    }

    [Fact]
    public void Gate_True_Activatable()
    {
        var ab = GatedAbility(() => true);
        ab.CanActivateNow().Should().BeTrue();
    }

    [Fact]
    public void Gate_False_NotActivatable()
    {
        var ab = GatedAbility(() => false);
        ab.CanActivateNow().Should().BeFalse();
    }

    [Fact]
    public void Validator_RejectsActivation_WhenGateFalse()
    {
        var ab = GatedAbility(() => false);
        var action = new ActivateAbilityAction(ab, _alice);
        var r = new ActionValidator().ValidateAction(action);
        r.IsValid.Should().BeFalse();
        r.ErrorMessage.Should().Contain("Activate only");
    }

    [Fact]
    public void Validator_AllowsActivation_WhenGateTrue()
    {
        var ab = GatedAbility(() => true);
        var action = new ActivateAbilityAction(ab, _alice);
        new ActionValidator().ValidateAction(action).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Activator_ThrowsActivation_WhenGateFalse()
    {
        var stack = new Majik.Core.Stack.Stack();
        var activator = new AbilityActivator(stack);
        var ab = GatedAbility(() => false);

        activator.CanActivate(ab, _alice).Should().BeFalse();
    }

    [Fact]
    public void Gate_ReevaluatedOnEachCall()
    {
        var flag = false;
        var ab = GatedAbility(() => flag);
        ab.CanActivateNow().Should().BeFalse();
        flag = true;
        ab.CanActivateNow().Should().BeTrue();
    }

    // ── Context-aware gate (Task 3.1) ──────────────────────────────────────

    private static Majik.Core.Game.GameContext Ctx(Player self, params Player[] all) =>
        new(
            self: self,
            allPlayers: all,
            activePlayer: self,
            turnNumber: 1,
            currentPhase: null,
            stack: new Majik.Core.Stack.Stack());

    [Fact]
    public void CtxGate_Preferred_WhenContextSupplied()
    {
        // The context-aware gate reads the supplied game; here it returns true
        // iff there are any opponents.
        var bob = new Player("Bob", 20);
        var ab = new ActivatedAbility(
            source: new Land("L"),
            controller: _alice,
            canActivateCheck: () => false, // context-less would say false
            canActivateCheckCtx: g => g.Opponents.Count > 0);

        ab.CanActivateNow(Ctx(_alice, _alice, bob)).Should().BeTrue();
    }

    [Fact]
    public void CtxGate_FallsBackToContextlessGate_WhenNoContext()
    {
        // No GameContext available → the context-aware gate is skipped and the
        // context-less Func<bool> fallback decides.
        var ab = new ActivatedAbility(
            source: new Land("L"),
            controller: _alice,
            canActivateCheck: () => true,
            canActivateCheckCtx: _ => false);

        ab.CanActivateNow((Majik.Core.Game.GameContext?)null).Should().BeTrue();
        ab.CanActivateNow().Should().BeTrue();
    }

    [Fact]
    public void CtxGate_ReadsLiveState()
    {
        var bob = new Player("Bob", 20);
        var ab = new ActivatedAbility(
            source: new Land("L"),
            controller: _alice,
            canActivateCheckCtx: g => g.Opponents.Any(o => o.LifeLostThisTurn > 0));

        ab.CanActivateNow(Ctx(_alice, _alice, bob)).Should().BeFalse();
        bob.LoseLife(2);
        ab.CanActivateNow(Ctx(_alice, _alice, bob)).Should().BeTrue();
    }

    [Fact]
    public void CtxGate_MirroredOntoStackCopy_ByActivator()
    {
        var bob = new Player("Bob", 20);
        bob.LoseLife(1);
        var stack = new Majik.Core.Stack.Stack();
        var activator = new AbilityActivator(stack);
        var ab = new ActivatedAbility(
            source: new Land("L"),
            controller: _alice,
            canActivateCheckCtx: g => g.Opponents.Any(o => o.LifeLostThisTurn > 0));

        activator.CanActivate(ab, _alice, Ctx(_alice, _alice, bob)).Should().BeTrue();
    }
}
