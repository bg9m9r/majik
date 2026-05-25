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
/// Tests for Unsummon (Alpha, {U}).
///
/// Coverage:
/// - Card shape (Instant, blue, {U}).
/// - NamedCardFactory dispatch.
/// - Resolve: creature returns to its owner's hand.
/// - Resolve: target not on battlefield at resolution → no-op (CR 608.2b).
/// - Resolve: opponent-owned creature returns to opponent's hand, not caster.
/// </summary>
public class UnsummonTests
{
    private readonly EventBus _bus = new();
    private readonly Majik.Core.Stack.Stack _stack;
    private readonly SpellCastFlow _flow;
    private readonly ZoneService _zones;
    private readonly StackResolver _resolver;
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    public UnsummonTests()
    {
        _stack = new Majik.Core.Stack.Stack(_bus);
        _zones = new ZoneService(_bus);
        _flow = new SpellCastFlow(_stack, _zones, _bus);
        _resolver = new StackResolver(_bus, _zones);
    }

    [Fact]
    public void Unsummon_Create_HasInstantShape_Blue()
    {
        var u = UnsummonFactory.Create(_alice);

        u.Name.Should().Be("Unsummon");
        u.HasType(CardType.Instant).Should().BeTrue();
        CardColors.GetColors(u).Should().Contain(ManaColor.Blue);
        u.Owner.Should().Be(_alice);
        u.Controller.Should().Be(_alice);
        u.ManaCostValue.TotalValue.Should().Be(1, because: "single {U} pip");
    }

    [Fact]
    public void Unsummon_NamedCardFactory_ReturnsCorrectShape()
    {
        var card = NamedCardFactory.Create("Unsummon", _alice);

        card.Should().BeOfType<Instant>();
        card.Name.Should().Be("Unsummon");
        card.HasType(CardType.Instant).Should().BeTrue();
    }

    [Fact]
    public async Task Unsummon_Resolve_CreatureReturnedToOwnerHand_NoLifeLoss()
    {
        // Bob controls a Grizzly Bears that he also owns.
        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2)
        { Owner = _bob, Controller = _bob };
        bear.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(bear);

        var u = UnsummonFactory.Create(_alice);
        u.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(u);

        var agent = new ScriptedAgent();
        agent.QueueTargets(new[] { (object)bear });
        agent.QueueMana(ManaPayment.Empty);
        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _alice, 1, PhaseStateType.Main, _stack);

        var bobLifeBefore = _bob.LifeTotal;

        await _flow.CastAsync(
            _alice, u,
            UnsummonFactory.BuildDefinition(),
            agent, ctx,
            alternativeCost: null);

        _resolver.ResolveTop(_stack);

        // Bear is in Bob's hand.
        bear.Zone.Should().Be(ZoneType.Hand);
        _bob.Zones.Hand.GetCards().Should().Contain(bear);
        _bob.Zones.Battlefield.GetCards().Should().NotContain(bear);

        // No life loss: Unsummon has no rider clause.
        _bob.LifeTotal.Should().Be(bobLifeBefore,
            because: "Unsummon's printed text has no life-loss rider");
        _alice.LifeTotal.Should().Be(20);
    }

    [Fact]
    public async Task Unsummon_TargetLeavesBeforeResolution_NoOp()
    {
        // Bear is already in the graveyard at resolution.
        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2)
        { Owner = _bob, Controller = _bob };
        bear.SetZone(ZoneType.Graveyard);
        _bob.Zones.Graveyard.AddCard(bear);

        var u = UnsummonFactory.Create(_alice);
        u.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(u);

        var agent = new ScriptedAgent();
        agent.QueueTargets(new[] { (object)bear });
        agent.QueueMana(ManaPayment.Empty);
        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _alice, 1, PhaseStateType.Main, _stack);

        await _flow.CastAsync(
            _alice, u,
            UnsummonFactory.BuildDefinition(),
            agent, ctx,
            alternativeCost: null);

        _resolver.ResolveTop(_stack);

        // CR 608.2b — illegal target at resolution → effect does nothing.
        bear.Zone.Should().Be(ZoneType.Graveyard,
            because: "bear was never on the battlefield at resolution");
        _bob.Zones.Hand.GetCards().Should().NotContain(bear);
    }

    [Fact]
    public async Task Unsummon_OpponentCreature_ReturnsToOpponentHand_NotCaster()
    {
        // Bob owns and controls his bear; Alice steals it temporarily? Use
        // the plain case: Bob owns + controls, Alice casts Unsummon. Bear
        // must end up in Bob's hand (owner's hand, CR 701.20).
        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2)
        { Owner = _bob, Controller = _bob };
        bear.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(bear);

        var u = UnsummonFactory.Create(_alice);
        u.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(u);

        var agent = new ScriptedAgent();
        agent.QueueTargets(new[] { (object)bear });
        agent.QueueMana(ManaPayment.Empty);
        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _alice, 1, PhaseStateType.Main, _stack);

        await _flow.CastAsync(
            _alice, u,
            UnsummonFactory.BuildDefinition(),
            agent, ctx,
            alternativeCost: null);

        _resolver.ResolveTop(_stack);

        _bob.Zones.Hand.GetCards().Should().Contain(bear,
            because: "CR 701.20 — return to owner's hand (Bob owns the bear)");
        _alice.Zones.Hand.GetCards().Should().NotContain(bear);
    }
}
