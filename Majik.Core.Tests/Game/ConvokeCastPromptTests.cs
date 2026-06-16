using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Costs;
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
/// CR 702.51 — cast-time Convoke creature-tap prompt. A spell carrying a
/// <see cref="KeywordAbility"/>("Convoke") marker that is cast WITHOUT a
/// caller-pre-built <see cref="ConvokeAdditionalCost"/> now triggers
/// <see cref="SpellCastFlow"/>'s in-flow prompt: the caster's agent is asked
/// (via the declarative <see cref="IPlayerAgent.ChooseAsync"/> sink, a
/// <see cref="ChoiceKind.PickN"/> over the caster's untapped creatures) which
/// creatures to tap. The chosen creatures are tapped (CR 702.51a) and the
/// printed cost is reduced by one pip each — generic first, then a coloured
/// pip matching the creature's colour (CR 702.51b) — before the mana payment
/// is requested.
///
/// <para>This pins the engine-driven Convoke path (the previously-deferred
/// "agent prompt to choose which creatures to tap" — see
/// <see cref="ConvokeAdditionalCost"/>'s xmldoc), distinct from the bot's
/// pre-selection probe (<see cref="ConvokeAltCostProbe"/>) which still
/// supplies a ready-built cost.</para>
/// </summary>
public class ConvokeCastPromptTests
{
    private readonly EventBus _bus = new();
    private readonly Majik.Core.Stack.Stack _stack;
    private readonly ZoneService _zones;
    private readonly SpellCastFlow _flow;
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    public ConvokeCastPromptTests()
    {
        _stack = new Majik.Core.Stack.Stack(_bus);
        _zones = new ZoneService(_bus);
        _flow = new SpellCastFlow(_stack, _zones, _bus);
    }

    private GameContext NewContext() =>
        new(_alice, new[] { _alice, _bob }, _alice, 1, StepStateType.PreCombatMain, _stack);

    /// <summary>A creature spell carrying the Convoke marker, in Alice's hand.</summary>
    private Creature ConvokeSpell(string name, string manaCost)
    {
        var c = new Creature(name, manaCost, 2, 2) { Owner = _alice, Zone = ZoneType.Hand };
        c.SetController(_alice);
        c.AddAbility(new KeywordAbility("Convoke", c, _alice));
        _alice.Zones.Hand.AddCard(c);
        return c;
    }

    /// <summary>An untapped creature Alice controls (a valid convoke tapper).</summary>
    private Creature Tapper(string name, string manaCost)
    {
        var c = new Creature(name, manaCost, 2, 2) { Owner = _alice, Zone = ZoneType.Battlefield };
        c.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(c);
        return c;
    }

    [Fact]
    public async Task ConvokeSpell_PromptsForCreatures_TapsThem_AndReducesCost()
    {
        // Markov Baron analogue — {2}{B}. Two untapped creatures available.
        var baron = ConvokeSpell("Convoke Bear", "2B");
        var t1 = Tapper("Token A", "G");
        var t2 = Tapper("Token B", "G");

        var agent = new ScriptedAgent();
        agent.QueueMana(ManaPayment.Empty);
        // Convoke prompt: tap both creatures.
        agent.QueueChoice(candidates => candidates);

        ManaCost? askedCost = null;

        var def = new SpellDefinition(
            Modes: System.Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: System.Array.Empty<TargetRequest>(),
            EffectFactory: _ => System.Array.Empty<IEffect>());

        await _flow.CastAsync(_alice, baron, def, agent, NewContext(),
            payManaCost: cost => { askedCost = cost; return true; });

        // CR 702.51a — both creatures tapped.
        t1.IsTapped.Should().BeTrue();
        t2.IsTapped.Should().BeTrue();

        // CR 702.51b — {2}{B} with two generic-eligible taps → {0}{B}.
        askedCost.Should().NotBeNull();
        askedCost!.Generic.Should().Be(0);
        askedCost.Black.Should().Be(1);

        _stack.Count.Should().Be(1);
    }

    [Fact]
    public async Task ConvokeSpell_DeclinedPrompt_TapsNothing_PaysPrintedCost()
    {
        var baron = ConvokeSpell("Convoke Bear", "2B");
        var t1 = Tapper("Token A", "G");

        var agent = new ScriptedAgent();
        agent.QueueMana(ManaPayment.Empty);
        // Decline convoke — tap no creatures.
        agent.QueueChoice(_ => System.Array.Empty<object>());

        ManaCost? askedCost = null;
        var def = new SpellDefinition(
            Modes: System.Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: System.Array.Empty<TargetRequest>(),
            EffectFactory: _ => System.Array.Empty<IEffect>());

        await _flow.CastAsync(_alice, baron, def, agent, NewContext(),
            payManaCost: cost => { askedCost = cost; return true; });

        t1.IsTapped.Should().BeFalse();
        askedCost.Should().Be(ManaCost.Parse("2B"));
        _stack.Count.Should().Be(1);
    }

    [Fact]
    public async Task ConvokeSpell_ColouredTap_ReducesMatchingColouredPip()
    {
        // {1}{B}, tap a Black creature → {1} generic eaten? no: generic-first
        // means the single tap eats the {1}, leaving {B}.
        var baron = ConvokeSpell("Convoke Bear", "1B");
        var black = Tapper("Black Token", "B");

        var agent = new ScriptedAgent();
        agent.QueueMana(ManaPayment.Empty);
        agent.QueueChoice(candidates => candidates);

        ManaCost? askedCost = null;
        var def = new SpellDefinition(
            Modes: System.Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: System.Array.Empty<TargetRequest>(),
            EffectFactory: _ => System.Array.Empty<IEffect>());

        await _flow.CastAsync(_alice, baron, def, agent, NewContext(),
            payManaCost: cost => { askedCost = cost; return true; });

        black.IsTapped.Should().BeTrue();
        askedCost!.Generic.Should().Be(0);
        askedCost.Black.Should().Be(1);
        _stack.Count.Should().Be(1);
    }

    [Fact]
    public async Task NonConvokeSpell_NoPrompt_NoCreaturesTapped()
    {
        // A plain creature (no Convoke marker) must NOT trigger the prompt —
        // the queued choice selector is left untouched.
        var bear = new Creature("Plain Bear", "2B", 2, 2) { Owner = _alice, Zone = ZoneType.Hand };
        bear.SetController(_alice);
        _alice.Zones.Hand.AddCard(bear);
        var t1 = Tapper("Token A", "G");

        var agent = new ScriptedAgent();
        agent.QueueMana(ManaPayment.Empty);

        ManaCost? askedCost = null;
        var def = new SpellDefinition(
            Modes: System.Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: System.Array.Empty<TargetRequest>(),
            EffectFactory: _ => System.Array.Empty<IEffect>());

        await _flow.CastAsync(_alice, bear, def, agent, NewContext(),
            payManaCost: cost => { askedCost = cost; return true; });

        t1.IsTapped.Should().BeFalse();
        askedCost.Should().Be(ManaCost.Parse("2B"));
    }

    [Fact]
    public async Task ConvokeSpell_PreSuppliedCost_DoesNotDoublePrompt()
    {
        // When the caller already supplies a ConvokeAdditionalCost (the bot
        // probe path), the flow must NOT prompt again — the pre-built cost
        // wins and the agent's choice selector is left untouched.
        var baron = ConvokeSpell("Convoke Bear", "2B");
        var t1 = Tapper("Token A", "G");
        var t2 = Tapper("Token B", "G");

        var preBuilt = new ConvokeAdditionalCost(baron, new[] { t1 });

        var agent = new ScriptedAgent();
        agent.QueueMana(ManaPayment.Empty);

        ManaCost? askedCost = null;
        var def = new SpellDefinition(
            Modes: System.Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: System.Array.Empty<TargetRequest>(),
            EffectFactory: _ => System.Array.Empty<IEffect>());

        await _flow.CastAsync(_alice, baron, def, agent, NewContext(),
            additionalCosts: new[] { preBuilt },
            payManaCost: cost => { askedCost = cost; return true; });

        // Only the pre-supplied creature is tapped (one pip reduction).
        t1.IsTapped.Should().BeTrue();
        t2.IsTapped.Should().BeFalse();
        askedCost.Should().Be(ManaCost.Parse("1B"));
    }
}
