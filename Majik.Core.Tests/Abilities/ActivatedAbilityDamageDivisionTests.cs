using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.StateMachine;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.Abilities;

/// <summary>
/// CR 601.2d / CR 119.4 — the divide-damage prompt for an ACTIVATED / loyalty
/// ability (the activated-ability analogue of the cast-time
/// <see cref="SpellCastFlow"/> seam and the triggered-ability
/// <see cref="TriggerManager"/> seam). An activated ability that "deals N damage
/// divided as you choose among …" declares a <see cref="DamageDivisionSpec"/>;
/// the live dispatcher (TurnDriver / GameFacade DispatchActivate +
/// DispatchLoyalty) prompts the activating player's agent for the per-target
/// split right after targets are chosen, records it via
/// <see cref="ActivatedAbility.SetChosenDamageDivision"/>, and
/// <see cref="AbilityActivator"/> mirrors it onto the stack copy so resolution
/// deals the announced amounts via <see cref="ResolutionContext.DamageDivision"/>
/// instead of an even split.
/// </summary>
public class ActivatedAbilityDamageDivisionTests
{
    private readonly EventBus _bus = new();
    private readonly Majik.Core.Stack.Stack _stack;
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    public ActivatedAbilityDamageDivisionTests()
    {
        _stack = new Majik.Core.Stack.Stack(_bus);
    }

    private GameContext Ctx() =>
        new(_alice, new[] { _alice, _bob }, _alice, 1, StepStateType.PreCombatMain, _stack);

    private ActivatedAbility BuildDividedDamageAbility(Creature source, int total)
    {
        var effect = new Effect(
            "deal divided damage",
            rc => { Majik.Core.Primitives.Fx.DealDividedDamageAny(rc, total, source); return ValueTask.CompletedTask; });
        return new ActivatedAbility(
            source: source,
            controller: _alice,
            costs: null,
            effects: new IEffect[] { effect },
            targetRequests: new[] { new TargetRequest("any target", 1, 3, System.Array.Empty<object>()) },
            damageDivision: new DamageDivisionSpec(total, TargetSlotIndex: 0));
    }

    [Fact]
    public void DamageDivisionSpec_CarriedOnAbility()
    {
        var source = new Creature("Titan", "4RR", 6, 6) { Owner = _alice };
        var ability = BuildDividedDamageAbility(source, 3);

        ability.DamageDivision.Should().NotBeNull();
        ability.DamageDivision!.TotalDamage.Should().Be(3);
        ability.ChosenDamageDivision.Should().BeNull("nothing announced yet");
    }

    [Fact]
    public void SetChosenDamageDivision_RecordsSplit()
    {
        var source = new Creature("Titan", "4RR", 6, 6) { Owner = _alice };
        var ability = BuildDividedDamageAbility(source, 3);

        var split = new[]
        {
            new DamageAllocation(_alice, 0, 1),
            new DamageAllocation(_bob, 1, 2),
        };
        ability.SetChosenDamageDivision(split);

        ability.ChosenDamageDivision.Should().BeEquivalentTo(split);
    }

    [Fact]
    public void RebindTo_PreservesDamageDivisionSpec()
    {
        var source = new Creature("Titan", "4RR", 6, 6) { Owner = _alice };
        var ability = BuildDividedDamageAbility(source, 3);
        var newSource = new Creature("Other", "4RR", 6, 6) { Owner = _bob };

        var rebound = ability.RebindTo(newSource, _bob);

        rebound.DamageDivision.Should().NotBeNull();
        rebound.DamageDivision!.TotalDamage.Should().Be(3);
        // CR 601.2d — the per-activation chosen split is NOT carried over (it is
        // re-announced per activation); only the declarative spec survives.
        rebound.ChosenDamageDivision.Should().BeNull();
    }

    [Fact]
    public async Task AbilityActivator_MirrorsChosenDivision_OntoStackObject()
    {
        var source = new Creature("Titan", "4RR", 6, 6)
            { Owner = _alice, Controller = _alice, Zone = ZoneType.Battlefield };
        var ability = BuildDividedDamageAbility(source, 3);

        // Record the announced split on the SOURCE ability (as the dispatcher does
        // after prompting the agent), then activate. AbilityActivator must copy it
        // onto the stack object (CR 602.4) so resolution reads it.
        ability.SetChosenTargets(new IReadOnlyList<object>[] { new object[] { _alice, _bob } });
        ability.SetChosenDamageDivision(new[]
        {
            new DamageAllocation(_alice, 0, 1),
            new DamageAllocation(_bob, 1, 2),
        });

        var activator = new AbilityActivator(_stack, _bus);
        activator.ActivateAbility(ability, _alice, targets: null, costs: null, game: Ctx());

        _stack.Count.Should().Be(1);
        var stackObject = (ActivatedAbility)_stack.Top!;
        stackObject.ChosenDamageDivision.Should().NotBeNull();
        stackObject.ChosenDamageDivision!.Should().HaveCount(2);

        var aliceL = _alice.LifeTotal;
        var bobL = _bob.LifeTotal;
        await stackObject.ResolveAsync(agent: null, game: Ctx());

        _alice.LifeTotal.Should().Be(aliceL - 1, "the announced split assigned Alice 1");
        _bob.LifeTotal.Should().Be(bobL - 2, "the announced split assigned Bob 2");
    }

    [Fact]
    public async Task ResolveAsync_NoChosenDivision_FallsBackToEvenSplit()
    {
        // No SetChosenDamageDivision (the no-agent dispatcher path / single
        // target). Fx.DealDividedDamageAny degenerates to the even split:
        // 3 among two targets → 2/1 (remainder front-loaded).
        var source = new Creature("Titan", "4RR", 6, 6)
            { Owner = _alice, Controller = _alice, Zone = ZoneType.Battlefield };
        var ability = BuildDividedDamageAbility(source, 3);
        ability.SetChosenTargets(new IReadOnlyList<object>[] { new object[] { _alice, _bob } });

        var aliceL = _alice.LifeTotal;
        var bobL = _bob.LifeTotal;
        await ability.ResolveAsync(agent: null, game: Ctx());

        _alice.LifeTotal.Should().Be(aliceL - 2, "even split front-loads the remainder");
        _bob.LifeTotal.Should().Be(bobL - 1);
    }

    [Fact]
    public async Task DispatchPrompt_PromptsAgent_AndDealsAnnouncedSplit()
    {
        // Exercise the shared dispatch-prompt helper (DamageDivisionDefaults.PromptAsync)
        // the activated/loyalty dispatchers call: it prompts the agent and
        // produces the per-target allocations the resolution deals verbatim.
        var source = new Creature("Titan", "4RR", 6, 6)
            { Owner = _alice, Controller = _alice, Zone = ZoneType.Battlefield };
        var ability = BuildDividedDamageAbility(source, 3);

        var agent = new ScriptedAgent();
        agent.QueueDamageDivision(1, 2); // Alice 1, Bob 2 — NOT the 2/1 even split

        IReadOnlyList<object> targets = new object[] { _alice, _bob };
        var division = await DamageDivisionDefaults.PromptAsync(
            agent, Ctx(), source, ability.DamageDivision!.TotalDamage, targets);

        division.Should().NotBeNull();
        ability.SetChosenTargets(new[] { targets });
        ability.SetChosenDamageDivision(division);

        var aliceL = _alice.LifeTotal;
        var bobL = _bob.LifeTotal;
        await ability.ResolveAsync(agent: agent, game: Ctx());

        _alice.LifeTotal.Should().Be(aliceL - 1, "agent announced Alice 1");
        _bob.LifeTotal.Should().Be(bobL - 2, "agent announced Bob 2");
    }
}
