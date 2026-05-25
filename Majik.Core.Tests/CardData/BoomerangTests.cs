using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
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

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Tests for Boomerang (Alpha, {U}{U}).
///
/// Coverage:
/// - Card shape (Instant, blue, {U}{U} = MV 2).
/// - NamedCardFactory dispatch.
/// - Resolve: target creature returns to owner's hand.
/// - Resolve: target artifact returns to owner's hand (broader than Unsummon).
/// - Resolve: target land returns to owner's hand.
/// - Resolve: target not on battlefield at resolution → no-op (CR 608.2b).
/// </summary>
public class BoomerangTests
{
    private readonly EventBus _bus = new();
    private readonly Majik.Core.Stack.Stack _stack;
    private readonly SpellCastFlow _flow;
    private readonly ZoneService _zones;
    private readonly StackResolver _resolver;
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    public BoomerangTests()
    {
        _stack = new Majik.Core.Stack.Stack(_bus);
        _zones = new ZoneService(_bus);
        _flow = new SpellCastFlow(_stack, _zones, _bus);
        _resolver = new StackResolver(_bus, _zones);
    }

    [Fact]
    public void Boomerang_Create_HasInstantShape_Blue_DoubleBlue()
    {
        var b = BoomerangFactory.Create(_alice);

        b.Name.Should().Be("Boomerang");
        b.HasType(CardType.Instant).Should().BeTrue();
        CardColors.GetColors(b).Should().Contain(ManaColor.Blue);
        b.Owner.Should().Be(_alice);
        b.Controller.Should().Be(_alice);
        b.ManaCostValue.TotalValue.Should().Be(2,
            because: "{U}{U} has mana value 2");
    }

    [Fact]
    public void Boomerang_NamedCardFactory_ReturnsCorrectShape()
    {
        var card = NamedCardFactory.Create("Boomerang", _alice);

        card.Should().BeOfType<Instant>();
        card.Name.Should().Be("Boomerang");
        card.HasType(CardType.Instant).Should().BeTrue();
    }

    [Fact]
    public async Task Boomerang_Resolve_TargetCreature_ReturnsToOwnerHand()
    {
        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2)
        { Owner = _bob, Controller = _bob };
        bear.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(bear);

        var b = BoomerangFactory.Create(_alice);
        b.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(b);

        var agent = new ScriptedAgent();
        agent.QueueTargets(new[] { (object)bear });
        agent.QueueMana(ManaPayment.Empty);
        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _alice, 1, PhaseStateType.Main, _stack);

        await _flow.CastAsync(
            _alice, b,
            BoomerangFactory.BuildDefinition(),
            agent, ctx,
            alternativeCost: null);

        _resolver.ResolveTop(_stack);

        bear.Zone.Should().Be(ZoneType.Hand);
        _bob.Zones.Hand.GetCards().Should().Contain(bear);
        _bob.Zones.Battlefield.GetCards().Should().NotContain(bear);
    }

    [Fact]
    public async Task Boomerang_Resolve_TargetArtifact_ReturnsToOwnerHand()
    {
        var sol = new Artifact("Sol Ring", "{1}")
        { Owner = _bob, Controller = _bob };
        sol.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(sol);

        var b = BoomerangFactory.Create(_alice);
        b.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(b);

        var agent = new ScriptedAgent();
        agent.QueueTargets(new[] { (object)sol });
        agent.QueueMana(ManaPayment.Empty);
        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _alice, 1, PhaseStateType.Main, _stack);

        await _flow.CastAsync(
            _alice, b,
            BoomerangFactory.BuildDefinition(),
            agent, ctx,
            alternativeCost: null);

        _resolver.ResolveTop(_stack);

        sol.Zone.Should().Be(ZoneType.Hand,
            because: "Boomerang targets ANY permanent, not just creatures");
        _bob.Zones.Hand.GetCards().Should().Contain(sol);
    }

    [Fact]
    public async Task Boomerang_Resolve_TargetLand_ReturnsToOwnerHand()
    {
        var island = new Land("Island")
        { Owner = _bob, Controller = _bob };
        island.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(island);

        var b = BoomerangFactory.Create(_alice);
        b.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(b);

        var agent = new ScriptedAgent();
        agent.QueueTargets(new[] { (object)island });
        agent.QueueMana(ManaPayment.Empty);
        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _alice, 1, PhaseStateType.Main, _stack);

        await _flow.CastAsync(
            _alice, b,
            BoomerangFactory.BuildDefinition(),
            agent, ctx,
            alternativeCost: null);

        _resolver.ResolveTop(_stack);

        island.Zone.Should().Be(ZoneType.Hand,
            because: "Boomerang can target lands too (any permanent)");
        _bob.Zones.Hand.GetCards().Should().Contain(island);
    }

    [Fact]
    public async Task Boomerang_TargetLeavesBeforeResolution_NoOp()
    {
        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2)
        { Owner = _bob, Controller = _bob };
        bear.SetZone(ZoneType.Graveyard);
        _bob.Zones.Graveyard.AddCard(bear);

        var b = BoomerangFactory.Create(_alice);
        b.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(b);

        var agent = new ScriptedAgent();
        agent.QueueTargets(new[] { (object)bear });
        agent.QueueMana(ManaPayment.Empty);
        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _alice, 1, PhaseStateType.Main, _stack);

        await _flow.CastAsync(
            _alice, b,
            BoomerangFactory.BuildDefinition(),
            agent, ctx,
            alternativeCost: null);

        _resolver.ResolveTop(_stack);

        // CR 608.2b — illegal target at resolution → effect does nothing.
        bear.Zone.Should().Be(ZoneType.Graveyard);
        _bob.Zones.Hand.GetCards().Should().NotContain(bear);
    }
}
