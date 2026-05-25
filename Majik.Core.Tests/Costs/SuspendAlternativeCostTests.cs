using FluentAssertions;
using Majik.Core.Cards;
using Majik.Core.Costs;
using Majik.Core.Counters;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.StateMachine;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.Costs;

/// <summary>
/// Tests for <see cref="SuspendAlternativeCost"/> and the per-upkeep
/// counter tick implemented by <see cref="SuspendedCardRegistry"/>.
/// Covers the cost legality gate, the Hand → Exile mutation, the upkeep
/// tick semantics, and the "cast for free" ready callback contract.
/// </summary>
public class SuspendAlternativeCostTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    [Fact]
    public void CanCastFor_CardInOwnersHand_ReturnsTrue()
    {
        var bolt = new Sorcery("Rift Bolt", "{2}{R}");
        bolt.SetOwner(_alice);
        bolt.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(bolt);

        var suspend = new SuspendAlternativeCost(1, ManaCost.Parse("R"));

        suspend.CanCastFor(bolt, _alice).Should().BeTrue();
    }

    [Fact]
    public void CanCastFor_CardNotInHand_ReturnsFalse()
    {
        var bolt = new Sorcery("Rift Bolt", "{2}{R}");
        bolt.SetOwner(_alice);
        bolt.SetZone(ZoneType.Graveyard);

        var suspend = new SuspendAlternativeCost(1, ManaCost.Parse("R"));

        suspend.CanCastFor(bolt, _alice).Should().BeFalse(
            "suspend is paid from the hand (CR 702.62b).");
    }

    [Fact]
    public void CanCastFor_DifferentOwner_ReturnsFalse()
    {
        var bolt = new Sorcery("Rift Bolt", "{2}{R}");
        bolt.SetOwner(_alice);
        bolt.SetZone(ZoneType.Hand);

        var suspend = new SuspendAlternativeCost(1, ManaCost.Parse("R"));

        suspend.CanCastFor(bolt, _bob).Should().BeFalse(
            "only the card's owner may suspend it.");
    }

    [Fact]
    public void Description_FormatsAsSuspendNCost()
    {
        var suspend = new SuspendAlternativeCost(3, ManaCost.Parse("1R"));

        suspend.Description.Should().StartWith("Suspend 3—");
        suspend.AlternativeManaCost.TotalValue.Should().Be(2);
    }

    [Fact]
    public void Constructor_NegativeN_Throws()
    {
        var act = () => new SuspendAlternativeCost(-1, ManaCost.Parse("R"));
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void ApplySuspend_MovesCardToExileWithTimeCounters()
    {
        var bolt = new Sorcery("Rift Bolt", "{2}{R}");
        bolt.SetOwner(_alice);
        bolt.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(bolt);

        var registry = new SuspendedCardRegistry((_, _) => { /* no-op ready */ });
        var suspend = new SuspendAlternativeCost(1, ManaCost.Parse("R"));

        suspend.ApplySuspend(bolt, _alice, registry);

        bolt.Zone.Should().Be(ZoneType.Exile);
        _alice.Zones.Hand.GetCards().Should().NotContain(bolt);
        _alice.Zones.Exile.GetCards().Should().Contain(bolt);

        registry.IsTracked(bolt).Should().BeTrue();
        registry.TimeCountersOn(bolt).Should().Be(1);
    }

    [Fact]
    public void ApplySuspend_NotInHand_Throws()
    {
        var bolt = new Sorcery("Rift Bolt", "{2}{R}");
        bolt.SetOwner(_alice);
        bolt.SetZone(ZoneType.Exile); // already suspended? not legal to re-suspend

        var registry = new SuspendedCardRegistry((_, _) => { });
        var suspend = new SuspendAlternativeCost(2, ManaCost.Parse("R"));

        var act = () => suspend.ApplySuspend(bolt, _alice, registry);
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void TickUpkeep_RemovesOneTimeCounterPerOwnerUpkeep()
    {
        var bolt = new Sorcery("Rift Bolt", "{2}{R}");
        bolt.SetOwner(_alice);
        bolt.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(bolt);

        var fired = 0;
        var registry = new SuspendedCardRegistry((_, _) => fired++);
        var suspend = new SuspendAlternativeCost(2, ManaCost.Parse("R"));
        suspend.ApplySuspend(bolt, _alice, registry);

        // First Alice upkeep — 2 → 1, no fire yet.
        registry.TickUpkeep(_alice);
        registry.TimeCountersOn(bolt).Should().Be(1);
        fired.Should().Be(0);

        // Bob's upkeep — Alice's card untouched (CR 702.62c only ticks on
        // owner's own upkeeps).
        registry.TickUpkeep(_bob);
        registry.TimeCountersOn(bolt).Should().Be(1);
        fired.Should().Be(0);

        // Second Alice upkeep — 1 → 0, fires the ready callback, drops
        // the entry from the registry.
        registry.TickUpkeep(_alice);
        fired.Should().Be(1);
        registry.IsTracked(bolt).Should().BeFalse();
    }

    [Fact]
    public void TickUpkeep_FiresReadyOnceAtZero_ThenStops()
    {
        var bolt = new Sorcery("Rift Bolt", "{2}{R}");
        bolt.SetOwner(_alice);
        bolt.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(bolt);

        var fired = 0;
        var registry = new SuspendedCardRegistry((_, _) => fired++);
        var suspend = new SuspendAlternativeCost(1, ManaCost.Parse("R"));
        suspend.ApplySuspend(bolt, _alice, registry);

        registry.TickUpkeep(_alice);
        fired.Should().Be(1);

        // Subsequent upkeeps: no double-fire (entry already gone).
        registry.TickUpkeep(_alice);
        registry.TickUpkeep(_alice);
        fired.Should().Be(1);
    }

    [Fact]
    public void Registry_SubscribesToBus_AutoTicksOnUpkeepEvent()
    {
        var bus = new EventBus();
        var bolt = new Sorcery("Rift Bolt", "{2}{R}");
        bolt.SetOwner(_alice);
        bolt.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(bolt);

        var fired = 0;
        var registry = new SuspendedCardRegistry(bus, (_, _) => fired++);

        var suspend = new SuspendAlternativeCost(1, ManaCost.Parse("R"));
        suspend.ApplySuspend(bolt, _alice, registry);

        // Non-upkeep events are ignored.
        bus.Publish(new StepStartedEvent(PhaseStateType.Draw, _alice));
        registry.TimeCountersOn(bolt).Should().Be(1);
        fired.Should().Be(0);

        // Upkeep of opponent — not the owner — also ignored.
        bus.Publish(new StepStartedEvent(PhaseStateType.Upkeep, _bob));
        registry.TimeCountersOn(bolt).Should().Be(1);

        // Owner's upkeep — ticks and fires.
        bus.Publish(new StepStartedEvent(PhaseStateType.Upkeep, _alice));
        fired.Should().Be(1);
        registry.IsTracked(bolt).Should().BeFalse();
    }

    [Fact]
    public void TimeCounter_IsPublic()
    {
        // Sanity: CounterType.Time is defined and reachable from consumers.
        CounterType.Time.Should().NotBeNull();
        CounterType.Time.Name.Should().Be("Time");
    }

    [Fact]
    public void TickUpkeep_PublishesSuspendCounterDrainedEvent_AtCounterZero()
    {
        // CR 702.62d — when the last time counter is removed, the registry
        // publishes a SuspendCounterDrainedEvent BEFORE firing the ready
        // callback. Diagnostic hook independent of the cast pipeline.
        var bus = new EventBus();
        var bolt = new Sorcery("Rift Bolt", "{2}{R}");
        bolt.SetOwner(_alice);
        bolt.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(bolt);

        SuspendCounterDrainedEvent? captured = null;
        bus.Subscribe<SuspendCounterDrainedEvent>(e => captured = e);

        var fired = 0;
        var registry = new SuspendedCardRegistry(bus, (_, _) => fired++);
        var suspend = new SuspendAlternativeCost(1, ManaCost.Parse("R"));
        suspend.ApplySuspend(bolt, _alice, registry);

        bus.Publish(new StepStartedEvent(PhaseStateType.Upkeep, _alice));

        captured.Should().NotBeNull();
        captured!.Card.Should().BeSameAs(bolt);
        captured.Owner.Should().BeSameAs(_alice);
        fired.Should().Be(1, "ready callback still fires after the drain event");
    }

    [Fact]
    public void TickUpkeep_DoesNotPublishDrainEvent_WhileCountersRemain()
    {
        var bus = new EventBus();
        var bolt = new Sorcery("Rift Bolt", "{2}{R}");
        bolt.SetOwner(_alice);
        bolt.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(bolt);

        var drained = 0;
        bus.Subscribe<SuspendCounterDrainedEvent>(_ => drained++);

        var registry = new SuspendedCardRegistry(bus, (_, _) => { });
        var suspend = new SuspendAlternativeCost(2, ManaCost.Parse("R"));
        suspend.ApplySuspend(bolt, _alice, registry);

        // First upkeep: 2 → 1, no drain event.
        bus.Publish(new StepStartedEvent(PhaseStateType.Upkeep, _alice));
        drained.Should().Be(0, "the card still has 1 counter");

        // Second upkeep: 1 → 0, drain fires exactly once.
        bus.Publish(new StepStartedEvent(PhaseStateType.Upkeep, _alice));
        drained.Should().Be(1);
    }

    [Fact]
    public void CastFromExileAlternativeCost_IsSuspendCast_DefaultsFalse()
    {
        var alt = new CastFromExileAlternativeCost("Generic", ManaCost.Parse("0"));
        alt.IsSuspendCast.Should().BeFalse();
    }

    [Fact]
    public void CastFromExileAlternativeCost_IsSuspendCast_HonoursConstructorFlag()
    {
        var alt = new CastFromExileAlternativeCost(
            "Suspend resolved (CR 702.62d)", ManaCost.Parse("0"), isSuspendCast: true);
        alt.IsSuspendCast.Should().BeTrue();
    }

    [Fact]
    public void SuspendHasteEffect_GrantsHaste_WhileCreatureOnBattlefield()
    {
        // CR 702.62g — a creature cast via suspend gains haste until it
        // leaves the battlefield. The continuous effect grants Haste in
        // Layer 6 (Abilities) while target.Zone == Battlefield.
        var grizzly = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        grizzly.SetOwner(_alice);
        grizzly.SetController(_alice);
        grizzly.SetZone(ZoneType.Battlefield);

        var effects = new ContinuousEffectsService();
        grizzly.ActiveEffects = effects;
        effects.Register(new SuspendHasteEffect(grizzly));

        var chars = effects.Compute(grizzly);
        chars.Keywords.Should().Contain("Haste",
            "the haste grant is active while the creature is on the battlefield (CR 702.62g).");
    }

    [Fact]
    public void SuspendHasteEffect_DropsOnLeaveBattlefield()
    {
        var grizzly = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        grizzly.SetOwner(_alice);
        grizzly.SetController(_alice);
        grizzly.SetZone(ZoneType.Battlefield);

        var effects = new ContinuousEffectsService();
        grizzly.ActiveEffects = effects;
        var haste = new SuspendHasteEffect(grizzly);
        effects.Register(haste);

        effects.Compute(grizzly).Keywords.Should().Contain("Haste");

        // Creature dies → goes to graveyard. The effect self-deactivates
        // via IsActive(); Prune drops it.
        grizzly.SetZone(ZoneType.Graveyard);
        haste.IsActive().Should().BeFalse();

        effects.Prune();
        effects.Compute(grizzly).Keywords.Should().NotContain("Haste");
    }

    [Fact]
    public void SuspendHasteEffect_StaysActive_WhileOnStack()
    {
        // Pre-resolve: the spell is on the stack. The grant attaches at
        // cast time so it's in place the instant the permanent ETBs.
        var grizzly = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        grizzly.SetOwner(_alice);
        grizzly.SetController(_alice);
        grizzly.SetZone(ZoneType.Stack);

        var haste = new SuspendHasteEffect(grizzly);
        haste.IsActive().Should().BeTrue("the grant rides from the spell into the permanent (CR 702.62g).");
    }
}
