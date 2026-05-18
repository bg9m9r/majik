using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.Spells;
using Majik.Core.StateMachine;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.Game;

/// <summary>
/// Rule 601 spell-casting steps run via agent prompts in order:
/// modes → X → targets → mana → push to stack.
/// </summary>
public class SpellCastFlowTests
{
    private readonly EventBus _bus = new();
    private readonly Majik.Core.Stack.Stack _stack;
    private readonly ZoneService _zones;
    private readonly SpellCastFlow _flow;
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    public SpellCastFlowTests()
    {
        _stack = new Majik.Core.Stack.Stack(_bus);
        _zones = new ZoneService(_bus);
        _flow = new SpellCastFlow(_stack, _zones, _bus);
    }

    [Fact]
    public async Task VanillaSpell_NoPrompts_LandsOnStack_FiresSpellCastEvent()
    {
        var bolt = new Instant("Bolt", "R") { Owner = _alice, Zone = ZoneType.Hand };
        var agent = new ScriptedAgent();
        agent.QueueMana(ManaPayment.Empty);

        SpellCastEvent? cast = null;
        _bus.Subscribe<SpellCastEvent>(e => cast = e);

        var spell = await _flow.CastAsync(_alice, bolt,
            SpellDefinition.Vanilla(_ => Array.Empty<IEffect>()),
            agent, NewContext());

        bolt.Zone.Should().Be(ZoneType.Stack);
        _stack.Count.Should().Be(1);
        _stack.Top.Should().BeSameAs(spell);
        cast.Should().NotBeNull();
    }

    [Fact]
    public async Task TargetedSpell_PromptsForTargets_InOrder_AttachesTargets()
    {
        var bear = new Creature("Bear", "1G", 2, 2) { Owner = _alice };
        var bolt = new Instant("Bolt", "R") { Owner = _alice, Zone = ZoneType.Hand };
        var agent = new ScriptedAgent();
        agent.QueueTargets(new[] { (object)bear });
        agent.QueueMana(ManaPayment.Empty);

        var capturedTargets = new List<object>();
        var def = new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: new[]
            {
                new TargetRequest("creature", 1, 1, new[] { (object)bear }),
            },
            EffectFactory: p =>
            {
                capturedTargets.AddRange(p.Targets.SelectMany(t => t));
                return Array.Empty<IEffect>();
            });

        var spell = await _flow.CastAsync(_alice, bolt, def, agent, NewContext());

        capturedTargets.Should().ContainSingle().Which.Should().BeSameAs(bear);
        spell.Should().NotBeNull();
    }

    [Fact]
    public async Task XSpell_PromptsForX_PassesValueToEffectFactory()
    {
        var fireball = new Instant("Fireball", "X{R}") { Owner = _alice, Zone = ZoneType.Hand };
        var agent = new ScriptedAgent();
        agent.QueueX(3);
        agent.QueueMana(ManaPayment.Empty);

        int? capturedX = null;
        var def = new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: true,
            TargetRequests: Array.Empty<TargetRequest>(),
            EffectFactory: p => { capturedX = p.X; return Array.Empty<IEffect>(); });

        await _flow.CastAsync(_alice, fireball, def, agent, NewContext());

        capturedX.Should().Be(3);
    }

    [Fact]
    public async Task ModalSpell_PromptsForMode_PassesIndexToEffectFactory()
    {
        var dual = new Instant("Dual", "1U") { Owner = _alice, Zone = ZoneType.Hand };
        var agent = new ScriptedAgent();
        agent.QueueMode(1);
        agent.QueueMana(ManaPayment.Empty);

        int? capturedMode = null;
        var def = new SpellDefinition(
            Modes: new[] { "draw 2", "counter target spell" },
            HasVariableX: false,
            TargetRequests: Array.Empty<TargetRequest>(),
            EffectFactory: p => { capturedMode = p.ModeIndex; return Array.Empty<IEffect>(); });

        await _flow.CastAsync(_alice, dual, def, agent, NewContext());

        capturedMode.Should().Be(1);
    }

    private GameContext NewContext() =>
        new(_alice, new[] { _alice, _bob }, _alice, 1, PhaseStateType.Main, _stack);
}
