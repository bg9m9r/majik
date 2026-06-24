using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Counters;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="MossbornHydraFactory"/>.
///
/// Card: Mossborn Hydra (Zendikar Rising Commander, {2}{G}) — Creature —
/// Elemental Hydra 0/0. Oracle (verified against Scryfall):
///   "Trample
///    This creature enters with a +1/+1 counter on it.
///    Landfall — Whenever a land you control enters, double the number of
///    +1/+1 counters on this creature."
///
/// Covers ONLY the card's UNIQUE behaviour (the landfall counter-doubling
/// trigger) plus a single identity assert (mana cost / P-T / subtypes /
/// Trample marker). NamedCardFactory dispatch + well-formedness are already
/// asserted for every implemented card by CardFactoryContractTests.
///
/// The "enters with a +1/+1 counter" clause is owned by EntersWithCountersBinder
/// on the production route (same posture as Goldvein Hydra), so this factory
/// does NOT wire it — the test seeds the counter directly before exercising the
/// doubling, which is the card's distinctive mechanic.
/// </summary>
[Trait("Color", "G")]
public class MossbornHydraFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void Identity_ElementalHydra_00_At2G_WithTrample()
    {
        var hydra = MossbornHydraFactory.Create(_alice);

        hydra.Name.Should().Be("Mossborn Hydra");
        hydra.ManaCost.Should().Be("{2}{G}");
        hydra.HasType(CardType.Creature).Should().BeTrue();
        hydra.HasSubtype(CardSubtype.Elemental).Should().BeTrue();
        hydra.HasSubtype(CardSubtype.Hydra).Should().BeTrue();
        hydra.BasePower.Should().Be(0);
        hydra.BaseToughness.Should().Be(0);
        hydra.Owner.Should().BeSameAs(_alice);
        hydra.Controller.Should().BeSameAs(_alice);

        // CR 702.19 — Trample keyword marker.
        hydra.Abilities.OfType<KeywordAbility>()
            .Any(k => string.Equals(k.Keyword, "Trample", System.StringComparison.OrdinalIgnoreCase))
            .Should().BeTrue("Mossborn Hydra has Trample");

        // Exactly one landfall trigger.
        hydra.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1);
    }

    // -----------------------------------------------------------------------
    // Landfall — double the +1/+1 counters on this creature (CR 121.4)
    // -----------------------------------------------------------------------

    [Fact]
    public void LandEntersUnderController_DoublesPlusOnePlusOneCountersOnSelf()
    {
        var (zones, stack, triggers) = BuildEngine();

        var hydra = MossbornHydraFactory.Create(_alice, triggers);
        hydra.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(hydra);

        // Seed the ETB +1/+1 counter the binder would place in prod, plus one
        // more (3 total) so the doubling is observable as 3 → 6.
        hydra.Counters.Add(CounterType.PlusOnePlusOne, 3);

        // Play a Forest under Alice's control via ZoneService.
        var forest = new Land("Forest", new[] { CardSupertype.Basic }, new[] { CardSubtype.Forest });
        forest.SetOwner(_alice);
        _alice.Zones.Hand.AddCard(forest);
        forest.SetZone(ZoneType.Hand);

        zones.MoveCardTo(forest, ZoneType.Battlefield, controller: _alice);

        triggers.PendingCount.Should().Be(1, "exactly one landfall trigger should be queued");

        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        hydra.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(6,
            "CR 121.4 — landfall doubles the +1/+1 counters on this creature (3 → 6)");
    }

    [Fact]
    public void LandEntersUnderController_WithZeroCounters_StaysZero()
    {
        var (zones, stack, triggers) = BuildEngine();

        var hydra = MossbornHydraFactory.Create(_alice, triggers);
        hydra.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(hydra);

        var forest = new Land("Forest", new[] { CardSupertype.Basic }, new[] { CardSubtype.Forest });
        forest.SetOwner(_alice);
        _alice.Zones.Hand.AddCard(forest);
        forest.SetZone(ZoneType.Hand);

        zones.MoveCardTo(forest, ZoneType.Battlefield, controller: _alice);

        triggers.PendingCount.Should().Be(1);
        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        hydra.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(0,
            "doubling zero +1/+1 counters leaves the count at zero");
    }

    [Fact]
    public void NonLandEnters_NoTrigger()
    {
        var (zones, _, triggers) = BuildEngine();

        var hydra = MossbornHydraFactory.Create(_alice, triggers);
        hydra.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(hydra);

        var bear = new Creature("Grizzly Bears", "1G", 2, 2);
        bear.SetOwner(_alice);
        _alice.Zones.Hand.AddCard(bear);
        bear.SetZone(ZoneType.Hand);

        zones.MoveCardTo(bear, ZoneType.Battlefield, controller: _alice);

        triggers.PendingCount.Should().Be(0,
            "landfall gates on HasType(Land); a creature ETB doesn't match");
    }

    [Fact]
    public void LandEntersUnderOpponent_NoTrigger()
    {
        var (zones, _, triggers) = BuildEngine();

        var hydra = MossbornHydraFactory.Create(_alice, triggers);
        hydra.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(hydra);

        var bobForest = new Land("Forest", new[] { CardSupertype.Basic }, new[] { CardSubtype.Forest });
        bobForest.SetOwner(_bob);
        _bob.Zones.Hand.AddCard(bobForest);
        bobForest.SetZone(ZoneType.Hand);

        zones.MoveCardTo(bobForest, ZoneType.Battlefield, controller: _bob);

        triggers.PendingCount.Should().Be(0,
            "opponent's land does not satisfy 'a land you control'");
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static (ZoneService zones, Majik.Core.Stack.Stack stack, TriggerManager triggers) BuildEngine()
    {
        var bus = new EventBus();
        var rep = new ReplacementBus();
        var zones = new ZoneService(bus, rep);
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);
        return (zones, stack, triggers);
    }
}
