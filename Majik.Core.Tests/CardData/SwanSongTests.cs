using System.Linq;
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
/// End-to-end tests for Swan Song ({U}).
/// Oracle: "Counter target enchantment, instant, or sorcery spell. Its
/// controller creates a 2/2 blue Bird creature token with flying."
///
/// <see cref="NegateFactory"/>-shape counter with a three-type filter, plus a
/// compensation token for the countered spell's controller. CR 608.2b: if the
/// target is not one of the three permitted types at resolution, neither the
/// counter nor the token happens.
/// </summary>
public class SwanSongTests
{
    private readonly EventBus _bus = new();
    private readonly Majik.Core.Stack.Stack _stack;
    private readonly SpellCastFlow _flow;
    private readonly ZoneService _zones;
    private readonly StackResolver _resolver;
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    public SwanSongTests()
    {
        _stack = new Majik.Core.Stack.Stack(_bus);
        _zones = new ZoneService(_bus);
        _flow = new SpellCastFlow(_stack, _zones, _bus);
        _resolver = new StackResolver(_bus, _zones);
    }

    [Fact]
    public void Create_HasInstantShape_Blue()
    {
        var card = SwanSongFactory.Create(_alice);

        card.Name.Should().Be("Swan Song");
        card.HasType(CardType.Instant).Should().BeTrue();
        CardColors.GetColors(card).Should().Contain(ManaColor.Blue);
        card.ManaCostValue.TotalValue.Should().Be(1);
    }

    [Fact]
    public void NamedCardFactory_DispatchByName_ReturnsShape()
    {
        var dispatched = NamedCardFactory.Create("Swan Song", _alice);

        dispatched.Should().BeOfType<Instant>();
        dispatched.Name.Should().Be("Swan Song");
        dispatched.ManaCost.Should().Be("{U}");
    }

    [Fact]
    public void SpellDefinition_DeclaresSingleTargetRequest_EnchantmentInstantSorcery()
    {
        var def = SwanSongFactory.BuildSpellDefinition(o => o, null);

        def.TargetRequests.Should().HaveCount(1);
        def.TargetRequests[0].MinTargets.Should().Be(1);
        def.TargetRequests[0].MaxTargets.Should().Be(1);
        def.TargetRequests[0].Description.Should().Contain("instant");
    }

    [Fact]
    public async Task CountersInstant_AndGivesControllerA2_2FlyingBird()
    {
        var card = SwanSongFactory.Create(_alice);
        card.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(card);

        var bobBolt = new Instant("Lightning Bolt", "{R}") { Owner = _bob, Controller = _bob };
        var bobSpell = new Majik.Core.Spells.Spell(bobBolt, _bob);
        _stack.Push(bobSpell);

        var agent = new ScriptedAgent();
        agent.QueueTargets(new[] { (object)bobSpell });
        agent.QueueMana(ManaPayment.Empty);
        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _alice, 1, PhaseStateType.PreCombatMain, _stack);

        await _flow.CastAsync(
            _alice, card, SwanSongFactory.BuildSpellDefinition(o => o, _stack, _zones), agent, ctx, alternativeCost: null);

        _resolver.ResolveTop(_stack);

        bobBolt.Zone.Should().Be(ZoneType.Graveyard, because: "Swan Song counters the instant spell");

        // The countered spell's controller (Bob) gets a 2/2 flying Bird.
        var bird = _bob.Zones.Battlefield.GetCards().OfType<Creature>()
            .SingleOrDefault(c => c.Name == "Bird");
        bird.Should().NotBeNull();
        bird!.Power.Should().Be(2);
        bird.Toughness.Should().Be(2);
    }

    [Fact]
    public async Task DoesNotCounterCreatureSpell_AndNoBird()
    {
        var card = SwanSongFactory.Create(_alice);
        card.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(card);

        var bobBear = new Creature("Grizzly Bears", "{1}{G}", 2, 2) { Owner = _bob, Controller = _bob };
        var bobSpell = new Majik.Core.Spells.Spell(bobBear, _bob);
        _stack.Push(bobSpell);

        var agent = new ScriptedAgent();
        agent.QueueTargets(new[] { (object)bobSpell });
        agent.QueueMana(ManaPayment.Empty);
        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _alice, 1, PhaseStateType.PreCombatMain, _stack);

        await _flow.CastAsync(
            _alice, card, SwanSongFactory.BuildSpellDefinition(o => o, _stack, _zones), agent, ctx, alternativeCost: null);

        _resolver.ResolveTop(_stack);

        bobBear.Zone.Should().NotBe(ZoneType.Graveyard, because: "creature spell is not a legal target");
        _bob.Zones.Battlefield.GetCards().OfType<Creature>()
            .Any(c => c.Name == "Bird").Should().BeFalse(because: "no counter → no compensation Bird");
    }
}
