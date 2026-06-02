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
/// Tests for Might of Oaks (Urza's Legacy, {3}{G}, Instant — +7/+7 EOT).
///
/// Coverage:
/// - Card identity (Instant, green, {3}{G}, owner/controller wired).
/// - NamedCardFactory dispatcher returns the correct shape.
/// - SpellDefinition shape (1 target creature, no modes, no X).
/// - Cast + resolve: target gets +7/+7.
/// - +7/+7 expires at end of turn (CR 514.2).
/// - Fizzle: target not on battlefield → no-op (CR 608.2b).
/// </summary>
[Trait("Color", "G")]
public class MightOfOaksFactoryTests
{
    private readonly EventBus _bus = new();
    private readonly Majik.Core.Stack.Stack _stack;
    private readonly SpellCastFlow _flow;
    private readonly ZoneService _zones;
    private readonly StackResolver _resolver;
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    public MightOfOaksFactoryTests()
    {
        _stack = new Majik.Core.Stack.Stack(_bus);
        _zones = new ZoneService(_bus);
        _flow = new SpellCastFlow(_stack, _zones, _bus);
        _resolver = new StackResolver(_bus, _zones);
    }

    [Fact]
    public void Create_HasInstantShape_Green_AtCost3G()
    {
        var mo = MightOfOaksFactory.Create(_alice);

        mo.Name.Should().Be("Might of Oaks");
        mo.ManaCost.Should().Be("{3}{G}");
        mo.HasType(CardType.Instant).Should().BeTrue();
        mo.Owner.Should().BeSameAs(_alice);
        mo.Controller.Should().BeSameAs(_alice);
        CardColors.GetColors(mo).Should().Contain(ManaColor.Green);
    }

    [Fact]
    public void Create_HasManaValueFour()
    {
        var mo = MightOfOaksFactory.Create(_alice);

        mo.ManaCostValue.TotalValue.Should().Be(4,
            "{3}{G} = 3 generic + 1 green = MV 4 (CR 202.3)");
    }
    [Fact]
    public void SpellDefinition_DeclaresSingleTargetCreatureRequest()
    {
        var def = MightOfOaksFactory.BuildDefinition();

        def.Modes.Should().BeEmpty();
        def.HasVariableX.Should().BeFalse();
        def.TargetRequests.Should().HaveCount(1);
        def.TargetRequests[0].Description.Should().Contain("creature");
        def.TargetRequests[0].MinTargets.Should().Be(1);
        def.TargetRequests[0].MaxTargets.Should().Be(1);
    }

    [Fact]
    public async Task CastAndResolve_TargetGetsPlusSevenPlusSeven()
    {
        var bear = BuildBear(_alice);

        var mo = MightOfOaksFactory.Create(_alice);
        mo.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(mo);

        var agent = new ScriptedAgent();
        agent.QueueTargets(new object[] { bear });
        agent.QueueMana(ManaPayment.Empty);
        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _alice, 1, PhaseStateType.PreCombatMain, _stack);

        await _flow.CastAsync(_alice, mo, MightOfOaksFactory.BuildDefinition(), agent, ctx);
        _resolver.ResolveTop(_stack);

        bear.GetPower().Should().Be(9);
        bear.GetToughness().Should().Be(9);
    }

    [Fact]
    public async Task PumpEffect_ExpiresAtEndOfTurn()
    {
        var bear = BuildBear(_alice);
        var svc = bear.ActiveEffects!;

        var mo = MightOfOaksFactory.Create(_alice);
        mo.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(mo);

        var agent = new ScriptedAgent();
        agent.QueueTargets(new object[] { bear });
        agent.QueueMana(ManaPayment.Empty);
        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _alice, 1, PhaseStateType.PreCombatMain, _stack);

        await _flow.CastAsync(_alice, mo, MightOfOaksFactory.BuildDefinition(), agent, ctx);
        _resolver.ResolveTop(_stack);

        bear.GetPower().Should().Be(9);

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

        var mo = MightOfOaksFactory.Create(_alice);
        mo.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(mo);

        var agent = new ScriptedAgent();
        agent.QueueTargets(new object[] { dead });
        agent.QueueMana(ManaPayment.Empty);
        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _alice, 1, PhaseStateType.PreCombatMain, _stack);

        await _flow.CastAsync(_alice, mo, MightOfOaksFactory.BuildDefinition(), agent, ctx);
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
