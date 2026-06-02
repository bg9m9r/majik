using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Effects;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Counters;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="BristlyBillSpineSowerFactory"/>.
///
/// Card: Bristly Bill, Spine Sower (Bloomburrow, {1}{G}) — Legendary
/// Creature — Plant Druid 2/2. Oracle (verified against Scryfall):
///   "Landfall — Whenever a land you control enters, put a +1/+1 counter on
///    target creature.
///    {3}{G}{G}: Double the number of +1/+1 counters on each creature you
///    control."
///
/// Covers:
///   - Card identity (name, Legendary supertype, Plant + Druid subtypes,
///     {1}{G}, 2/2, owner / controller, one TriggeredAbility + one
///     ActivatedAbility).
///   - <see cref="NamedCardFactory"/> dispatch hands back the same shape.
///   - End-to-end landfall trigger: land ETB under controller → a +1/+1
///     counter lands on the chosen target creature.
///   - Trigger does NOT fire on a non-land ETB or an opponent's land ETB.
///   - Activated {3}{G}{G} ability doubles +1/+1 counters on each creature
///     the controller controls (2 → 4), leaving counterless creatures and
///     opponents' creatures alone.
/// </summary>
public class BristlyBillSpineSowerFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void Identity_LegendaryPlantDruid_22_At1G()
    {
        var bill = BristlyBillSpineSowerFactory.Create(_alice);

        bill.Name.Should().Be("Bristly Bill, Spine Sower");
        bill.ManaCost.Should().Be("{1}{G}");
        bill.HasType(CardType.Creature).Should().BeTrue();
        bill.HasSupertype(CardSupertype.Legendary).Should().BeTrue();
        bill.HasSubtype(CardSubtype.Plant).Should().BeTrue();
        bill.HasSubtype(CardSubtype.Druid).Should().BeTrue();
        bill.BasePower.Should().Be(2);
        bill.BaseToughness.Should().Be(2);
        bill.Owner.Should().BeSameAs(_alice);
        bill.Controller.Should().BeSameAs(_alice);

        bill.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1);
        bill.Abilities.OfType<ActivatedAbility>().Should().HaveCount(1);
    }

    [Fact]
    public void NamedCardFactory_DispatchesShape()
    {
        var card = NamedCardFactory.Create("Bristly Bill, Spine Sower", _alice);

        card.Should().BeOfType<Creature>();
        card.Name.Should().Be("Bristly Bill, Spine Sower");
        card.ManaCost.Should().Be("{1}{G}");
        card.HasSupertype(CardSupertype.Legendary).Should().BeTrue();
        card.HasSubtype(CardSubtype.Plant).Should().BeTrue();
        card.HasSubtype(CardSubtype.Druid).Should().BeTrue();
        card.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1);
        card.Abilities.OfType<ActivatedAbility>().Should().HaveCount(1);
    }

    // -----------------------------------------------------------------------
    // Landfall trigger — +1/+1 counter on target creature
    // -----------------------------------------------------------------------

    [Fact]
    public void LandEntersUnderController_PutsCounterOnTargetCreature()
    {
        var (zones, stack, triggers) = BuildEngine();

        var bill = BristlyBillSpineSowerFactory.Create(_alice, triggers);
        bill.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(bill);

        // A separate creature to receive the counter.
        var bear = new Creature("Grizzly Bears", "1G", 2, 2);
        bear.SetOwner(_alice);
        bear.SetController(_alice);
        bear.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(bear);

        // Play a Forest under Alice's control via ZoneService.
        var forest = new Land("Forest", new[] { CardSupertype.Basic }, new[] { CardSubtype.Forest });
        forest.SetOwner(_alice);
        _alice.Zones.Hand.AddCard(forest);
        forest.SetZone(ZoneType.Hand);

        zones.MoveCardTo(forest, ZoneType.Battlefield, controller: _alice);

        triggers.PendingCount.Should().Be(1, "exactly one landfall trigger should be queued");

        // Choose the bear as the target, then resolve.
        var trigger = bill.Abilities.OfType<TriggeredAbility>().Single();
        trigger.SetChosenTargets(new IReadOnlyList<object>[] { new object[] { bear } });

        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        bear.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(1,
            "the landfall trigger puts a +1/+1 counter on the chosen target creature");
    }

    [Fact]
    public void NonLandEnters_NoTrigger()
    {
        var (zones, _, triggers) = BuildEngine();

        var bill = BristlyBillSpineSowerFactory.Create(_alice, triggers);
        bill.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(bill);

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

        var bill = BristlyBillSpineSowerFactory.Create(_alice, triggers);
        bill.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(bill);

        var bobForest = new Land("Forest", new[] { CardSupertype.Basic }, new[] { CardSubtype.Forest });
        bobForest.SetOwner(_bob);
        _bob.Zones.Hand.AddCard(bobForest);
        bobForest.SetZone(ZoneType.Hand);

        zones.MoveCardTo(bobForest, ZoneType.Battlefield, controller: _bob);

        triggers.PendingCount.Should().Be(0,
            "opponent's land does not satisfy 'a land you control'");
    }

    // -----------------------------------------------------------------------
    // Activated ability — double +1/+1 counters on each creature you control
    // -----------------------------------------------------------------------

    [Fact]
    public void DoubleCounters_DoublesPlusOnePlusOneOnControlledCreatures_Only()
    {
        var bill = BristlyBillSpineSowerFactory.Create(_alice);
        bill.SetZone(ZoneType.Battlefield);
        bill.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(bill);

        // Alice's creature with 2 +1/+1 counters → should become 4.
        var withCounters = new Creature("Counter Bear", "1G", 2, 2);
        withCounters.SetOwner(_alice);
        withCounters.SetController(_alice);
        withCounters.SetZone(ZoneType.Battlefield);
        withCounters.Counters.Add(CounterType.PlusOnePlusOne, 2);
        _alice.Zones.Battlefield.AddCard(withCounters);

        // Alice's creature with no counters → stays at 0.
        var noCounters = new Creature("Plain Bear", "1G", 2, 2);
        noCounters.SetOwner(_alice);
        noCounters.SetController(_alice);
        noCounters.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(noCounters);

        // Bob's creature with counters → must NOT be doubled ("you control").
        var bobCreature = new Creature("Bob Bear", "1G", 2, 2);
        bobCreature.SetOwner(_bob);
        bobCreature.SetController(_bob);
        bobCreature.SetZone(ZoneType.Battlefield);
        bobCreature.Counters.Add(CounterType.PlusOnePlusOne, 3);
        _bob.Zones.Battlefield.AddCard(bobCreature);

        var ability = bill.Abilities.OfType<ActivatedAbility>().Single();
        ability.Costs.OfType<ManaCostCost>().Single().Cost
            .Should().Be(Majik.Core.ValueObjects.ManaCost.Parse("{3}{G}{G}"));

        // Pay the cost + run the effects (mirrors other named-factory tests).
        _alice.AddManaToPool(Majik.Core.ValueObjects.ManaCost.Parse("3GG"));
        foreach (var cost in ability.Costs) cost.Pay(_alice);
        foreach (var effect in ability.Effects) effect.Execute();

        withCounters.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(4,
            "2 +1/+1 counters double to 4");
        noCounters.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(0,
            "a creature with no +1/+1 counters is unaffected by doubling");
        bobCreature.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(3,
            "an opponent's creature is not 'a creature you control' — unchanged");
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
