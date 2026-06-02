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
using Majik.Core.Spells;
using Majik.Core.StateMachine;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// End-to-end tests for Saw It Coming (Kaldheim, {1}{U}{U}, Instant).
///
/// Oracle text (verified against Scryfall 2026-06-02):
///   "Counter target spell.
///    Foretell {1}{U} (During your turn, you may pay {2} and exile this card
///    from your hand face down. Cast it on a later turn for its foretell cost.)"
///
/// The counter body is the archetypal hard counter — identical shape to
/// <see cref="CounterspellFactory"/> (no type filter, any spell is a legal
/// target). The Foretell alternative cost (CR 702.143) is NOT yet modelled by
/// the cast pipeline; following the <see cref="DoomskarFactory"/> precedent,
/// v1 ships only the printed mana-cost cast path and records the foretell cost
/// as a constant for the future cast pipeline.
///
/// Coverage:
///   * Card shape (name / Instant / {1}{U}{U} / blue) materialised from the
///     embedded JSON definition + NamedCardFactory dispatch.
///   * SpellDefinition shape (1 target spell request, no type filter).
///   * Counters a noncreature spell → graveyard (CR 701.5).
///   * Counters a creature spell → graveyard (no noncreature filter; "any
///     spell" oracle text).
///   * Foretell printed cost constant matches the oracle text.
/// </summary>
[Trait("Color", "U")]
public class SawItComingFactoryTests
{
    private readonly EventBus _bus = new();
    private readonly Majik.Core.Stack.Stack _stack;
    private readonly SpellCastFlow _flow;
    private readonly ZoneService _zones;
    private readonly StackResolver _resolver;
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    public SawItComingFactoryTests()
    {
        _stack = new Majik.Core.Stack.Stack(_bus);
        _zones = new ZoneService(_bus);
        _flow = new SpellCastFlow(_stack, _zones, _bus);
        _resolver = new StackResolver(_bus, _zones);
    }

    [Fact]
    public void Create_HasInstantShape_Blue_At1UU()
    {
        var sic = SawItComingFactory.Create(_alice);

        sic.Name.Should().Be("Saw It Coming");
        sic.HasType(CardType.Instant).Should().BeTrue();
        CardColors.GetColors(sic).Should().Contain(ManaColor.Blue);
        sic.ManaCost.ToString().Should().Be("{1}{U}{U}");
        sic.Owner.Should().BeSameAs(_alice);
        sic.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_SawItComing()
    {
        var card = NamedCardFactory.Create("Saw It Coming", _alice);

        card.Should().BeOfType<Instant>();
        card.Name.Should().Be("Saw It Coming");
        card.HasType(CardType.Instant).Should().BeTrue();
        card.ManaCost.ToString().Should().Be("{1}{U}{U}");
        card.Owner.Should().BeSameAs(_alice);
    }

    [Fact]
    public void ForetellCost_IsRecorded_ForFutureCastPipeline()
    {
        // Pins the constant — when Foretell (CR 702.143) is wired the cast
        // pipeline will bill this string for the foretold cast path.
        SawItComingFactory.ForetellPrintedCost.Should().Be("{1}{U}");
    }

    [Fact]
    public void SpellDefinition_DeclaresSingleTargetSpellRequest_NoTypeFilter()
    {
        var def = SawItComingFactory.BuildSpellDefinition(o => o, null);

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
        var sic = SawItComingFactory.Create(_alice);
        sic.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(sic);

        var bobBolt = new Instant("Lightning Bolt", "{R}") { Owner = _bob, Controller = _bob };
        var bobSpell = new Majik.Core.Spells.Spell(bobBolt, _bob);
        _stack.Push(bobSpell);

        var agent = new ScriptedAgent();
        agent.QueueTargets(new[] { (object)bobSpell });
        agent.QueueMana(ManaPayment.Empty);
        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _alice, 1, PhaseStateType.PreCombatMain, _stack);

        await _flow.CastAsync(
            _alice, sic,
            SawItComingFactory.BuildSpellDefinition(o => o, _stack),
            agent, ctx,
            alternativeCost: null);

        _resolver.ResolveTop(_stack);

        bobBolt.Zone.Should().Be(ZoneType.Graveyard,
            because: "Saw It Coming counters the noncreature spell (CR 701.5)");
    }

    [Fact]
    public async Task CountersCreatureSpell_AnySpell()
    {
        var sic = SawItComingFactory.Create(_alice);
        sic.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(sic);

        var bobBear = new Creature("Grizzly Bears", "{1}{G}", 2, 2) { Owner = _bob, Controller = _bob };
        var bobSpell = new Majik.Core.Spells.Spell(bobBear, _bob);
        _stack.Push(bobSpell);

        var agent = new ScriptedAgent();
        agent.QueueTargets(new[] { (object)bobSpell });
        agent.QueueMana(ManaPayment.Empty);
        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _alice, 1, PhaseStateType.PreCombatMain, _stack);

        await _flow.CastAsync(
            _alice, sic,
            SawItComingFactory.BuildSpellDefinition(o => o, _stack),
            agent, ctx,
            alternativeCost: null);

        _resolver.ResolveTop(_stack);

        bobBear.Zone.Should().Be(ZoneType.Graveyard,
            because: "Saw It Coming has no noncreature filter — creature spells are countered too");
    }
}
