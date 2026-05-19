using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.StateMachine;
using Majik.Core.Zones;
using Xunit;

/// <summary>
/// CR 700.2 — modal spells. SpellCastFlow already threads ChooseModeAsync;
/// these tests demonstrate end-to-end behaviour with a Charm-style spell
/// that branches the effect on chosen mode.
/// </summary>
public class ModalSpellTests
{
    private readonly EventBus _bus = new();
    private readonly Majik.Core.Stack.Stack _stack;
    private readonly SpellCastFlow _flow;
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    public ModalSpellTests()
    {
        _stack = new Majik.Core.Stack.Stack(_bus);
        _flow = new SpellCastFlow(_stack, new ZoneService(_bus), _bus);
    }

    [Fact]
    public async Task Charm_PicksMode1_GainLifeBranch()
    {
        var charm = new Instant("Charm", "1G") { Owner = _alice, Zone = ZoneType.Hand };
        _alice.Zones.Hand.AddCard(charm);

        var agent = new ScriptedAgent();
        agent.QueueMode(1); // gain 3 life
        agent.QueueMana(ManaPayment.Empty);

        var def = new SpellDefinition(
            Modes: new[] { "Deal 2 damage to any target", "You gain 3 life", "Draw a card" },
            HasVariableX: false,
            TargetRequests: System.Array.Empty<TargetRequest>(),
            EffectFactory: p => new IEffect[] { new Effect("modal effect", () =>
            {
                switch (p.ModeIndex)
                {
                    case 0: _bob.LoseLife(2); break;
                    case 1: _alice.GainLife(3); break;
                    case 2: /* draw stub */ break;
                }
            }) });

        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _alice, 1, PhaseStateType.Main, _stack);

        var spell = await _flow.CastAsync(_alice, charm, def, agent, ctx);
        spell.Resolve();

        _alice.LifeTotal.Should().Be(23);
        _bob.LifeTotal.Should().Be(20);
    }

    [Fact]
    public async Task Charm_PicksMode0_DamageBranch()
    {
        var charm = new Instant("Charm", "1G") { Owner = _alice, Zone = ZoneType.Hand };
        _alice.Zones.Hand.AddCard(charm);

        var agent = new ScriptedAgent();
        agent.QueueMode(0);
        agent.QueueMana(ManaPayment.Empty);

        var def = new SpellDefinition(
            Modes: new[] { "Deal 2 damage to any target", "You gain 3 life", "Draw a card" },
            HasVariableX: false,
            TargetRequests: System.Array.Empty<TargetRequest>(),
            EffectFactory: p => new IEffect[] { new Effect("modal effect", () =>
            {
                switch (p.ModeIndex)
                {
                    case 0: _bob.LoseLife(2); break;
                    case 1: _alice.GainLife(3); break;
                }
            }) });

        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _alice, 1, PhaseStateType.Main, _stack);

        var spell = await _flow.CastAsync(_alice, charm, def, agent, ctx);
        spell.Resolve();

        _bob.LifeTotal.Should().Be(18);
        _alice.LifeTotal.Should().Be(20);
    }
}
