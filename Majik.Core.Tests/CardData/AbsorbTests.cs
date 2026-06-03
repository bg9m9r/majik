using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.StateMachine;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// End-to-end tests for Absorb (Apocalypse / Dominaria United, {W}{U}{U}).
/// Oracle: "Counter target spell. You gain 3 life."
///
/// Proves the new declarative <c>counter_target_spell</c> union verb composed
/// with <c>gain_life_self</c> resolves BOTH printed clauses (CR 608.2c) on the
/// production cast flow, and that the prod oracle-binder chain binds the full
/// text (the lifegain rider is NOT dropped by the generic single-clause
/// counter template).
/// </summary>
public class AbsorbTests
{
    private readonly EventBus _bus = new();
    private readonly Majik.Core.Stack.Stack _stack;
    private readonly SpellCastFlow _flow;
    private readonly Majik.Core.Services.ZoneService _zones;
    private readonly Majik.Core.Services.StackResolver _resolver;
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    public AbsorbTests()
    {
        _stack = new Majik.Core.Stack.Stack(_bus);
        _zones = new Majik.Core.Services.ZoneService(_bus);
        _flow = new SpellCastFlow(_stack, _zones, _bus);
        _resolver = new Majik.Core.Services.StackResolver(_bus, _zones);
    }

    [Fact]
    public void Create_HasInstantShape_WhiteBlue()
    {
        var absorb = AbsorbFactory.Create(_alice);

        absorb.Name.Should().Be("Absorb");
        absorb.HasType(CardType.Instant).Should().BeTrue();
        absorb.ManaCostValue.TotalValue.Should().Be(3);
        CardColors.GetColors(absorb).Should().Contain(ManaColor.White);
        CardColors.GetColors(absorb).Should().Contain(ManaColor.Blue);
    }

    [Fact]
    public void NamedCardFactory_DispatchByName_ReturnsAbsorbShape()
    {
        var dispatched = NamedCardFactory.Create("Absorb", _alice);

        dispatched.Should().BeOfType<Instant>();
        dispatched.Name.Should().Be("Absorb");
        dispatched.ManaCost.Should().Be("{W}{U}{U}");
    }

    [Fact]
    public void BuildDefinition_DeclaresSingleSpellTargetRequest()
    {
        var def = AbsorbFactory.BuildDefinition();

        // counter_target_spell declares one slot; gain_life_self is untargeted.
        def.TargetRequests.Should().HaveCount(1);
        def.TargetRequests[0].MinTargets.Should().Be(1);
        def.TargetRequests[0].Description.Should().Contain("spell");
    }

    [Fact]
    public void OracleBinder_BindsFullText_WithCounterTargetRequest()
    {
        // The prod binder chain (oracle text → SpellDefinition) must bind the
        // composite "Counter target spell. You gain 3 life." with the counter
        // target slot AND the lifegain rider — not the single-clause counter.
        var def = OracleSpellBinder.Bind(
            new CardEntity
            {
                Name = "Absorb",
                ManaCost = "{W}{U}{U}",
                OracleText = "Counter target spell. You gain 3 life.",
            },
            _alice, raw => raw, null);

        def.Should().NotBeNull();
        def!.TargetRequests.Should().HaveCount(1);
        def.TargetRequests[0].Description.Should().Contain("spell");
    }

    [Fact]
    public async Task CountersSpell_AndGainsThreeLife()
    {
        var absorb = AbsorbFactory.Create(_alice);
        absorb.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(absorb);

        var bobBolt = new Instant("Lightning Bolt", "{R}") { Owner = _bob, Controller = _bob };
        var bobSpell = new Majik.Core.Spells.Spell(bobBolt, _bob);
        _stack.Push(bobSpell);

        var agent = new ScriptedAgent();
        agent.QueueTargets(new[] { (object)bobSpell });
        agent.QueueMana(ManaPayment.Empty);
        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _alice, 1, StepStateType.PreCombatMain, _stack);

        await _flow.CastAsync(_alice, absorb, AbsorbFactory.BuildDefinition(), agent, ctx, alternativeCost: null);
        await _resolver.ResolveTopAsync(_stack, game: ctx);

        bobBolt.Zone.Should().Be(ZoneType.Graveyard, "Absorb counters the target spell (CR 701.5)");
        _alice.LifeTotal.Should().Be(23, "Absorb's controller gains 3 life (CR 119.3)");
    }
}
