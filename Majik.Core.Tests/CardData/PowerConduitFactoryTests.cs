using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Counters;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;
using Artifact = Majik.Core.Cards.Artifact;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Unit tests for <see cref="PowerConduitFactory"/>.
///
/// Card: Power Conduit — Artifact {1} (Darksteel — v1 brief).
///
/// v1 oracle (per factory):
///   "{T}: Remove a counter from a permanent you control and put a
///    counter of the same type on another permanent you control."
///
/// Covers:
///   - Identity / dispatch.
///   - Activated ability has a tap cost + two TargetRequest slots
///     (source + destination).
///   - Resolve: counter moves source → target, same type.
///   - Multiple counter-type sources: picks one available type.
///   - Source with no counters: silent no-op.
///   - Source = target rejected ("another permanent" guard).
///   - Opponent-controlled permanents (source or target) rejected.
///   - Replacement bus integration: Hardened Scales bumps the +1/+1
///     placed on the destination (source still loses exactly 1).
///   - CounterAddedEvent fires for the destination placement.
/// </summary>
public class PowerConduitFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    private static void PutOnBattlefield(Player owner, Card card)
    {
        card.SetOwner(owner);
        card.SetController(owner);
        owner.Zones.Battlefield.AddCard(card);
        card.SetZone(ZoneType.Battlefield);
    }

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void PowerConduit_Identity()
    {
        var c = PowerConduitFactory.Create(_alice);

        c.Name.Should().Be("Power Conduit");
        c.ManaCost.Should().Be("{1}");
        c.HasType(CardType.Artifact).Should().BeTrue();
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_PowerConduit()
    {
        var card = NamedCardFactory.Create("Power Conduit", _alice);

        card.Should().BeOfType<Artifact>();
        card.Name.Should().Be("Power Conduit");
        card.HasType(CardType.Artifact).Should().BeTrue();
        card.Abilities.OfType<ActivatedAbility>().Should().HaveCount(1);
    }

    // -----------------------------------------------------------------------
    // Activated ability shape
    // -----------------------------------------------------------------------

    [Fact]
    public void ActivatedAbility_HasTapCost_AndTwoTargetSlots()
    {
        var pc = PowerConduitFactory.Create(_alice);

        var activated = pc.Abilities.OfType<ActivatedAbility>().Single();
        activated.Costs.OfType<AdditionalCost>().Should().ContainSingle(
            "tap is the printed activation cost (no mana cost in v1)");
        activated.TargetRequests.Should().HaveCount(2,
            "two target slots: source + destination");
    }

    // -----------------------------------------------------------------------
    // Resolve — counter moves source → target, same type
    // -----------------------------------------------------------------------

    [Fact]
    public void Activated_Resolve_MovesPlusOneCounterFromSourceToTarget()
    {
        var pc = PowerConduitFactory.Create(_alice);
        PutOnBattlefield(_alice, pc);

        var source = new Creature("Source", "{1}", 1, 1);
        source.AddCardType(CardType.Artifact);
        PutOnBattlefield(_alice, source);
        source.Counters.Add(CounterType.PlusOnePlusOne, 3);

        var dest = new Creature("Dest", "{2}", 2, 2);
        PutOnBattlefield(_alice, dest);

        var activated = pc.Abilities.OfType<ActivatedAbility>().Single();
        activated.SetChosenTargets(new[]
        {
            new object[] { source },
            new object[] { dest },
        });
        foreach (var e in activated.Effects) e.Execute();

        source.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(2,
            "one +1/+1 counter removed from the source");
        dest.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(1,
            "one +1/+1 counter placed on the destination");
    }

    [Fact]
    public void Activated_Resolve_MovesChargeCounter_SameType()
    {
        var pc = PowerConduitFactory.Create(_alice);
        PutOnBattlefield(_alice, pc);

        var source = new Artifact("Mox-shape", "{0}");
        PutOnBattlefield(_alice, source);
        source.Counters.Add(CounterType.Charge, 2);

        var dest = new Artifact("Another artifact", "{0}");
        PutOnBattlefield(_alice, dest);

        var activated = pc.Abilities.OfType<ActivatedAbility>().Single();
        activated.SetChosenTargets(new[]
        {
            new object[] { source },
            new object[] { dest },
        });
        foreach (var e in activated.Effects) e.Execute();

        source.Counters.Count(CounterType.Charge).Should().Be(1);
        dest.Counters.Count(CounterType.Charge).Should().Be(1,
            "same counter type carried source → destination");
        dest.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(0);
    }

    [Fact]
    public void Activated_Resolve_SourceWithNoCounters_NoOp()
    {
        var pc = PowerConduitFactory.Create(_alice);
        PutOnBattlefield(_alice, pc);

        var source = new Creature("Source", "{1}", 1, 1);
        PutOnBattlefield(_alice, source);
        // No counters on source.

        var dest = new Creature("Dest", "{2}", 2, 2);
        PutOnBattlefield(_alice, dest);

        var activated = pc.Abilities.OfType<ActivatedAbility>().Single();
        activated.SetChosenTargets(new[]
        {
            new object[] { source },
            new object[] { dest },
        });
        foreach (var e in activated.Effects) e.Execute();

        dest.Counters.HasAny.Should().BeFalse("source had no counter to move");
    }

    [Fact]
    public void Activated_Resolve_SourceEqualsTarget_NoOp()
    {
        var pc = PowerConduitFactory.Create(_alice);
        PutOnBattlefield(_alice, pc);

        var same = new Creature("Same", "{1}", 1, 1);
        PutOnBattlefield(_alice, same);
        same.Counters.Add(CounterType.PlusOnePlusOne, 1);

        var activated = pc.Abilities.OfType<ActivatedAbility>().Single();
        activated.SetChosenTargets(new[]
        {
            new object[] { same },
            new object[] { same },
        });
        foreach (var e in activated.Effects) e.Execute();

        // "Another permanent" — source = target is illegal on resolve.
        same.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(1,
            "self-targeting rejected; counter neither removed nor placed");
    }

    [Fact]
    public void Activated_Resolve_OpponentControlled_NoOp()
    {
        var pc = PowerConduitFactory.Create(_alice);
        PutOnBattlefield(_alice, pc);

        // Source is on Bob's side — illegal source.
        var source = new Creature("Bob's source", "{1}", 1, 1);
        PutOnBattlefield(_bob, source);
        source.Counters.Add(CounterType.PlusOnePlusOne, 1);

        var dest = new Creature("Alice's dest", "{2}", 2, 2);
        PutOnBattlefield(_alice, dest);

        var activated = pc.Abilities.OfType<ActivatedAbility>().Single();
        activated.SetChosenTargets(new[]
        {
            new object[] { source },
            new object[] { dest },
        });
        foreach (var e in activated.Effects) e.Execute();

        source.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(1,
            "opponent's permanent isn't a legal source");
        dest.Counters.HasAny.Should().BeFalse();
    }

    [Fact]
    public void Activated_Resolve_RoutesPlacementThroughReplacementBus_HardenedScalesBumps()
    {
        var bus = new ReplacementBus();
        var pc = PowerConduitFactory.Create(_alice, bus, eventBus: null);
        PutOnBattlefield(_alice, pc);

        var scales = HardenedScalesFactory.Create(_alice, bus);
        PutOnBattlefield(_alice, scales);

        var source = new Creature("Source", "{1}", 1, 1);
        PutOnBattlefield(_alice, source);
        source.Counters.Add(CounterType.PlusOnePlusOne, 1);

        var dest = new Creature("Dest", "{2}", 2, 2);
        PutOnBattlefield(_alice, dest);

        var activated = pc.Abilities.OfType<ActivatedAbility>().Single();
        activated.SetChosenTargets(new[]
        {
            new object[] { source },
            new object[] { dest },
        });
        foreach (var e in activated.Effects) e.Execute();

        source.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(0,
            "exactly one +1/+1 counter removed regardless of Hardened Scales");
        dest.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(2,
            "Hardened Scales bumps the destination placement +1 → +2");
    }

    [Fact]
    public void Activated_Resolve_PublishesCounterAddedEvent_OnDestination()
    {
        var bus = new EventBus();
        var observed = new List<CounterAddedEvent>();
        bus.Subscribe<CounterAddedEvent>(observed.Add);

        var pc = PowerConduitFactory.Create(_alice, replacements: null, eventBus: bus);
        PutOnBattlefield(_alice, pc);

        var source = new Creature("Source", "{1}", 1, 1);
        PutOnBattlefield(_alice, source);
        source.Counters.Add(CounterType.PlusOnePlusOne, 1);

        var dest = new Creature("Dest", "{2}", 2, 2);
        PutOnBattlefield(_alice, dest);

        var activated = pc.Abilities.OfType<ActivatedAbility>().Single();
        activated.SetChosenTargets(new[]
        {
            new object[] { source },
            new object[] { dest },
        });
        foreach (var e in activated.Effects) e.Execute();

        observed.Should().HaveCount(1,
            "single CounterAddedEvent for the destination placement");
        observed[0].Target.Should().BeSameAs(dest);
        observed[0].CounterType.Should().Be(CounterType.PlusOnePlusOne);
        observed[0].Amount.Should().Be(1);
    }
}
