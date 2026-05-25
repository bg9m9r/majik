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
using Majik.Core.Spells;
using Majik.Core.StateMachine;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// End-to-end tests for Spell Pierce (Zendikar, {U}).
/// Oracle: "Counter target noncreature spell unless its controller pays {2}."
///
/// Coverage:
///   - Card shape + dispatch by name (Instant {U}, blue).
///   - SpellDefinition declares one 1..1 "target noncreature spell" request.
///   - Counter-unless-pay success branch (controller pays {2} → resolves).
///   - Counter-unless-pay failure branch (no mana → countered).
///   - Creature spell at resolution → illegal target, full fizzle
///     (CR 608.2b — sole-target rule).
/// </summary>
public class SpellPierceFactoryTests
{
    private readonly EventBus _bus = new();
    private readonly Majik.Core.Stack.Stack _stack;
    private readonly SpellCastFlow _flow;
    private readonly ZoneService _zones;
    private readonly StackResolver _resolver;
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    public SpellPierceFactoryTests()
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
    public void Create_HasInstantShape_Blue_U()
    {
        var pierce = SpellPierceFactory.Create(_alice);

        pierce.Name.Should().Be("Spell Pierce");
        pierce.HasType(CardType.Instant).Should().BeTrue();
        CardColors.GetColors(pierce).Should().Contain(ManaColor.Blue);
        pierce.ManaCost.Should().Be("{U}");
        pierce.ManaCostValue.TotalValue.Should().Be(1);
    }

    [Fact]
    public void NamedCardFactory_DispatchByName_ReturnsSpellPierceShape()
    {
        var dispatched = NamedCardFactory.Create("Spell Pierce", _alice);

        dispatched.Should().BeOfType<Instant>();
        dispatched.Name.Should().Be("Spell Pierce");
        dispatched.ManaCost.Should().Be("{U}");
    }

    [Fact]
    public void SpellDefinition_DeclaresSingleNoncreatureTargetSpellRequest()
    {
        var def = SpellPierceFactory.BuildSpellDefinition(o => o, null);

        def.Modes.Should().BeEmpty();
        def.HasVariableX.Should().BeFalse();
        def.TargetRequests.Should().HaveCount(1);
        def.TargetRequests[0].MinTargets.Should().Be(1);
        def.TargetRequests[0].MaxTargets.Should().Be(1);
        def.TargetRequests[0].Description.Should().Contain("noncreature");
    }

    // -----------------------------------------------------------------------
    // Counter unless pay {2}
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Counters_NoncreatureSpell_WhenControllerCannotPayTwo()
    {
        var pierce = SpellPierceFactory.Create(_alice);
        pierce.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(pierce);

        // Bob casts a noncreature spell (Lightning Bolt {R}).
        // Bob has 0 mana → cannot pay {2} → Pierce counters.
        var bobBolt = new Instant("Lightning Bolt", "{R}") { Owner = _bob, Controller = _bob };
        var bobSpell = new Majik.Core.Spells.Spell(bobBolt, _bob);
        _stack.Push(bobSpell);

        var agent = new ScriptedAgent();
        agent.QueueTargets(new[] { (object)bobSpell });
        agent.QueueMana(ManaPayment.Empty);
        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _alice, 1, PhaseStateType.Main, _stack);

        await _flow.CastAsync(
            _alice, pierce,
            SpellPierceFactory.BuildSpellDefinition(o => o, _stack),
            agent, ctx,
            alternativeCost: null);

        _resolver.ResolveTop(_stack);

        bobBolt.Zone.Should().Be(ZoneType.Graveyard,
            because: "Bob couldn't pay {2} so Spell Pierce counters his spell");
    }

    [Fact]
    public async Task DoesNotCounter_WhenControllerPaysTwo()
    {
        var pierce = SpellPierceFactory.Create(_alice);
        pierce.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(pierce);

        // Bob has {2} available in his mana pool — he auto-pays the rider.
        _bob.AddManaToPool(ManaCost.Zero.AddGenericCost(2));

        var bobBolt = new Instant("Lightning Bolt", "{R}") { Owner = _bob, Controller = _bob };
        var bobSpell = new Majik.Core.Spells.Spell(bobBolt, _bob);
        _stack.Push(bobSpell);

        var agent = new ScriptedAgent();
        agent.QueueTargets(new[] { (object)bobSpell });
        agent.QueueMana(ManaPayment.Empty);
        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _alice, 1, PhaseStateType.Main, _stack);

        await _flow.CastAsync(
            _alice, pierce,
            SpellPierceFactory.BuildSpellDefinition(o => o, _stack),
            agent, ctx,
            alternativeCost: null);

        _resolver.ResolveTop(_stack);

        bobBolt.Zone.Should().NotBe(ZoneType.Graveyard,
            because: "Bob paid {2} so Spell Pierce is countered into a no-op");
    }

    [Fact]
    public async Task DoesNotCounter_CreatureSpellAtResolution_FullFizzle()
    {
        // Spell Pierce illegally targeting a creature spell (e.g. via a
        // type-changing effect mid-stack) → CR 608.2b sole-target rule:
        // full fizzle. The creature spell remains on the stack and
        // resolves normally; Pierce itself goes to its owner's
        // graveyard via the cast pipeline's post-resolve cleanup.
        var pierce = SpellPierceFactory.Create(_alice);
        pierce.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(pierce);

        // Bob's creature spell. Has no payable mana → if Pierce DID
        // counter, the creature would land in Bob's graveyard.
        var bobBear = new Creature("Grizzly Bears", "{1}{G}", 2, 2)
        { Owner = _bob, Controller = _bob };
        var bobCreatureSpell = new Majik.Core.Spells.Spell(bobBear, _bob);
        _stack.Push(bobCreatureSpell);

        var agent = new ScriptedAgent();
        agent.QueueTargets(new[] { (object)bobCreatureSpell });
        agent.QueueMana(ManaPayment.Empty);
        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _alice, 1, PhaseStateType.Main, _stack);

        await _flow.CastAsync(
            _alice, pierce,
            SpellPierceFactory.BuildSpellDefinition(o => o, _stack),
            agent, ctx,
            alternativeCost: null);

        _resolver.ResolveTop(_stack);

        bobBear.Zone.Should().NotBe(ZoneType.Graveyard,
            because: "Spell Pierce illegally targeted a creature spell — sole-target rule fizzles the counter");
    }
}
