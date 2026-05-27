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
/// Tests for Bull Rush (Portal, {R}, Instant — +2/+0 EOT).
///
/// Coverage:
/// - Card identity (Instant, red, {R}, owner/controller wired).
/// - NamedCardFactory dispatcher returns the correct shape.
/// - SpellDefinition shape (1 target creature, no modes, no X).
/// - Cast + resolve: target gets +2/+0.
/// - +2/+0 expires at end of turn (CR 514.2).
/// - Fizzle: target not on battlefield → no-op (CR 608.2b).
/// </summary>
public class BullRushFactoryTests
{
    private readonly EventBus _bus = new();
    private readonly Majik.Core.Stack.Stack _stack;
    private readonly SpellCastFlow _flow;
    private readonly ZoneService _zones;
    private readonly StackResolver _resolver;
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    public BullRushFactoryTests()
    {
        _stack = new Majik.Core.Stack.Stack(_bus);
        _zones = new ZoneService(_bus);
        _flow = new SpellCastFlow(_stack, _zones, _bus);
        _resolver = new StackResolver(_bus, _zones);
    }

    [Fact]
    public void Create_HasInstantShape_Red_AtCostR()
    {
        var br = BullRushFactory.Create(_alice);

        br.Name.Should().Be("Bull Rush");
        br.ManaCost.Should().Be("{R}");
        br.HasType(CardType.Instant).Should().BeTrue();
        br.Owner.Should().BeSameAs(_alice);
        br.Controller.Should().BeSameAs(_alice);
        CardColors.GetColors(br).Should().Contain(ManaColor.Red);
    }

    [Fact]
    public void NamedCardFactory_DispatchByName_ReturnsBullRush()
    {
        var dispatched = NamedCardFactory.Create("Bull Rush", _alice);

        dispatched.Should().BeOfType<Instant>();
        dispatched.Name.Should().Be("Bull Rush");
        dispatched.HasType(CardType.Instant).Should().BeTrue();
    }

    [Fact]
    public void SpellDefinition_DeclaresSingleTargetCreatureRequest()
    {
        var def = BullRushFactory.BuildDefinition();

        def.Modes.Should().BeEmpty();
        def.HasVariableX.Should().BeFalse();
        def.TargetRequests.Should().HaveCount(1);
        def.TargetRequests[0].Description.Should().Contain("creature");
        def.TargetRequests[0].MinTargets.Should().Be(1);
        def.TargetRequests[0].MaxTargets.Should().Be(1);
    }

    [Fact]
    public async Task CastAndResolve_TargetGetsPlusTwoPlusZero()
    {
        var bear = BuildBear(_alice);

        var br = BullRushFactory.Create(_alice);
        br.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(br);

        var agent = new ScriptedAgent();
        agent.QueueTargets(new object[] { bear });
        agent.QueueMana(ManaPayment.Empty);
        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _alice, 1, PhaseStateType.PreCombatMain, _stack);

        await _flow.CastAsync(_alice, br, BullRushFactory.BuildDefinition(), agent, ctx);
        _resolver.ResolveTop(_stack);

        // 2/2 bear + 2 power + 0 toughness = 4/2
        bear.GetPower().Should().Be(4);
        bear.GetToughness().Should().Be(2);
    }

    [Fact]
    public async Task PumpEffect_ExpiresAtEndOfTurn()
    {
        var bear = BuildBear(_alice);
        var svc = bear.ActiveEffects!;

        var br = BullRushFactory.Create(_alice);
        br.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(br);

        var agent = new ScriptedAgent();
        agent.QueueTargets(new object[] { bear });
        agent.QueueMana(ManaPayment.Empty);
        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _alice, 1, PhaseStateType.PreCombatMain, _stack);

        await _flow.CastAsync(_alice, br, BullRushFactory.BuildDefinition(), agent, ctx);
        _resolver.ResolveTop(_stack);

        bear.GetPower().Should().Be(4);
        bear.GetToughness().Should().Be(2);

        // CR 514.2 — until end of turn effects expire in the cleanup step.
        svc.ExpireEndOfTurn();

        bear.GetPower().Should().Be(2);
        bear.GetToughness().Should().Be(2);
    }

    [Fact]
    public async Task TargetNotOnBattlefield_IsNoOp()
    {
        // Bob's creature is in the graveyard at resolution (CR 608.2b).
        var dead = new Creature("Goblin Piker", "{1}{R}", 2, 1)
        { Owner = _bob, Controller = _bob, ActiveEffects = new ContinuousEffectsService() };
        dead.SetZone(ZoneType.Graveyard);
        _bob.Zones.Graveyard.AddCard(dead);

        var br = BullRushFactory.Create(_alice);
        br.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(br);

        var agent = new ScriptedAgent();
        agent.QueueTargets(new object[] { dead });
        agent.QueueMana(ManaPayment.Empty);
        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _alice, 1, PhaseStateType.PreCombatMain, _stack);

        await _flow.CastAsync(_alice, br, BullRushFactory.BuildDefinition(), agent, ctx);
        _resolver.ResolveTop(_stack);

        dead.GetPower().Should().Be(2);
        dead.GetToughness().Should().Be(1);
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
