using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Counters;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="EvolutionSageFactory"/> (War of the Spark, {2}{G}).
///
/// Oracle:
///   "Landfall — Whenever a land you control enters, proliferate."
///
/// Covers:
/// - Identity (Creature — Elf Druid, 3/2, {2}{G}, Green, owner / controller).
/// - <see cref="NamedCardFactory"/> dispatch.
/// - Landfall trigger attached (CR 603.1 / 603.6a / 702.142), no target
///   (proliferate self-selects, CR 701.27).
/// - A land entering under the controller's control queues the trigger; on
///   resolve it proliferates every controller-side permanent that already has
///   a counter (CR 701.27).
/// - Trigger does NOT fire when a land enters under the opponent's control.
/// </summary>
[Trait("Color", "G")]
public class EvolutionSageFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void EvolutionSage_Identity_CreatureElfDruid_3_2_Green2G()
    {
        var sage = EvolutionSageFactory.Create(_alice);

        sage.Name.Should().Be("Evolution Sage");
        sage.HasType(CardType.Creature).Should().BeTrue();
        sage.ManaCost.Should().Be("{2}{G}");
        sage.ManaCostValue.TotalValue.Should().Be(3);
        CardColors.GetColors(sage).Should().Contain(ManaColor.Green);
        sage.Power.Should().Be(3);
        sage.Toughness.Should().Be(2);
        sage.Subtypes.Should().Contain(CardSubtype.Elf);
        sage.Subtypes.Should().Contain(CardSubtype.Druid);
        sage.Owner.Should().BeSameAs(_alice);
        sage.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatch_ReturnsCreature()
    {
        var card = NamedCardFactory.Create("Evolution Sage", _alice);

        card.Should().BeOfType<Creature>();
        card.Name.Should().Be("Evolution Sage");
        card.HasType(CardType.Creature).Should().BeTrue();
    }

    [Fact]
    public void EvolutionSage_LandfallTrigger_NoTarget()
    {
        var sage = EvolutionSageFactory.Create(_alice);

        var trigger = sage.Abilities.OfType<TriggeredAbility>().Should().ContainSingle().Subject;
        trigger.Source.Should().BeSameAs(sage);
        trigger.Controller.Should().BeSameAs(_alice);
        trigger.ActiveZones.Should().Contain(ZoneType.Battlefield);
        trigger.TargetRequests.Should().BeEmpty(
            "proliferate self-selects its permanent/player set — nothing to target (CR 701.27)");
    }

    // -----------------------------------------------------------------------
    // Landfall — fires on owner's land ETB, proliferates
    // -----------------------------------------------------------------------

    [Fact]
    public void EvolutionSage_OwnersLandEnters_QueuesTrigger_Proliferates()
    {
        var (zones, stack, triggers) = BuildEngine();

        // Evolution Sage in play under Alice's control.
        var sage = EvolutionSageFactory.Create(_alice, triggers);
        _alice.Zones.Battlefield.AddCard(sage);
        sage.SetZone(ZoneType.Battlefield);
        triggers.BindCard(sage);

        // A permanent that already has a +1/+1 counter (gets proliferated)
        // and one with no counters (skipped).
        var counted = new Creature("Walking Ballista", "{0}", 0, 0);
        counted.SetOwner(_alice);
        counted.SetController(_alice);
        counted.Counters.Add(CounterType.PlusOnePlusOne, 2);
        _alice.Zones.Battlefield.AddCard(counted);
        counted.SetZone(ZoneType.Battlefield);

        var uncountered = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        uncountered.SetOwner(_alice);
        uncountered.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(uncountered);
        uncountered.SetZone(ZoneType.Battlefield);

        // Drop a land under Alice's control.
        var forest = new Land("Forest");
        forest.SetOwner(_alice);
        forest.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(forest);

        zones.MoveCardTo(forest, ZoneType.Battlefield, controller: _alice);

        triggers.PendingCount.Should().Be(1,
            "landfall trigger must queue when a land enters under controller's control");

        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        counted.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(3,
            "proliferate adds one more counter of an existing kind (CR 701.27)");
        uncountered.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(0,
            "permanents with no counters are NOT touched by proliferate");
    }

    [Fact]
    public void EvolutionSage_OpponentsLandEnters_DoesNotFire()
    {
        var (zones, _, triggers) = BuildEngine();

        var sage = EvolutionSageFactory.Create(_alice, triggers);
        _alice.Zones.Battlefield.AddCard(sage);
        sage.SetZone(ZoneType.Battlefield);
        triggers.BindCard(sage);

        // Bob plays a land — should NOT fire Alice's landfall.
        var swamp = new Land("Swamp");
        swamp.SetOwner(_bob);
        swamp.SetZone(ZoneType.Hand);
        _bob.Zones.Hand.AddCard(swamp);

        zones.MoveCardTo(swamp, ZoneType.Battlefield, controller: _bob);

        triggers.PendingCount.Should().Be(0,
            "landfall only triggers on a land entering under YOUR control");
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static (ZoneService zones, Majik.Core.Stack.Stack stack, TriggerManager triggers) BuildEngine()
    {
        var bus = new EventBus();
        var zones = new ZoneService(bus);
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);
        return (zones, stack, triggers);
    }
}
