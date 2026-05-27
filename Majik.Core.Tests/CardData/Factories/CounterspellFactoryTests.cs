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
/// End-to-end tests for Counterspell (Alpha, {U}{U}).
/// Oracle: "Counter target spell."
///
/// Coverage:
///   * Card shape + dispatch by name.
///   * SpellDefinition shape (1 target spell request, no type filter).
///   * Counters a noncreature spell → graveyard (CR 701.5).
///   * Counters a creature spell → graveyard (no noncreature filter; matches
///     Counterspell's "any spell" oracle text and differs from
///     <see cref="NegateFactory"/>).
/// </summary>
public class CounterspellFactoryTests
{
    private readonly EventBus _bus = new();
    private readonly Majik.Core.Stack.Stack _stack;
    private readonly SpellCastFlow _flow;
    private readonly ZoneService _zones;
    private readonly StackResolver _resolver;
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    public CounterspellFactoryTests()
    {
        _stack = new Majik.Core.Stack.Stack(_bus);
        _zones = new ZoneService(_bus);
        _flow = new SpellCastFlow(_stack, _zones, _bus);
        _resolver = new StackResolver(_bus, _zones);
    }

    [Fact]
    public void Create_HasInstantShape_Blue_AtUU()
    {
        var cs = CounterspellFactory.Create(_alice);

        cs.Name.Should().Be("Counterspell");
        cs.HasType(CardType.Instant).Should().BeTrue();
        CardColors.GetColors(cs).Should().Contain(ManaColor.Blue);
        cs.ManaCost.ToString().Should().Be("{U}{U}");
        cs.Owner.Should().BeSameAs(_alice);
        cs.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_DispatchByName_ReturnsCounterspellShape()
    {
        var dispatched = NamedCardFactory.Create("Counterspell", _alice);

        dispatched.Should().BeOfType<Instant>();
        dispatched.Name.Should().Be("Counterspell");
        dispatched.ManaCost.Should().Be("{U}{U}");
    }

    [Fact]
    public void SpellDefinition_DeclaresSingleTargetSpellRequest_NoTypeFilter()
    {
        var def = CounterspellFactory.BuildSpellDefinition(o => o, null);

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
        var cs = CounterspellFactory.Create(_alice);
        cs.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(cs);

        // Bob casts Lightning Bolt — a noncreature spell.
        var bobBolt = new Instant("Lightning Bolt", "{R}") { Owner = _bob, Controller = _bob };
        var bobSpell = new Majik.Core.Spells.Spell(bobBolt, _bob);
        _stack.Push(bobSpell);

        var agent = new ScriptedAgent();
        agent.QueueTargets(new[] { (object)bobSpell });
        agent.QueueMana(ManaPayment.Empty);
        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _alice, 1, PhaseStateType.PreCombatMain, _stack);

        await _flow.CastAsync(
            _alice, cs,
            CounterspellFactory.BuildSpellDefinition(o => o, _stack),
            agent, ctx,
            alternativeCost: null);

        _resolver.ResolveTop(_stack);

        bobBolt.Zone.Should().Be(ZoneType.Graveyard,
            because: "Counterspell counters the noncreature spell (CR 701.5)");
    }

    [Fact]
    public async Task CountersCreatureSpell_DiffersFromNegate()
    {
        var cs = CounterspellFactory.Create(_alice);
        cs.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(cs);

        // Bob casts Grizzly Bears — a creature spell. Counterspell has no
        // noncreature filter (unlike Negate), so it counters this too.
        var bobBear = new Creature("Grizzly Bears", "{1}{G}", 2, 2) { Owner = _bob, Controller = _bob };
        var bobSpell = new Majik.Core.Spells.Spell(bobBear, _bob);
        _stack.Push(bobSpell);

        var agent = new ScriptedAgent();
        agent.QueueTargets(new[] { (object)bobSpell });
        agent.QueueMana(ManaPayment.Empty);
        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _alice, 1, PhaseStateType.PreCombatMain, _stack);

        await _flow.CastAsync(
            _alice, cs,
            CounterspellFactory.BuildSpellDefinition(o => o, _stack),
            agent, ctx,
            alternativeCost: null);

        _resolver.ResolveTop(_stack);

        bobBear.Zone.Should().Be(ZoneType.Graveyard,
            because: "Counterspell has no noncreature filter — creature spells are countered too");
    }
}
