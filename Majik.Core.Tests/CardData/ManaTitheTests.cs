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
/// End-to-end tests for Mana Tithe ({W}).
/// Oracle: "Counter target spell unless its controller pays {1}."
///
/// White Force Spike — same "auto-pay-if-able" posture as
/// <see cref="QuenchFactory"/> / Mana Leak: at resolution the target's
/// controller pays {1} automatically when able (counter no-ops), otherwise
/// the spell is countered (CR 118.4 / 701.5). Real "do you want to pay?"
/// prompt is deferred to the shared agent-pay queue.
/// </summary>
public class ManaTitheTests
{
    private readonly EventBus _bus = new();
    private readonly Majik.Core.Stack.Stack _stack;
    private readonly SpellCastFlow _flow;
    private readonly ZoneService _zones;
    private readonly StackResolver _resolver;
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    public ManaTitheTests()
    {
        _stack = new Majik.Core.Stack.Stack(_bus);
        _zones = new ZoneService(_bus);
        _flow = new SpellCastFlow(_stack, _zones, _bus);
        _resolver = new StackResolver(_bus, _zones);
    }

    [Fact]
    public void Create_HasInstantShape_White()
    {
        var card = ManaTitheFactory.Create(_alice);

        card.Name.Should().Be("Mana Tithe");
        card.HasType(CardType.Instant).Should().BeTrue();
        CardColors.GetColors(card).Should().Contain(ManaColor.White);
        card.ManaCostValue.TotalValue.Should().Be(1);
    }

    [Fact]
    public void NamedCardFactory_DispatchByName_ReturnsShape()
    {
        var dispatched = NamedCardFactory.Create("Mana Tithe", _alice);

        dispatched.Should().BeOfType<Instant>();
        dispatched.Name.Should().Be("Mana Tithe");
        dispatched.ManaCost.Should().Be("{W}");
    }

    [Fact]
    public void SpellDefinition_DeclaresSingleTargetSpellRequest()
    {
        var def = ManaTitheFactory.BuildSpellDefinition(o => o, null);

        def.Modes.Should().BeEmpty();
        def.HasVariableX.Should().BeFalse();
        def.TargetRequests.Should().HaveCount(1);
        def.TargetRequests[0].MinTargets.Should().Be(1);
        def.TargetRequests[0].MaxTargets.Should().Be(1);
        def.TargetRequests[0].Description.Should().Contain("spell");
    }

    [Fact]
    public async Task CountersWhenControllerCannotPay()
    {
        var card = ManaTitheFactory.Create(_alice);
        card.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(card);

        // Bob's mana pool is empty — he cannot pay the {1} tithe.
        var bobBolt = new Instant("Lightning Bolt", "{R}") { Owner = _bob, Controller = _bob };
        var bobSpell = new Majik.Core.Spells.Spell(bobBolt, _bob);
        _stack.Push(bobSpell);

        var agent = new ScriptedAgent();
        agent.QueueTargets(new[] { (object)bobSpell });
        agent.QueueMana(ManaPayment.Empty);
        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _alice, 1, PhaseStateType.Main, _stack);

        await _flow.CastAsync(
            _alice, card,
            ManaTitheFactory.BuildSpellDefinition(o => o, _stack),
            agent, ctx, alternativeCost: null);

        _resolver.ResolveTop(_stack);

        bobBolt.Zone.Should().Be(ZoneType.Graveyard,
            because: "Bob cannot pay {1}, so Mana Tithe counters the spell");
    }

    [Fact]
    public async Task DoesNotCounterWhenControllerPaysOne()
    {
        var card = ManaTitheFactory.Create(_alice);
        card.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(card);

        var bobBolt = new Instant("Lightning Bolt", "{R}") { Owner = _bob, Controller = _bob };
        var bobSpell = new Majik.Core.Spells.Spell(bobBolt, _bob);
        _stack.Push(bobSpell);

        // Bob has {1} floating — auto-pays the tithe.
        _bob.AddManaToPool(ManaCost.Zero.AddGenericCost(1));

        var agent = new ScriptedAgent();
        agent.QueueTargets(new[] { (object)bobSpell });
        agent.QueueMana(ManaPayment.Empty);
        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _alice, 1, PhaseStateType.Main, _stack);

        await _flow.CastAsync(
            _alice, card,
            ManaTitheFactory.BuildSpellDefinition(o => o, _stack),
            agent, ctx, alternativeCost: null);

        _resolver.ResolveTop(_stack);

        bobBolt.Zone.Should().NotBe(ZoneType.Graveyard,
            because: "Bob pays {1}, so Mana Tithe does not counter the spell");
    }
}
