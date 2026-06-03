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

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for Unsubstantiate (Eldritch Moon, {1}{U}).
///
/// Instant. Oracle text (verified against Scryfall):
///   "Return target spell or creature to its owner's hand."
///
/// Coverage:
/// - Card shape (Instant, blue, {1}{U}) + NamedCardFactory dispatch.
/// - Creature mode: target creature returns to its owner's hand.
/// - Spell mode: target spell on the stack returns to its owner's hand
///   (a soft counter — CR 701.10 — the card lands in hand, never the
///   graveyard, even if it can't be countered).
/// - CR 608.2b — a creature target that has left the battlefield no-ops.
/// </summary>
public class UnsubstantiateTests
{
    private readonly EventBus _bus = new();
    private readonly Majik.Core.Stack.Stack _stack;
    private readonly SpellCastFlow _flow;
    private readonly ZoneService _zones;
    private readonly StackResolver _resolver;
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    public UnsubstantiateTests()
    {
        _stack = new Majik.Core.Stack.Stack(_bus);
        _zones = new ZoneService(_bus);
        _flow = new SpellCastFlow(_stack, _zones, _bus);
        _resolver = new StackResolver(_bus, _zones);
    }

    // ── Shape ────────────────────────────────────────────────────────────────

    [Fact]
    public void Unsubstantiate_Create_HasInstantShape_Blue()
    {
        var u = UnsubstantiateFactory.Create(_alice);

        u.Name.Should().Be("Unsubstantiate");
        u.HasType(CardType.Instant).Should().BeTrue();
        CardColors.GetColors(u).Should().Contain(ManaColor.Blue);
        u.Owner.Should().Be(_alice);
        u.Controller.Should().Be(_alice);
        u.ManaCostValue.TotalValue.Should().Be(2, because: "{1}{U} has mana value 2");
    }

    [Fact]
    public void Unsubstantiate_NamedCardFactory_ReturnsCorrectShape()
    {
        var card = NamedCardFactory.Create("Unsubstantiate", _alice);

        card.Should().BeOfType<Instant>();
        card.Name.Should().Be("Unsubstantiate");
        card.HasType(CardType.Instant).Should().BeTrue();
    }

    // ── Creature mode ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Unsubstantiate_Resolve_CreatureReturnedToOwnerHand()
    {
        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2)
        { Owner = _bob, Controller = _bob };
        bear.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(bear);

        var u = UnsubstantiateFactory.Create(_alice);
        u.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(u);

        var agent = new ScriptedAgent();
        agent.QueueTargets(new[] { (object)bear });
        agent.QueueMana(ManaPayment.Empty);
        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _alice, 1, StepStateType.PreCombatMain, _stack);

        await _flow.CastAsync(
            _alice, u,
            UnsubstantiateFactory.BuildDefinition(_stack),
            agent, ctx,
            alternativeCost: null);

        _resolver.ResolveTop(_stack);

        bear.Zone.Should().Be(ZoneType.Hand);
        _bob.Zones.Hand.GetCards().Should().Contain(bear);
        _bob.Zones.Battlefield.GetCards().Should().NotContain(bear);
    }

    [Fact]
    public async Task Unsubstantiate_CreatureLeavesBeforeResolution_IsNoOp()
    {
        // CR 608.2b — creature already in the graveyard when the spell resolves.
        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2)
        { Owner = _bob, Controller = _bob };
        bear.SetZone(ZoneType.Graveyard);
        _bob.Zones.Graveyard.AddCard(bear);

        var u = UnsubstantiateFactory.Create(_alice);
        u.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(u);

        var agent = new ScriptedAgent();
        agent.QueueTargets(new[] { (object)bear });
        agent.QueueMana(ManaPayment.Empty);
        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _alice, 1, StepStateType.PreCombatMain, _stack);

        await _flow.CastAsync(
            _alice, u,
            UnsubstantiateFactory.BuildDefinition(_stack),
            agent, ctx,
            alternativeCost: null);

        _resolver.ResolveTop(_stack);

        bear.Zone.Should().Be(ZoneType.Graveyard,
            because: "illegal target at resolution: creature is not on the battlefield");
        _bob.Zones.Hand.GetCards().Should().NotContain(bear);
    }

    // ── Spell mode ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task Unsubstantiate_Resolve_SpellReturnedToOwnerHand_NotCountered()
    {
        // Bob has a Lightning Bolt on the stack. Alice casts Unsubstantiate
        // targeting it; the bolt returns to Bob's hand (CR 701.10 — soft
        // counter; card to hand, never the graveyard).
        var bobBolt = new Instant("Lightning Bolt", "{R}") { Owner = _bob, Controller = _bob };
        var bobSpell = new Majik.Core.Spells.Spell(bobBolt, _bob);
        _stack.Push(bobSpell);

        var u = UnsubstantiateFactory.Create(_alice);
        u.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(u);

        var agent = new ScriptedAgent();
        agent.QueueTargets(new[] { (object)bobSpell });
        agent.QueueMana(ManaPayment.Empty);
        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _alice, 1, StepStateType.PreCombatMain, _stack);

        await _flow.CastAsync(
            _alice, u,
            UnsubstantiateFactory.BuildDefinition(_stack),
            agent, ctx,
            alternativeCost: null);

        // Resolve Unsubstantiate (top of stack); the bolt remains beneath.
        _resolver.ResolveTop(_stack);

        bobBolt.Zone.Should().Be(ZoneType.Hand,
            because: "Unsubstantiate returns the targeted spell to its owner's hand");
        _bob.Zones.Hand.GetCards().Should().Contain(bobBolt);
        _stack.GetAll().Should().NotContain(bobSpell,
            because: "the targeted spell is removed from the stack");
    }
}
