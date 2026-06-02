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

namespace Majik.Core.Tests.CardData;

/// <summary>
/// End-to-end tests for Tale's End (War of the Spark, {1}{U}).
/// Oracle (Scryfall, WAR):
///   "Counter target activated ability, triggered ability, or legendary
///    spell."
///
/// Coverage:
///   * Identity (Instant {1}{U}, blue).
///   * Dispatcher entry returns the correct shape.
///   * SpellDefinition shape (1 ability-or-legendary-spell request).
///   * Counter a triggered ability on the stack → removed (CR 701.5b).
///   * Counter an activated ability on the stack → removed (CR 701.5b).
///   * Counter a legendary spell → lands in graveyard (CR 701.5a).
///   * Non-legendary spell target → no-op at resolution (CR 608.2b / 205.4).
/// </summary>
[Trait("Color", "U")]
public class TalesEndTests
{
    private readonly EventBus _bus = new();
    private readonly Majik.Core.Stack.Stack _stack;
    private readonly SpellCastFlow _flow;
    private readonly ZoneService _zones;
    private readonly StackResolver _resolver;
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    public TalesEndTests()
    {
        _stack = new Majik.Core.Stack.Stack(_bus);
        _zones = new ZoneService(_bus);
        _flow = new SpellCastFlow(_stack, _zones, _bus);
        _resolver = new StackResolver(_bus, _zones);
    }

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void Create_HasInstantShape_Blue()
    {
        var talesEnd = TalesEndFactory.Create(_alice);

        talesEnd.Name.Should().Be("Tale's End");
        talesEnd.HasType(CardType.Instant).Should().BeTrue();
        talesEnd.ManaCost.Should().Be("{1}{U}");
        CardColors.GetColors(talesEnd).Should().Contain(ManaColor.Blue);
        talesEnd.ManaCostValue.TotalValue.Should().Be(2);
    }

    [Fact]
    public void NamedCardFactory_DispatchByName_ReturnsTalesEndShape()
    {
        var dispatched = NamedCardFactory.Create("Tale's End", _alice);

        dispatched.Should().BeOfType<Instant>();
        dispatched.Name.Should().Be("Tale's End");
        dispatched.ManaCost.Should().Be("{1}{U}");
    }

    [Fact]
    public void SpellDefinition_DeclaresSingleAbilityOrLegendarySpellTargetRequest()
    {
        var def = TalesEndFactory.BuildSpellDefinition(o => o, null);

        def.Modes.Should().BeEmpty();
        def.HasVariableX.Should().BeFalse();
        def.TargetRequests.Should().HaveCount(1);
        def.TargetRequests[0].MinTargets.Should().Be(1);
        def.TargetRequests[0].MaxTargets.Should().Be(1);
        def.TargetRequests[0].Description.Should().Contain("activated ability");
        def.TargetRequests[0].Description.Should().Contain("triggered ability");
        def.TargetRequests[0].Description.Should().Contain("legendary spell");
    }

    // -----------------------------------------------------------------------
    // Counter a triggered ability on the stack
    // -----------------------------------------------------------------------

    [Fact]
    public async Task CountersTriggeredAbilityOnStack()
    {
        var talesEnd = TalesEndFactory.Create(_alice);
        talesEnd.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(talesEnd);

        var bobSource = new Creature("Bob's Bear", "{1}{G}", 2, 2)
        {
            Owner = _bob,
            Controller = _bob,
            Zone = ZoneType.Battlefield,
        };
        var ranEffect = false;
        var trigger = new TriggeredAbility(
            bobSource,
            _bob,
            Triggers.OnEnterBattlefieldSelf(bobSource),
            effects: new IEffect[] { new Effect("eff", () => ranEffect = true) });
        _stack.Push(trigger);

        var agent = new ScriptedAgent();
        agent.QueueTargets(new[] { (object)trigger });
        agent.QueueMana(ManaPayment.Empty);
        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _alice, 1, PhaseStateType.PreCombatMain, _stack);

        await _flow.CastAsync(
            _alice, talesEnd,
            TalesEndFactory.BuildSpellDefinition(o => o, _stack),
            agent, ctx,
            alternativeCost: null);

        _resolver.ResolveTop(_stack);

        _stack.GetAll().Should().NotContain(trigger,
            because: "Tale's End removes the targeted triggered ability from the stack (CR 701.5b)");
        ranEffect.Should().BeFalse(
            because: "the countered ability's effects never run");
    }

    // -----------------------------------------------------------------------
    // Counter an activated ability on the stack
    // -----------------------------------------------------------------------

    [Fact]
    public async Task CountersActivatedAbilityOnStack()
    {
        var talesEnd = TalesEndFactory.Create(_alice);
        talesEnd.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(talesEnd);

        var bobSource = new Creature("Bob's Pinger", "{1}{U}", 1, 1)
        {
            Owner = _bob,
            Controller = _bob,
            Zone = ZoneType.Battlefield,
        };
        var ranEffect = false;
        var ability = new ActivatedAbility(
            bobSource,
            _bob,
            effects: new IEffect[] { new Effect("eff", () => ranEffect = true) });
        _stack.Push(ability);

        var agent = new ScriptedAgent();
        agent.QueueTargets(new[] { (object)ability });
        agent.QueueMana(ManaPayment.Empty);
        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _alice, 1, PhaseStateType.PreCombatMain, _stack);

        await _flow.CastAsync(
            _alice, talesEnd,
            TalesEndFactory.BuildSpellDefinition(o => o, _stack),
            agent, ctx,
            alternativeCost: null);

        _resolver.ResolveTop(_stack);

        _stack.GetAll().Should().NotContain(ability,
            because: "Tale's End counters activated abilities too (CR 701.5b) — distinct from Consign to Memory");
        ranEffect.Should().BeFalse(
            because: "the countered ability never resolves");
    }

    // -----------------------------------------------------------------------
    // Counter a legendary spell
    // -----------------------------------------------------------------------

    [Fact]
    public async Task CountersLegendarySpell()
    {
        var talesEnd = TalesEndFactory.Create(_alice);
        talesEnd.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(talesEnd);

        // Bob casts a legendary spell (a legendary creature).
        var bobLegend = new Creature(
            "Bob's Legend", "{2}{G}", 4, 4,
            supertypes: new[] { CardSupertype.Legendary })
        {
            Owner = _bob,
            Controller = _bob,
        };
        bobLegend.HasSupertype(CardSupertype.Legendary).Should().BeTrue();

        var bobSpell = new Majik.Core.Spells.Spell(bobLegend, _bob);
        _stack.Push(bobSpell);

        var agent = new ScriptedAgent();
        agent.QueueTargets(new[] { (object)bobSpell });
        agent.QueueMana(ManaPayment.Empty);
        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _alice, 1, PhaseStateType.PreCombatMain, _stack);

        await _flow.CastAsync(
            _alice, talesEnd,
            TalesEndFactory.BuildSpellDefinition(o => o, _stack),
            agent, ctx,
            alternativeCost: null);

        _resolver.ResolveTop(_stack);

        bobLegend.Zone.Should().Be(ZoneType.Graveyard,
            because: "Tale's End counters the legendary spell (CR 701.5a)");
    }

    // -----------------------------------------------------------------------
    // Non-legendary spell — illegal target (CR 205.4 / 608.2b)
    // -----------------------------------------------------------------------

    [Fact]
    public async Task DoesNotCounterNonLegendarySpell()
    {
        var talesEnd = TalesEndFactory.Create(_alice);
        talesEnd.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(talesEnd);

        // Bob casts a non-legendary spell (Lightning Bolt {R}).
        var bobBolt = new Instant("Lightning Bolt", "{R}") { Owner = _bob, Controller = _bob };
        bobBolt.HasSupertype(CardSupertype.Legendary).Should().BeFalse();
        var bobSpell = new Majik.Core.Spells.Spell(bobBolt, _bob);
        _stack.Push(bobSpell);

        var agent = new ScriptedAgent();
        agent.QueueTargets(new[] { (object)bobSpell });
        agent.QueueMana(ManaPayment.Empty);
        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _alice, 1, PhaseStateType.PreCombatMain, _stack);

        await _flow.CastAsync(
            _alice, talesEnd,
            TalesEndFactory.BuildSpellDefinition(o => o, _stack),
            agent, ctx,
            alternativeCost: null);

        _resolver.ResolveTop(_stack);

        // CR 205.4 / 608.2b — a non-legendary spell is not in Tale's End's
        // printed predicate. Tale's End's effect does nothing; the spell is
        // NOT sent to the graveyard by Tale's End.
        bobBolt.Zone.Should().NotBe(ZoneType.Graveyard,
            because: "Tale's End does not counter non-legendary spells");
    }
}
