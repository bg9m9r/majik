using FluentAssertions;
using Majik.Core.Abilities;
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

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="RegressFactory"/>.
///
/// Regress — Instant {2}{U}. Oracle text:
///   "Return target permanent to its owner's hand."
///
/// Covers:
/// - Identity (name, Instant type, {2}{U} mana cost, mana value 3, Blue colour,
///   owner/controller).
/// - <see cref="NamedCardFactory"/> dispatch.
/// - BuildDefinition has exactly one TargetRequest (1..1, description contains
///   "permanent", BotIntent.Bounce).
/// - Resolve: bounces a creature permanent to its owner's hand.
/// - Resolve: bounces a non-creature permanent (Enchantment) to its owner's hand.
/// - Resolve: target already off battlefield (CR 608.2b) → no-op.
/// </summary>
public class RegressFactoryTests
{
    private readonly EventBus _bus = new();
    private readonly Majik.Core.Stack.Stack _stack;
    private readonly SpellCastFlow _flow;
    private readonly ZoneService _zones;
    private readonly StackResolver _resolver;
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    public RegressFactoryTests()
    {
        _stack = new Majik.Core.Stack.Stack(_bus);
        _zones = new ZoneService(_bus);
        _flow = new SpellCastFlow(_stack, _zones, _bus);
        _resolver = new StackResolver(_bus, _zones);
    }

    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void Regress_Create_HasInstantShape_Blue_ManaValue3()
    {
        var card = RegressFactory.Create(_alice);

        card.Name.Should().Be("Regress");
        card.HasType(CardType.Instant).Should().BeTrue();
        CardColors.GetColors(card).Should().Contain(ManaColor.Blue);
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
        card.ManaCostValue.TotalValue.Should().Be(3,
            because: "{2}{U} has mana value 3 (two generic + one Blue pip)");
    }

    // -----------------------------------------------------------------------
    // NamedCardFactory dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void Regress_NamedCardFactory_DispatchesByName()
    {
        var card = NamedCardFactory.Create("Regress", _alice);

        card.Should().BeOfType<Instant>("Regress is an Instant");
        card.Name.Should().Be("Regress");
        card.HasType(CardType.Instant).Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // BuildDefinition — target request shape
    // -----------------------------------------------------------------------

    [Fact]
    public void Regress_BuildDefinition_HasOneTargetRequest_TargetPermanent()
    {
        var def = RegressFactory.BuildDefinition();

        def.TargetRequests.Should().HaveCount(1,
            "Regress has exactly one 'target permanent' request");

        var req = def.TargetRequests[0];
        req.MinTargets.Should().Be(1);
        req.MaxTargets.Should().Be(1);
        req.Description.Should().Contain("permanent",
            "the request is for a target permanent");
        req.Intent.Should().Be(BotIntent.Bounce,
            "bot uses Bounce intent to rank the target");
    }

    // -----------------------------------------------------------------------
    // Resolve — creature permanent bounced to hand
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Regress_Resolve_CreaturePermanent_ReturnedToOwnerHand()
    {
        // Bob controls a 2/2 Bear that he owns.
        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2)
        { Owner = _bob, Controller = _bob };
        bear.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(bear);

        var regress = RegressFactory.Create(_alice);
        regress.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(regress);

        var agent = new ScriptedAgent();
        agent.QueueTargets(new[] { (object)bear });
        agent.QueueMana(ManaPayment.Empty);
        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _alice, 1, PhaseStateType.Main, _stack);

        await _flow.CastAsync(
            _alice, regress,
            RegressFactory.BuildDefinition(zoneService: _zones),
            agent, ctx,
            alternativeCost: null);

        _resolver.ResolveTop(_stack);

        bear.Zone.Should().Be(ZoneType.Hand,
            "Regress returns the creature permanent to its owner's hand");
        _bob.Zones.Hand.GetCards().Should().Contain(bear);
        _bob.Zones.Battlefield.GetCards().Should().NotContain(bear);
    }

    // -----------------------------------------------------------------------
    // Resolve — non-creature permanent (Enchantment) bounced to hand
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Regress_Resolve_EnchantmentPermanent_ReturnedToOwnerHand()
    {
        // Bob controls an Enchantment on the battlefield.
        var enchantment = new Enchantment("Curiosity", "{U}")
        { Owner = _bob, Controller = _bob };
        enchantment.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(enchantment);

        var regress = RegressFactory.Create(_alice);
        regress.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(regress);

        var agent = new ScriptedAgent();
        agent.QueueTargets(new[] { (object)enchantment });
        agent.QueueMana(ManaPayment.Empty);
        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _alice, 1, PhaseStateType.Main, _stack);

        await _flow.CastAsync(
            _alice, regress,
            RegressFactory.BuildDefinition(zoneService: _zones),
            agent, ctx,
            alternativeCost: null);

        _resolver.ResolveTop(_stack);

        enchantment.Zone.Should().Be(ZoneType.Hand,
            "Regress targets any permanent — Enchantment is a legal target");
        _bob.Zones.Hand.GetCards().Should().Contain(enchantment);
        _bob.Zones.Battlefield.GetCards().Should().NotContain(enchantment);
    }

    // -----------------------------------------------------------------------
    // Resolve — target already off battlefield (CR 608.2b) → no-op
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Regress_Resolve_TargetNotOnBattlefield_IsNoOp()
    {
        // Bob's creature is already in the graveyard by resolution time.
        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2)
        { Owner = _bob, Controller = _bob };
        bear.SetZone(ZoneType.Graveyard);
        _bob.Zones.Graveyard.AddCard(bear);

        var regress = RegressFactory.Create(_alice);
        regress.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(regress);

        var agent = new ScriptedAgent();
        agent.QueueTargets(new[] { (object)bear });
        agent.QueueMana(ManaPayment.Empty);
        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _alice, 1, PhaseStateType.Main, _stack);

        await _flow.CastAsync(
            _alice, regress,
            RegressFactory.BuildDefinition(zoneService: _zones),
            agent, ctx,
            alternativeCost: null);

        _resolver.ResolveTop(_stack);

        // CR 608.2b — target is not on battlefield → effect does nothing.
        bear.Zone.Should().Be(ZoneType.Graveyard,
            "CR 608.2b: target not on battlefield at resolution — Regress does nothing");
        _bob.Zones.Hand.GetCards().Should().NotContain(bear);
    }
}
