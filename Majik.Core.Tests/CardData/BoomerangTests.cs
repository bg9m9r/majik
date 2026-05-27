using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
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
/// End-to-end tests for Boomerang ({U}{U}).
/// Oracle: "Return target permanent to its owner's hand."
///
/// The broad bounce — targets any permanent (not just creatures).
/// CR 608.2b: if the chosen target is no longer a permanent on the
/// battlefield at resolution, the effect does nothing.
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

    // ── Identity ──────────────────────────────────────────────────────────

    [Fact]
    public void Create_HasInstantShape_Blue_TwoMana()
    {
        var card = BoomerangFactory.Create(_alice);

        card.Name.Should().Be("Boomerang");
        card.HasType(CardType.Instant).Should().BeTrue();
        CardColors.GetColors(card).Should().Contain(ManaColor.Blue);
        card.ManaCost.Should().Be("{U}{U}");
        card.ManaCostValue.TotalValue.Should().Be(2);
    }

    [Fact]
    public void NamedCardFactory_DispatchByName_ReturnsShape()
    {
        var dispatched = NamedCardFactory.Create("Boomerang", _alice);

        dispatched.Should().BeOfType<Instant>();
        dispatched.Name.Should().Be("Boomerang");
        dispatched.ManaCost.Should().Be("{U}{U}");
    }

    // ── Resolve: creature ─────────────────────────────────────────────────

    [Fact]
    public async Task ReturnsTargetCreatureToOwnersHand()
    {
        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2) { Owner = _bob, Controller = _bob };
        bear.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(bear);

        var card = BoomerangFactory.Create(_alice);
        card.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(card);

        var agent = new ScriptedAgent();
        agent.QueueTargets(new[] { (object)bear });
        agent.QueueMana(ManaPayment.Empty);
        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _alice, 1, PhaseStateType.PreCombatMain, _stack);

        await _flow.CastAsync(
            _alice, card, BoomerangFactory.BuildDefinition(), agent, ctx, alternativeCost: null);

        _resolver.ResolveTop(_stack);

        bear.Zone.Should().Be(ZoneType.Hand);
        _bob.Zones.Hand.GetCards().Should().Contain(bear);
        _bob.Zones.Battlefield.GetCards().Should().NotContain(bear);
    }

    // ── Resolve: noncreature permanent (enchantment) ──────────────────────

    [Fact]
    public async Task ReturnsTargetEnchantmentToOwnersHand()
    {
        var enchantment = new Enchantment("Blood Moon", "{2}{R}") { Owner = _bob, Controller = _bob };
        enchantment.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(enchantment);

        var card = BoomerangFactory.Create(_alice);
        card.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(card);

        var agent = new ScriptedAgent();
        agent.QueueTargets(new[] { (object)enchantment });
        agent.QueueMana(ManaPayment.Empty);
        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _alice, 1, PhaseStateType.PreCombatMain, _stack);

        await _flow.CastAsync(
            _alice, card, BoomerangFactory.BuildDefinition(), agent, ctx, alternativeCost: null);

        _resolver.ResolveTop(_stack);

        enchantment.Zone.Should().Be(ZoneType.Hand);
        _bob.Zones.Hand.GetCards().Should().Contain(enchantment);
        _bob.Zones.Battlefield.GetCards().Should().NotContain(enchantment);
    }

    // ── Resolve: land ─────────────────────────────────────────────────────

    [Fact]
    public async Task ReturnsTargetLandToOwnersHand()
    {
        var land = new Land("Island") { Owner = _bob, Controller = _bob };
        land.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(land);

        var card = BoomerangFactory.Create(_alice);
        card.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(card);

        var agent = new ScriptedAgent();
        agent.QueueTargets(new[] { (object)land });
        agent.QueueMana(ManaPayment.Empty);
        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _alice, 1, PhaseStateType.PreCombatMain, _stack);

        await _flow.CastAsync(
            _alice, card, BoomerangFactory.BuildDefinition(), agent, ctx, alternativeCost: null);

        _resolver.ResolveTop(_stack);

        land.Zone.Should().Be(ZoneType.Hand);
        _bob.Zones.Hand.GetCards().Should().Contain(land);
        _bob.Zones.Battlefield.GetCards().Should().NotContain(land);
    }

    // ── No-op: target not on battlefield at resolution ────────────────────

    [Fact]
    public async Task NoOp_WhenTargetNotOnBattlefieldAtResolution()
    {
        // Target starts in graveyard — simulates a permanent that died before
        // Boomerang resolved (CR 608.2b: illegal / no longer on battlefield → no-op).
        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2) { Owner = _bob, Controller = _bob };
        bear.SetZone(ZoneType.Graveyard);
        _bob.Zones.Graveyard.AddCard(bear);

        var card = BoomerangFactory.Create(_alice);
        card.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(card);

        var agent = new ScriptedAgent();
        agent.QueueTargets(new[] { (object)bear });
        agent.QueueMana(ManaPayment.Empty);
        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _alice, 1, PhaseStateType.PreCombatMain, _stack);

        await _flow.CastAsync(
            _alice, card, BoomerangFactory.BuildDefinition(), agent, ctx, alternativeCost: null);

        _resolver.ResolveTop(_stack);

        bear.Zone.Should().Be(ZoneType.Graveyard, because: "target not on battlefield at resolution → no-op (CR 608.2b)");
        _bob.Zones.Hand.GetCards().Should().NotContain(bear);
    }
}
