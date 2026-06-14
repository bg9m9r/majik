using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Primitives;
using Majik.Core.StateMachine;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.Primitives;

/// <summary>
/// CR 118.4 / CR 701.5 — the "counter target spell unless its controller pays
/// {N}" rider (<see cref="PayUnlessCounterRider"/>) is the Cancel-family
/// pay-or-counter primitive. These tests pin the new <b>real agent prompt</b>:
/// at resolution the engine asks the TARGET SPELL'S CONTROLLER (not the
/// counterspell caster) whether to pay {N}; on "no" the spell is countered, on
/// "yes" + affordable it stays. The legacy / shape-only synchronous path with
/// no live decision surface keeps the deterministic "pay if able" posture so
/// pre-existing factory-direct tests stay green.
/// </summary>
public class PayUnlessCounterRiderTests : IDisposable
{
    private readonly Player _alice = new("Alice", 20);   // counterspell caster
    private readonly Player _bob = new("Bob", 20);       // pays to keep his spell

    public PayUnlessCounterRiderTests() => AgentRegistry.Clear();
    public void Dispose() => AgentRegistry.Clear();

    private static GameContext Ctx(Player active, Player a, Player b, Majik.Core.Stack.Stack stack)
        => new(active, new[] { a, b }, active, 1, StepStateType.PreCombatMain, stack);

    private (Majik.Core.Stack.Stack stack, Majik.Core.Spells.Spell bobSpell, Instant bolt) PushBobSpell()
    {
        var stack = new Majik.Core.Stack.Stack();
        var bolt = new Instant("Lightning Bolt", "{R}") { Owner = _bob, Controller = _bob };
        bolt.SetZone(ZoneType.Stack);
        var bobSpell = new Majik.Core.Spells.Spell(bolt, _bob);
        stack.Push(bobSpell);
        return (stack, bobSpell, bolt);
    }

    // -----------------------------------------------------------------------
    // The NEW behaviour: a real agent prompt routed to the PAYING player.
    // -----------------------------------------------------------------------

    [Fact]
    public async Task AsyncResolve_PayingPlayerDeclinesPrompt_CountersEvenThoughAffordable()
    {
        var (stack, bobSpell, bolt) = PushBobSpell();
        // Bob CAN afford the {1} tithe, but his agent chooses NOT to pay.
        _bob.AddManaToPool(ManaCost.Zero.AddGenericCost(3));

        var bobAgent = new ScriptedAgent();
        bobAgent.QueueYesNo(false);                 // "No, don't pay."
        AgentRegistry.Set(_bob, bobAgent);

        var effect = PayUnlessCounterRider.Build(
            "test rider", stack, () => bobSpell, unlessPayN: 1);

        // Resolution context belongs to ALICE (the counterspell caster).
        var rc = ResolutionContext.For(
            _alice, agent: new ScriptedAgent(), game: Ctx(_alice, _alice, _bob, stack),
            chosenTargets: null);

        await effect.ExecuteAsync(rc);

        stack.GetAll().Should().NotContain(bobSpell,
            "Bob's agent declined to pay, so the spell is countered (CR 701.5)");
        bolt.Zone.Should().Be(ZoneType.Graveyard);
        _bob.ManaPool.CanPay(ManaCost.Zero.AddGenericCost(3)).Should().BeTrue(
            "no mana was spent because Bob declined");
    }

    [Fact]
    public async Task AsyncResolve_PayingPlayerAcceptsPrompt_PaysAndSpellSurvives()
    {
        var (stack, bobSpell, bolt) = PushBobSpell();
        _bob.AddManaToPool(ManaCost.Zero.AddGenericCost(1));

        var bobAgent = new ScriptedAgent();
        bobAgent.QueueYesNo(true);                  // "Yes, pay {1}."
        AgentRegistry.Set(_bob, bobAgent);

        var effect = PayUnlessCounterRider.Build(
            "test rider", stack, () => bobSpell, unlessPayN: 1);

        var rc = ResolutionContext.For(
            _alice, agent: new ScriptedAgent(), game: Ctx(_alice, _alice, _bob, stack),
            chosenTargets: null);

        await effect.ExecuteAsync(rc);

        stack.GetAll().Should().Contain(bobSpell,
            "Bob paid {1}, so the counter no-ops and the spell stays on the stack");
        bolt.Zone.Should().Be(ZoneType.Stack);
        _bob.ManaPool.CanPay(ManaCost.Zero.AddGenericCost(1)).Should().BeFalse(
            "Bob spent his {1} paying the rider");
    }

    [Fact]
    public async Task AsyncResolve_PayingPlayerWantsToPayButCannotAfford_CountersWithoutPrompting()
    {
        var (stack, bobSpell, bolt) = PushBobSpell();
        // Bob has NO mana — even a "yes" agent can't pay.
        var bobAgent = new ScriptedAgent();
        // Intentionally queue NO yes/no answer: the affordability probe must
        // short-circuit BEFORE the prompt (so ScriptedAgent.Pop is never hit).
        AgentRegistry.Set(_bob, bobAgent);

        var effect = PayUnlessCounterRider.Build(
            "test rider", stack, () => bobSpell, unlessPayN: 1);

        var rc = ResolutionContext.For(
            _alice, agent: new ScriptedAgent(), game: Ctx(_alice, _alice, _bob, stack),
            chosenTargets: null);

        await effect.ExecuteAsync(rc);

        stack.GetAll().Should().NotContain(bobSpell,
            "Bob can't afford {1}, so the spell is countered without a prompt");
        bolt.Zone.Should().Be(ZoneType.Graveyard);
    }

    // -----------------------------------------------------------------------
    // The PRESERVED behaviour: legacy synchronous path = "pay if able".
    // -----------------------------------------------------------------------

    [Fact]
    public void SyncExecute_NoAgentNoGame_AutoPaysWhenAble()
    {
        var (stack, bobSpell, bolt) = PushBobSpell();
        _bob.AddManaToPool(ManaCost.Zero.AddGenericCost(1));

        var effect = PayUnlessCounterRider.Build(
            "test rider", stack, () => bobSpell, unlessPayN: 1);

        // Legacy synchronous Execute() — no agent, no game context.
        effect.Execute();

        stack.GetAll().Should().Contain(bobSpell,
            "shape-only / no-agent path preserves the deterministic 'pay if able' posture");
        bolt.Zone.Should().Be(ZoneType.Stack);
    }

    [Fact]
    public void SyncExecute_NoAgentNoGame_CountersWhenUnable()
    {
        var (stack, bobSpell, bolt) = PushBobSpell();
        // Bob has no mana.

        var effect = PayUnlessCounterRider.Build(
            "test rider", stack, () => bobSpell, unlessPayN: 1);

        effect.Execute();

        stack.GetAll().Should().NotContain(bobSpell);
        bolt.Zone.Should().Be(ZoneType.Graveyard);
    }

    // -----------------------------------------------------------------------
    // End-to-end through a real named factory (Mana Leak, pays {3}).
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ManaLeak_AsyncResolve_ControllerDeclines_CountersDespiteHavingMana()
    {
        var (stack, bobSpell, bolt) = PushBobSpell();
        _bob.AddManaToPool(ManaCost.Zero.AddGenericCost(3));   // can pay Mana Leak's {3}

        var bobAgent = new ScriptedAgent();
        bobAgent.QueueYesNo(false);
        AgentRegistry.Set(_bob, bobAgent);

        var def = ManaLeakFactory.BuildDefinition(o => o, stack);
        var chosen = new ChosenSpellParams(
            null, null,
            new IReadOnlyList<object>[] { new[] { (object)bobSpell } },
            ManaPayment.Empty);

        var rc = ResolutionContext.For(
            _alice, agent: new ScriptedAgent(), game: Ctx(_alice, _alice, _bob, stack),
            chosenTargets: null);

        foreach (var e in def.EffectFactory(chosen)) await e.ExecuteAsync(rc);

        bolt.Zone.Should().Be(ZoneType.Graveyard,
            "Mana Leak's controller declined to pay {3}, so the spell is countered");
    }
}
