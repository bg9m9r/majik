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
/// End-to-end tests for Broken Concentration (Torment, {1}{U}{U}, Instant).
///
/// Oracle text (verified against Scryfall 2026-06-10):
///   "Counter target spell.
///    Madness {3}{U} (If you discard this card, discard it into exile. When you
///    do, cast it for its madness cost or put it into your graveyard.)"
///
/// The counter body is the archetypal hard counter — identical shape to
/// <see cref="SawItComingFactory"/> (no type filter, any spell is a legal
/// target). Madness (CR 702.35) is intrinsic to the engine (the
/// <see cref="Majik.Core.Primitives.Fx.DiscardCard"/> funnel + MadnessCatalog)
/// and is covered by MadnessDiscardFunnelTests — these tests exercise only the
/// "Counter target spell." body + the card identity.
///
/// Coverage:
///   * Card identity (name / Instant / {1}{U}{U} / blue) from the embedded JSON.
///   * SpellDefinition shape (1 target spell request, no type filter).
///   * Counters a noncreature spell → graveyard (CR 701.5).
///   * Counters a creature spell → graveyard (no noncreature filter; "any
///     spell" oracle text).
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
    public void Create_HasInstantShape_Blue_At1UU()
    {
        var card = BrokenConcentrationFactory.Create(_alice);

        card.Name.Should().Be("Broken Concentration");
        card.HasType(CardType.Instant).Should().BeTrue();
        CardColors.GetColors(card).Should().Contain(ManaColor.Blue);
        card.ManaCost.ToString().Should().Be("{1}{U}{U}");
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void SpellDefinition_DeclaresSingleTargetSpellRequest_NoTypeFilter()
    {
        var def = BrokenConcentrationFactory.BuildSpellDefinition(o => o, null);

        def.Modes.Should().BeEmpty();
        def.HasVariableX.Should().BeFalse();
        def.TargetRequests.Should().HaveCount(1);
        def.TargetRequests[0].MinTargets.Should().Be(1);
        def.TargetRequests[0].MaxTargets.Should().Be(1);
        def.TargetRequests[0].Description.Should().Be("target spell");
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
            agent, ctx,
            alternativeCost: null);

        _resolver.ResolveTop(_stack);

        bobBolt.Zone.Should().Be(ZoneType.Graveyard,
            because: "Broken Concentration counters the noncreature spell (CR 701.5)");
    }

    [Fact]
    public async Task CountersCreatureSpell_AnySpell()
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
            agent, ctx,
            alternativeCost: null);

        _resolver.ResolveTop(_stack);

        bobBear.Zone.Should().Be(ZoneType.Graveyard,
            because: "Broken Concentration has no noncreature filter — creature spells are countered too");
    }
}
