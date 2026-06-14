using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.SpellTemplates.Templates.Damage;
using Majik.Core.Cards;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.StateMachine;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.Game;

/// <summary>
/// CR 601.2d / CR 119.4 — the real divide-damage prompt. A "deals N damage
/// divided as you choose among …" spell declares a <see cref="DamageDivisionSpec"/>
/// on its bound <see cref="SpellDefinition"/>; <see cref="SpellCastFlow"/>
/// prompts the caster's agent (<see cref="IPlayerAgent.ChooseDamageDivisionAsync"/>)
/// at cast time, records the announced split on
/// <see cref="ChosenSpellParams.DamageDivision"/>, and the deal-damage effect
/// deals the announced amounts — replacing the even-split stand-in.
/// </summary>
public class DivideDamagePromptTests
{
    private readonly EventBus _bus = new();
    private readonly Majik.Core.Stack.Stack _stack;
    private readonly SpellCastFlow _flow;
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);
    private readonly Player _carol = new("Carol", 20);

    public DivideDamagePromptTests()
    {
        _stack = new Majik.Core.Stack.Stack(_bus);
        _flow = new SpellCastFlow(_stack, new ZoneService(_bus), _bus);
    }

    private SpellDefinition Def() =>
        DamageSpellFactory.DamageDividedAmongAnyTargetsSpell(
            n: 3, maxTargets: 3, resolver: o => o!,
            replacements: null, caster: _alice, bus: _bus);

    private GameContext Ctx() =>
        new(_alice, new[] { _alice, _bob, _carol }, _alice, 1, StepStateType.PreCombatMain, _stack);

    [Fact]
    public async Task CasterAnnouncedSplit_IsDealtVerbatim()
    {
        // Arc Lightning analogue: 3 damage divided among Bob + Carol. The
        // caster announces 2 on Bob, 1 on Carol — NOT the even split (which
        // for two targets would also be 2/1, so use a skew the even split
        // would never produce: all 3 on Bob requires a single target, so use
        // Bob 1 / Carol 2 — the OPPOSITE of the front-loaded even split).
        var spell = new Sorcery("Arc Lightning", "{2}{R}") { Owner = _alice, Zone = ZoneType.Hand };
        _alice.Zones.Hand.AddCard(spell);

        var agent = new ScriptedAgent();
        agent.QueueTargets(new object[] { _bob, _carol });
        agent.QueueDamageDivision(1, 2); // Bob 1, Carol 2
        agent.QueueMana(ManaPayment.Empty);

        var bobL = _bob.LifeTotal;
        var carolL = _carol.LifeTotal;

        var cast = await _flow.CastAsync(_alice, spell, Def(), agent, Ctx());
        cast.Resolve();

        _bob.LifeTotal.Should().Be(bobL - 1, "caster announced 1 on Bob");
        _carol.LifeTotal.Should().Be(carolL - 2, "caster announced 2 on Carol");
    }

    [Fact]
    public async Task DefaultEvenSplit_FrontLoadsRemainder()
    {
        // No QueueDamageDivision → the agent default (even split, remainder
        // front-loaded). 3 among two → Bob 2, Carol 1.
        var spell = new Sorcery("Arc Lightning", "{2}{R}") { Owner = _alice, Zone = ZoneType.Hand };
        _alice.Zones.Hand.AddCard(spell);

        var agent = new ScriptedAgent();
        agent.QueueTargets(new object[] { _bob, _carol });
        agent.QueueMana(ManaPayment.Empty);

        var bobL = _bob.LifeTotal;
        var carolL = _carol.LifeTotal;

        var cast = await _flow.CastAsync(_alice, spell, Def(), agent, Ctx());
        cast.Resolve();

        _bob.LifeTotal.Should().Be(bobL - 2, "even split front-loads the remainder");
        _carol.LifeTotal.Should().Be(carolL - 1);
    }

    [Fact]
    public async Task SingleTarget_AllDamageToIt()
    {
        var spell = new Sorcery("Arc Lightning", "{2}{R}") { Owner = _alice, Zone = ZoneType.Hand };
        _alice.Zones.Hand.AddCard(spell);

        var agent = new ScriptedAgent();
        agent.QueueTargets(new object[] { _bob });
        // Even split of 3 among 1 → [3]; no need to queue a custom division.
        agent.QueueMana(ManaPayment.Empty);

        var bobL = _bob.LifeTotal;

        var cast = await _flow.CastAsync(_alice, spell, Def(), agent, Ctx());
        cast.Resolve();

        _bob.LifeTotal.Should().Be(bobL - 3);
    }

    [Fact]
    public async Task IllegalSplit_NormalisedToExactlyTotal()
    {
        // Agent returns an over-allocation (5 + 5 = 10 for a 3-damage spell).
        // CR 119.4 — the engine normalises to exactly 3 (each ≥ 1; surplus
        // shaved off the last target): clamp → [5, 5] → shave to [1, 2].
        var spell = new Sorcery("Arc Lightning", "{2}{R}") { Owner = _alice, Zone = ZoneType.Hand };
        _alice.Zones.Hand.AddCard(spell);

        var agent = new ScriptedAgent();
        agent.QueueTargets(new object[] { _bob, _carol });
        agent.QueueDamageDivision(5, 5);
        agent.QueueMana(ManaPayment.Empty);

        var bobL = _bob.LifeTotal;
        var carolL = _carol.LifeTotal;

        var cast = await _flow.CastAsync(_alice, spell, Def(), agent, Ctx());
        cast.Resolve();

        var dealt = (bobL - _bob.LifeTotal) + (carolL - _carol.LifeTotal);
        dealt.Should().Be(3, "the engine clamps the division to exactly the printed total (CR 119.4)");
        (_bob.LifeTotal < bobL).Should().BeTrue("each chosen target gets at least 1");
        (_carol.LifeTotal < carolL).Should().BeTrue("each chosen target gets at least 1");
    }

    [Fact]
    public async Task ChosenSpellParams_CarriesAnnouncedDivision()
    {
        // White-box: the prompt result rides on ChosenSpellParams.DamageDivision
        // index-aligned with the divided target slot.
        IReadOnlyList<DamageAllocation>? captured = null;
        var def = new SpellDefinition(
            Modes: System.Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: new[] { new TargetRequest("any target", 1, 3, System.Array.Empty<object>()) },
            EffectFactory: p =>
            {
                captured = p.DamageDivision;
                return System.Array.Empty<IEffect>();
            },
            DamageDivision: new DamageDivisionSpec(3, TargetSlotIndex: 0));

        var spell = new Sorcery("Arc Lightning", "{2}{R}") { Owner = _alice, Zone = ZoneType.Hand };
        _alice.Zones.Hand.AddCard(spell);

        var agent = new ScriptedAgent();
        agent.QueueTargets(new object[] { _bob, _carol });
        agent.QueueDamageDivision(1, 2);
        agent.QueueMana(ManaPayment.Empty);

        await _flow.CastAsync(_alice, spell, def, agent, Ctx());

        captured.Should().NotBeNull();
        captured!.Should().HaveCount(2);
        captured[0].Should().BeEquivalentTo(new { Target = _bob, TargetSlotPosition = 0, Amount = 1 });
        captured[1].Should().BeEquivalentTo(new { Target = _carol, TargetSlotPosition = 1, Amount = 2 });
    }

    [Fact]
    public void EvenSplit_Helper_FrontLoadsRemainder()
    {
        DamageDivisionDefaults.EvenSplit(5, 3).Should().Equal(2, 2, 1);
        DamageDivisionDefaults.EvenSplit(3, 2).Should().Equal(2, 1);
        DamageDivisionDefaults.EvenSplit(4, 4).Should().Equal(1, 1, 1, 1);
        DamageDivisionDefaults.EvenSplit(2, 0).Should().BeEmpty();
    }

    [Fact]
    public void Normalize_Helper_CoercesToLegalDivision()
    {
        // Wrong length → even-split fallback.
        DamageDivisionDefaults.Normalize(new[] { 5 }, 3, 2).Should().Equal(2, 1);
        // Zero entry clamped to 1, then total reconciled to 3 (deficit onto first).
        DamageDivisionDefaults.Normalize(new[] { 0, 1 }, 3, 2).Should().Equal(2, 1);
        // Over-allocation shaved off the last targets first (never below 1):
        // [5,5] clamp→[5,5], surplus 7 → shave last to 1 (−4), then first by 3
        // → [2, 1].
        DamageDivisionDefaults.Normalize(new[] { 5, 5 }, 3, 2).Should().Equal(2, 1);
        // Already legal → untouched.
        DamageDivisionDefaults.Normalize(new[] { 1, 2 }, 3, 2).Should().Equal(1, 2);
    }
}
