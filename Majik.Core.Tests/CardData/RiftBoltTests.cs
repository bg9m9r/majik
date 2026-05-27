using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
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
/// Tests for Rift Bolt (Time Spiral, {2}{R}, Sorcery).
///
/// Covers:
///   - Card shape (name, type, mana cost).
///   - NamedCardFactory dispatch.
///   - Normal cast (pay {2}{R}, deal 3 to any target).
///   - Suspend cast cycle (pay {R}, exile with 1 time counter; next
///     upkeep auto-casts the spell for free, dealing 3 damage).
///   - Sanity: a card with no time counters doesn't trigger the free
///     cast.
/// </summary>
public class RiftBoltTests
{
    private readonly EventBus _bus = new();
    private readonly Majik.Core.Stack.Stack _stack;
    private readonly SpellCastFlow _flow;
    private readonly ZoneService _zones;
    private readonly StackResolver _resolver;
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    public RiftBoltTests()
    {
        _stack = new Majik.Core.Stack.Stack(_bus);
        _zones = new ZoneService(_bus);
        _flow = new SpellCastFlow(_stack, _zones, _bus);
        _resolver = new StackResolver(_bus, _zones);
    }

    [Fact]
    public void RiftBolt_IsSorcery_AtCost2R()
    {
        var rb = RiftBoltFactory.Create(_alice);

        rb.Name.Should().Be("Rift Bolt");
        rb.ManaCost.Should().Be("{2}{R}");
        rb.HasType(CardType.Sorcery).Should().BeTrue();
        rb.Owner.Should().Be(_alice);
        rb.Controller.Should().Be(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_RiftBolt()
    {
        var card = NamedCardFactory.Create("Rift Bolt", _alice);

        card.Should().BeOfType<Sorcery>();
        card.Name.Should().Be("Rift Bolt");
        card.HasType(CardType.Sorcery).Should().BeTrue();
        card.Owner.Should().Be(_alice);
    }

    [Fact]
    public async Task RiftBolt_NormalCast_Pay2R_Deal3DamageToTarget()
    {
        // Setup: Rift Bolt in Alice's hand, Bob is target.
        var rb = RiftBoltFactory.Create(_alice);
        rb.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(rb);

        var agent = new ScriptedAgent();
        agent.QueueTargets(new object[] { _bob });
        agent.QueueMana(ManaPayment.Empty);

        var ctx = new GameContext(_alice, new[] { _alice, _bob },
            _alice, 1, PhaseStateType.PreCombatMain, _stack);

        var spell = await _flow.CastAsync(
            _alice, rb,
            RiftBoltFactory.BuildSpellDefinition(t => t),
            agent, ctx);

        rb.Zone.Should().Be(ZoneType.Stack);

        // Resolve — Bob takes 3 damage.
        spell.Resolve();

        _bob.LifeTotal.Should().Be(17);
    }

    [Fact]
    public void Suspend_Pay_R_ExilesWithOneTimeCounter()
    {
        var rb = RiftBoltFactory.Create(_alice);
        rb.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(rb);

        var registry = new SuspendedCardRegistry((_, _) => { /* test below */ });
        var suspend = RiftBoltFactory.BuildSuspendCost();

        suspend.TimeCounters.Should().Be(1);
        suspend.AlternativeManaCost.Should().Be(ManaCost.Parse("R"));

        suspend.ApplySuspend(rb, _alice, registry);

        rb.Zone.Should().Be(ZoneType.Exile);
        _alice.Zones.Exile.GetCards().Should().Contain(rb);
        registry.TimeCountersOn(rb).Should().Be(1);
    }

    [Fact]
    public async Task Suspend_FullCycle_CounterRemovedOnUpkeep_CastsForFreeAndDeals3()
    {
        // Rift Bolt in Alice's hand. Bob is target.
        var rb = RiftBoltFactory.Create(_alice);
        rb.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(rb);

        // We model "auto-cast on counter-zero" by capturing the ready
        // signal here, then casting on the test thread after the tick
        // fires. CR 702.62d — "the player casts it without paying its
        // mana cost". Reuse CastFromExileAlternativeCost({0}) for the
        // zero-cost cast-from-exile path: it gates on Zone == Exile +
        // owner-matches, which is exactly the state Rift Bolt is in
        // after suspend resolution.
        (ICard Card, Player Owner)? ready = null;
        var registry = new SuspendedCardRegistry(_bus, (card, owner) =>
            ready = (card, owner));

        // Suspend Rift Bolt.
        var suspend = RiftBoltFactory.BuildSuspendCost();
        suspend.ApplySuspend(rb, _alice, registry);

        rb.Zone.Should().Be(ZoneType.Exile);
        registry.TimeCountersOn(rb).Should().Be(1);

        // Fire Alice's upkeep on the bus — registry auto-ticks; counter
        // hits zero, the ready callback captures (card, owner).
        _bus.Publish(new StepStartedEvent(PhaseStateType.Upkeep, _alice));

        registry.IsTracked(rb).Should().BeFalse();
        ready.Should().NotBeNull("ready callback should have fired");
        rb.Zone.Should().Be(ZoneType.Exile,
            "card is still in exile until the free cast moves it to the stack");

        // Drive the free cast through SpellCastFlow with a {0}
        // alternative cost (the zero-cost form of cast-from-exile).
        var agent = new ScriptedAgent();
        agent.QueueTargets(new object[] { _bob });
        agent.QueueMana(ManaPayment.Empty);

        var ctx = new GameContext(_alice, new[] { _alice, _bob },
            _alice, 2, PhaseStateType.Upkeep, _stack);
        var freeCast = new CastFromExileAlternativeCost(
            "Suspend resolved (CR 702.62d)", ManaCost.Parse("0"));

        var spell = await _flow.CastAsync(
            ready!.Value.Owner, ready.Value.Card,
            RiftBoltFactory.BuildSpellDefinition(t => t),
            agent, ctx,
            alternativeCost: freeCast);

        rb.Zone.Should().Be(ZoneType.Stack);

        // Resolve the free-cast Rift Bolt — Bob takes 3 damage.
        spell.Resolve();
        _bob.LifeTotal.Should().Be(17);
    }

    [Fact]
    public async Task Suspend_FreeCast_WithSuspendFlag_StampsWasCastFromSuspendOnSpellAndCard()
    {
        // CR 702.62d / 702.62g — when SpellCastFlow sees a
        // CastFromExileAlternativeCost with IsSuspendCast=true, both the
        // resolving Spell and the underlying Card get the
        // WasCastFromSuspend sentinel stamped so creature-haste riders and
        // future "if cast via suspend" gates can branch.
        var rb = RiftBoltFactory.Create(_alice);
        rb.SetZone(ZoneType.Exile);
        _alice.Zones.Exile.AddCard(rb);

        var agent = new ScriptedAgent();
        agent.QueueTargets(new object[] { _bob });
        agent.QueueMana(ManaPayment.Empty);

        var ctx = new GameContext(_alice, new[] { _alice, _bob },
            _alice, 2, PhaseStateType.Upkeep, _stack);
        var freeCast = new CastFromExileAlternativeCost(
            "Suspend resolved (CR 702.62d)", ManaCost.Parse("0"), isSuspendCast: true);

        var spell = await _flow.CastAsync(
            _alice, rb,
            RiftBoltFactory.BuildSpellDefinition(t => t),
            agent, ctx,
            alternativeCost: freeCast);

        spell.WasCastFromSuspend.Should().BeTrue();
        rb.WasCastFromSuspend.Should().BeTrue();
    }

    [Fact]
    public async Task Suspend_FreeCast_WithoutSuspendFlag_DoesNotStampSentinel()
    {
        // Symmetry: a plain CastFromExileAlternativeCost without
        // IsSuspendCast leaves the sentinel false (Cascade / Plot / impulse
        // draw cast-from-exile paths use the no-flag overload).
        var rb = RiftBoltFactory.Create(_alice);
        rb.SetZone(ZoneType.Exile);
        _alice.Zones.Exile.AddCard(rb);

        var agent = new ScriptedAgent();
        agent.QueueTargets(new object[] { _bob });
        agent.QueueMana(ManaPayment.Empty);

        var ctx = new GameContext(_alice, new[] { _alice, _bob },
            _alice, 2, PhaseStateType.Upkeep, _stack);
        var freeCast = new CastFromExileAlternativeCost(
            "Generic cast from exile", ManaCost.Parse("0"));

        var spell = await _flow.CastAsync(
            _alice, rb,
            RiftBoltFactory.BuildSpellDefinition(t => t),
            agent, ctx,
            alternativeCost: freeCast);

        spell.WasCastFromSuspend.Should().BeFalse();
        rb.WasCastFromSuspend.Should().BeFalse();
    }

    [Fact]
    public void Sanity_UntrackedCard_DoesNotTriggerFreeCastOnUpkeep()
    {
        // A card never suspended (no time counters, not in the registry)
        // must not trigger the free-cast callback when an upkeep ticks.
        // Guards against the registry firing on cards it never saw.
        var rb = RiftBoltFactory.Create(_alice);
        rb.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(rb);

        var fired = 0;
        var registry = new SuspendedCardRegistry((_, _) => fired++);

        registry.IsTracked(rb).Should().BeFalse();
        registry.TimeCountersOn(rb).Should().Be(0);

        // Tick Alice's upkeep multiple times — nothing happens.
        registry.TickUpkeep(_alice);
        registry.TickUpkeep(_alice);
        fired.Should().Be(0);
        rb.Zone.Should().Be(ZoneType.Hand);
    }
}
