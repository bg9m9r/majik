using FluentAssertions;
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
/// End-to-end tests for Broken Concentration (Torment, {1}{U}{U}).
/// Oracle: "Counter target spell. / Madness {3}{U}"
///
/// The vanilla hard counter — same body as <see cref="CancelFactory"/> with no
/// type filter. Counters any spell on the stack (CR 701.5). Madness is
/// intrinsic (MadnessCatalog + the discard funnel) and is NOT covered here.
///
/// Coverage:
///   * Identity — instant shape, {1}{U}{U}, blue.
///   * SpellDefinition shape (1 target spell request, no X).
///   * Counters a noncreature spell → graveyard.
///   * Counters a creature spell → graveyard (no type filter, unlike Negate).
/// </summary>
[Trait("Color", "U")]
public class BrokenConcentrationFactoryTests
{
    private readonly EventBus _bus = new();
    private readonly Majik.Core.Stack.Stack _stack;
    private readonly SpellCastFlow _flow;
    private readonly ZoneService _zones;
    private readonly StackResolver _resolver;
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    public BrokenConcentrationFactoryTests()
    {
        _stack = new Majik.Core.Stack.Stack(_bus);
        _zones = new ZoneService(_bus);
        _flow = new SpellCastFlow(_stack, _zones, _bus);
        _resolver = new StackResolver(_bus, _zones);
    }

    [Fact]
    public void Create_HasInstantShape_Blue()
    {
        var card = BrokenConcentrationFactory.Create(_alice);

        card.Name.Should().Be("Broken Concentration");
        card.HasType(CardType.Instant).Should().BeTrue();
        CardColors.GetColors(card).Should().Contain(ManaColor.Blue);
        card.ManaCost.Should().Be("{1}{U}{U}");
        card.ManaCostValue.TotalValue.Should().Be(3);
    }

    [Fact]
    public void SpellDefinition_DeclaresSingleTargetSpellRequest()
    {
        var def = BrokenConcentrationFactory.BuildSpellDefinition(o => o, null);

        def.Modes.Should().BeEmpty();
        def.HasVariableX.Should().BeFalse();
        def.TargetRequests.Should().HaveCount(1);
        def.TargetRequests[0].MinTargets.Should().Be(1);
        def.TargetRequests[0].MaxTargets.Should().Be(1);
        def.TargetRequests[0].Description.Should().Contain("spell");
    }

    [Fact]
    public async Task CountersNoncreatureSpell()
    {
        var card = BrokenConcentrationFactory.Create(_alice);
        card.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(card);

        var bobBolt = new Instant("Lightning Bolt", "{R}") { Owner = _bob, Controller = _bob };
        var bobSpell = new Majik.Core.Spells.Spell(bobBolt, _bob);
        _stack.Push(bobSpell);

        var agent = new ScriptedAgent();
        agent.QueueTargets(new[] { (object)bobSpell });
        agent.QueueMana(ManaPayment.Empty);
        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _alice, 1, StepStateType.PreCombatMain, _stack);

        await _flow.CastAsync(
            _alice, card,
            BrokenConcentrationFactory.BuildSpellDefinition(o => o, _stack),
            agent, ctx, alternativeCost: null);

        _resolver.ResolveTop(_stack);

        bobBolt.Zone.Should().Be(ZoneType.Graveyard,
            because: "Broken Concentration counters any spell (CR 701.5)");
    }

    [Fact]
    public async Task CountersCreatureSpell()
    {
        var card = BrokenConcentrationFactory.Create(_alice);
        card.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(card);

        var bobBear = new Creature("Grizzly Bears", "{1}{G}", 2, 2) { Owner = _bob, Controller = _bob };
        var bobSpell = new Majik.Core.Spells.Spell(bobBear, _bob);
        _stack.Push(bobSpell);

        var agent = new ScriptedAgent();
        agent.QueueTargets(new[] { (object)bobSpell });
        agent.QueueMana(ManaPayment.Empty);
        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _alice, 1, StepStateType.PreCombatMain, _stack);

        await _flow.CastAsync(
            _alice, card,
            BrokenConcentrationFactory.BuildSpellDefinition(o => o, _stack),
            agent, ctx, alternativeCost: null);

        _resolver.ResolveTop(_stack);

        bobBear.Zone.Should().Be(ZoneType.Graveyard,
            because: "Broken Concentration counters any spell, including creatures (no type filter)");
    }
}
