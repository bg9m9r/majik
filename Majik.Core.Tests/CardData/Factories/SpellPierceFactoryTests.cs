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

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// End-to-end tests for Spell Pierce (Zendikar, {U}, Instant).
///
/// Oracle text:
///   "Counter target noncreature spell unless its controller pays {2}."
///
/// Covers:
///   - Card shape + dispatch by <see cref="NamedCardFactory"/>.
///   - SpellDefinition shape (single 1..1 "target noncreature spell" request).
///   - Counter a noncreature spell whose controller has no {2} available
///     (countered → graveyard, CR 701.5).
///   - Auto-pay path: controller has {2} in their mana pool → spell resolves
///     uncountered (CR 118.4 v1 auto-pay posture).
///   - Noncreature filter: target became a creature spell at resolution
///     (CR 608.2b) → no-op.
/// </summary>
[Trait("Color", "U")]
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
    // Card identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void Create_HasInstantShape_Blue_AtCostU()
    {
        var sp = SpellPierceFactory.Create(_alice);

        sp.Name.Should().Be("Spell Pierce");
        sp.ManaCost.Should().Be("{U}");
        sp.HasType(CardType.Instant).Should().BeTrue();
        CardColors.GetColors(sp).Should().Contain(ManaColor.Blue);
        sp.ManaCostValue.TotalValue.Should().Be(1);
        sp.Owner.Should().BeSameAs(_alice);
        sp.Controller.Should().BeSameAs(_alice);
    }
    [Fact]
    public void SpellDefinition_DeclaresSingleTargetNoncreatureSpellRequest()
    {
        var def = SpellPierceFactory.BuildDefinition(o => o, null);

        def.Modes.Should().BeEmpty();
        def.HasVariableX.Should().BeFalse();
        def.TargetRequests.Should().HaveCount(1);
        def.TargetRequests[0].MinTargets.Should().Be(1);
        def.TargetRequests[0].MaxTargets.Should().Be(1);
        def.TargetRequests[0].Description.Should().Contain("noncreature");
    }

    // -----------------------------------------------------------------------
    // Counter when controller can't pay {2}
    // -----------------------------------------------------------------------

    [Fact]
    public async Task CountersNoncreatureSpell_WhenControllerCannotPayTwo()
    {
        // Bob casts a noncreature spell (Lightning Bolt {R}) with no {2}
        // available — Spell Pierce counters it.
        var sp = SpellPierceFactory.Create(_alice);
        sp.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(sp);

        var bobBolt = new Instant("Lightning Bolt", "{R}") { Owner = _bob, Controller = _bob };
        var bobSpell = new Majik.Core.Spells.Spell(bobBolt, _bob);
        _stack.Push(bobSpell);

        var agent = new ScriptedAgent();
        agent.QueueTargets(new[] { (object)bobSpell });
        agent.QueueMana(ManaPayment.Empty);
        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _alice, 1, PhaseStateType.PreCombatMain, _stack);

        await _flow.CastAsync(
            _alice, sp,
            SpellPierceFactory.BuildDefinition(o => o, _stack),
            agent, ctx,
            alternativeCost: null);

        _resolver.ResolveTop(_stack);

        bobBolt.Zone.Should().Be(ZoneType.Graveyard,
            because: "Bob has no {2}; the unless-pay rider fails and Spell Pierce counters (CR 701.5)");
    }

    // -----------------------------------------------------------------------
    // Auto-pay path: controller has {2} → no-op
    // -----------------------------------------------------------------------

    [Fact]
    public async Task DoesNotCounter_WhenControllerAutoPaysTwo()
    {
        var sp = SpellPierceFactory.Create(_alice);
        sp.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(sp);

        var bobBolt = new Instant("Lightning Bolt", "{R}") { Owner = _bob, Controller = _bob };
        var bobSpell = new Majik.Core.Spells.Spell(bobBolt, _bob);
        _stack.Push(bobSpell);

        // Bob has {2} in his pool to pay the unless-rider.
        _bob.AddManaToPool(ManaCost.Zero.AddGenericCost(2));

        var agent = new ScriptedAgent();
        agent.QueueTargets(new[] { (object)bobSpell });
        agent.QueueMana(ManaPayment.Empty);
        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _alice, 1, PhaseStateType.PreCombatMain, _stack);

        await _flow.CastAsync(
            _alice, sp,
            SpellPierceFactory.BuildDefinition(o => o, _stack),
            agent, ctx,
            alternativeCost: null);

        _resolver.ResolveTop(_stack);

        bobBolt.Zone.Should().NotBe(ZoneType.Graveyard,
            because: "Bob paid {2}; the counter no-ops and Bolt remains uncountered");
    }

    // -----------------------------------------------------------------------
    // Noncreature filter — creature spell target is illegal at resolve
    // -----------------------------------------------------------------------

    [Fact]
    public async Task DoesNotCounter_CreatureSpell()
    {
        var sp = SpellPierceFactory.Create(_alice);
        sp.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(sp);

        // Bob casts a creature spell — Spell Pierce can't target it (the
        // chosen-target filter runs at resolve, CR 608.2b).
        var bobBear = new Creature("Grizzly Bears", "{1}{G}", 2, 2) { Owner = _bob, Controller = _bob };
        var bobSpell = new Majik.Core.Spells.Spell(bobBear, _bob);
        _stack.Push(bobSpell);

        var agent = new ScriptedAgent();
        agent.QueueTargets(new[] { (object)bobSpell });
        agent.QueueMana(ManaPayment.Empty);
        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _alice, 1, PhaseStateType.PreCombatMain, _stack);

        await _flow.CastAsync(
            _alice, sp,
            SpellPierceFactory.BuildDefinition(o => o, _stack),
            agent, ctx,
            alternativeCost: null);

        _resolver.ResolveTop(_stack);

        bobBear.Zone.Should().NotBe(ZoneType.Graveyard,
            because: "Spell Pierce does not counter creature spells");
    }
}
