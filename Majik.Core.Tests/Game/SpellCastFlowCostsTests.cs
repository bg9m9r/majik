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
using Creature = Majik.Core.Cards.Creature;

public class SpellCastFlowCostsTests
{
    private readonly EventBus _bus = new();
    private readonly Majik.Core.Stack.Stack _stack;
    private readonly SpellCastFlow _flow;
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    public SpellCastFlowCostsTests()
    {
        _stack = new Majik.Core.Stack.Stack(_bus);
        _flow = new SpellCastFlow(_stack, new ZoneService(_bus), _bus);
    }

    [Fact]
    public async Task AdditionalCost_Sacrifice_PaidBeforeSpellHitsStack()
    {
        var bear = new Creature("Bear", "1G", 2, 2)
        { Owner = _alice, Controller = _alice, Zone = ZoneType.Battlefield };
        _alice.Zones.Battlefield.AddCard(bear);

        var spell = new Instant("Carnage Charm", "B") { Owner = _alice, Zone = ZoneType.Hand };
        _alice.Zones.Hand.AddCard(spell);

        var agent = new ScriptedAgent();
        agent.QueueMana(ManaPayment.Empty);

        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _alice, 1, PhaseStateType.Main, _stack);

        await _flow.CastAsync(
            _alice, spell,
            SpellDefinition.Vanilla(_ => System.Array.Empty<IEffect>()),
            agent, ctx,
            additionalCosts: new[] { new SacrificeCreatureCost(bear) });

        bear.Zone.Should().Be(ZoneType.Graveyard);
        _stack.Count.Should().Be(1);
    }

    [Fact]
    public async Task FlashbackCost_CastsFromGraveyard_ExilesOnResolve()
    {
        var firebolt = new Instant("Firebolt", "R") { Owner = _alice, Zone = ZoneType.Graveyard };
        _alice.Zones.Graveyard.AddCard(firebolt);

        var agent = new ScriptedAgent();
        agent.QueueMana(ManaPayment.Empty);

        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _alice, 1, PhaseStateType.Main, _stack);

        var ran = false;
        var spell = await _flow.CastAsync(
            _alice, firebolt,
            new SpellDefinition(
                Modes: System.Array.Empty<string>(), HasVariableX: false,
                TargetRequests: System.Array.Empty<TargetRequest>(),
                EffectFactory: _ => new IEffect[] { new Effect("dmg", () => { _bob.LoseLife(3); ran = true; }) }),
            agent, ctx,
            alternativeCost: new FlashbackAlternativeCost(ManaCost.Parse("4R")));

        // Spell on stack now in Stack zone.
        firebolt.Zone.Should().Be(ZoneType.Stack);
        spell.Resolve();

        ran.Should().BeTrue();
        _bob.LifeTotal.Should().Be(17);
        // Flashback cleanup effect runs as part of Resolve.
        firebolt.Zone.Should().Be(ZoneType.Exile);
    }
}
