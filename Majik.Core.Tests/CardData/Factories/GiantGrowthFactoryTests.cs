using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.StateMachine;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for Giant Growth (Limited Edition Alpha, {G}, Instant — +3/+3 EOT).
///
/// Coverage:
/// - Card identity (Instant, green, {G}, owner/controller wired).
/// - NamedCardFactory dispatcher returns the correct shape.
/// - SpellDefinition shape (1 target creature, no modes, no X).
/// - Cast + resolve: target gets +3/+3.
/// - +3/+3 expires at end of turn (CR 514.2).
/// - Fizzle: target not on battlefield → no-op (CR 608.2b).
/// </summary>
public class GiantGrowthFactoryTests
{
    private readonly EventBus _bus = new();
    private readonly Majik.Core.Stack.Stack _stack;
    private readonly SpellCastFlow _flow;
    private readonly ZoneService _zones;
    private readonly StackResolver _resolver;
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    public GiantGrowthFactoryTests()
    {
        _stack = new Majik.Core.Stack.Stack(_bus);
        _zones = new ZoneService(_bus);
        _flow = new SpellCastFlow(_stack, _zones, _bus);
        _resolver = new StackResolver(_bus, _zones);
    }

    [Fact]
    public void Create_HasInstantShape_Green_AtCostG()
    {
        var g = GiantGrowthFactory.Create(_alice);

        g.Name.Should().Be("Giant Growth");
        g.ManaCost.Should().Be("{G}");
        g.HasType(CardType.Instant).Should().BeTrue();
        g.Owner.Should().BeSameAs(_alice);
        g.Controller.Should().BeSameAs(_alice);
        CardColors.GetColors(g).Should().Contain(ManaColor.Green);
    }

    [Fact]
    public void NamedCardFactory_DispatchByName_ReturnsGiantGrowth()
    {
        var dispatched = NamedCardFactory.Create("Giant Growth", _alice);

        dispatched.Should().BeOfType<Instant>();
        dispatched.Name.Should().Be("Giant Growth");
        dispatched.HasType(CardType.Instant).Should().BeTrue();
    }

    [Fact]
    public void SpellDefinition_DeclaresSingleTargetCreatureRequest()
    {
        var def = GiantGrowthFactory.BuildDefinition();

        def.Modes.Should().BeEmpty();
        def.HasVariableX.Should().BeFalse();
        def.TargetRequests.Should().HaveCount(1);
        def.TargetRequests[0].Description.Should().Contain("creature");
        def.TargetRequests[0].MinTargets.Should().Be(1);
        def.TargetRequests[0].MaxTargets.Should().Be(1);
    }

    [Fact]
    public async Task CastAndResolve_TargetGetsPlusThreePlusThree()
    {
        var bear = BuildBear(_alice);

        var gg = GiantGrowthFactory.Create(_alice);
        gg.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(gg);

        var agent = new ScriptedAgent();
        agent.QueueTargets(new object[] { bear });
        agent.QueueMana(ManaPayment.Empty);
        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _alice, 1, PhaseStateType.PreCombatMain, _stack);

        await _flow.CastAsync(_alice, gg, GiantGrowthFactory.BuildDefinition(), agent, ctx);
        _resolver.ResolveTop(_stack);

        bear.GetPower().Should().Be(5);
        bear.GetToughness().Should().Be(5);
    }

    [Fact]
    public async Task PumpEffect_ExpiresAtEndOfTurn()
    {
        var bear = BuildBear(_alice);
        var svc = bear.ActiveEffects!;

        var gg = GiantGrowthFactory.Create(_alice);
        gg.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(gg);

        var agent = new ScriptedAgent();
        agent.QueueTargets(new object[] { bear });
        agent.QueueMana(ManaPayment.Empty);
        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _alice, 1, PhaseStateType.PreCombatMain, _stack);

        await _flow.CastAsync(_alice, gg, GiantGrowthFactory.BuildDefinition(), agent, ctx);
        _resolver.ResolveTop(_stack);

        bear.GetPower().Should().Be(5);

        // CR 514.2 — until end of turn effects expire in the cleanup step.
        svc.ExpireEndOfTurn();

        bear.GetPower().Should().Be(2);
        bear.GetToughness().Should().Be(2);
    }

    [Fact]
    public async Task TargetNotOnBattlefield_IsNoOp()
    {
        // Bob's creature is in the graveyard at resolution (CR 608.2b).
        var dead = new Creature("Grizzly Bears", "{1}{G}", 2, 2)
        { Owner = _bob, Controller = _bob, ActiveEffects = new ContinuousEffectsService() };
        dead.SetZone(ZoneType.Graveyard);
        _bob.Zones.Graveyard.AddCard(dead);

        var gg = GiantGrowthFactory.Create(_alice);
        gg.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(gg);

        var agent = new ScriptedAgent();
        agent.QueueTargets(new object[] { dead });
        agent.QueueMana(ManaPayment.Empty);
        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _alice, 1, PhaseStateType.PreCombatMain, _stack);

        await _flow.CastAsync(_alice, gg, GiantGrowthFactory.BuildDefinition(), agent, ctx);
        _resolver.ResolveTop(_stack);

        dead.GetPower().Should().Be(2);
        dead.GetToughness().Should().Be(2);
    }

    private Creature BuildBear(Player owner)
    {
        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2)
        { Owner = owner, Controller = owner, ActiveEffects = new ContinuousEffectsService() };
        bear.SetZone(ZoneType.Battlefield);
        owner.Zones.Battlefield.AddCard(bear);
        return bear;
    }
}
