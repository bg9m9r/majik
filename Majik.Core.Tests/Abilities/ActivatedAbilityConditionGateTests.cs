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
}
