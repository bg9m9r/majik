using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Counters;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="SazhsChocoboFactory"/> (Final Fantasy, {G}).
///
/// Sazh's Chocobo — Creature — Bird 0/1. Oracle text (verified against
/// Scryfall 2026-06):
///   "Landfall — Whenever a land you control enters, put a +1/+1 counter on
///    this creature."
///
/// Same landfall shape as <see cref="AkoumHellhoundFactory"/> /
/// <see cref="PlatedGeopedeFactory"/>
/// (<see cref="Triggers.OnLandEntersUnderControl"/>, CR 603.6a) but the
/// resolve effect is a PERMANENT <see cref="CounterType.PlusOnePlusOne"/>
/// counter on itself (CR 122.1 / CR 613.7d), not an "until end of turn" pump —
/// so the growth does NOT expire in the cleanup step.
///
/// Coverage:
/// - Identity (Creature — Bird, 0/1, {G}, green, owner/controller).
/// - Landfall trigger attached, self-affecting (no targets).
/// - Controller's land ETB queues the trigger; resolving adds a +1/+1 counter
///   and grows the Chocobo to 1/2.
/// - The counter is permanent (does NOT expire end of turn).
/// - A second land drop stacks a second counter (2 -> 2/3).
/// - Opponent's land ETB does NOT fire (CR 603.6a — "a land you control").
/// </summary>
[Trait("Color", "G")]
public class SazhsChocoboFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void SazhsChocobo_Identity_CreatureBird_0_1_GreenG()
    {
        var chocobo = SazhsChocoboFactory.Create(_alice);

        chocobo.Name.Should().Be("Sazh's Chocobo");
        chocobo.HasType(CardType.Creature).Should().BeTrue();
        chocobo.ManaCost.Should().Be("{G}");
        chocobo.ManaCostValue.TotalValue.Should().Be(1);
        CardColors.GetColors(chocobo).Should().Contain(ManaColor.Green);
        chocobo.Power.Should().Be(0);
        chocobo.Toughness.Should().Be(1);
        chocobo.Subtypes.Should().Contain(CardSubtype.Bird);
        chocobo.Owner.Should().BeSameAs(_alice);
        chocobo.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void SazhsChocobo_LandfallTrigger_IsSelfAffecting_NoTargets()
    {
        var chocobo = SazhsChocoboFactory.Create(_alice);

        var trigger = chocobo.Abilities.OfType<TriggeredAbility>().Should().ContainSingle().Subject;
        trigger.Source.Should().BeSameAs(chocobo);
        trigger.Controller.Should().BeSameAs(_alice);
        trigger.ActiveZones.Should().Contain(ZoneType.Battlefield);
        trigger.TargetRequests.Should().BeEmpty(
            "landfall counter goes on the Chocobo itself — no target is chosen");
    }

    // -----------------------------------------------------------------------
    // Landfall — fires on controller's land ETB, +1/+1 counter on itself
    // -----------------------------------------------------------------------

    [Fact]
    public void SazhsChocobo_OwnersLandEnters_QueuesTrigger_AddsPlusOnePlusOneCounter()
    {
        var (zones, stack, triggers) = BuildEngine();

        var chocobo = SazhsChocoboFactory.Create(_alice, triggers);
        chocobo.ActiveEffects = new ContinuousEffectsService();
        _alice.Zones.Battlefield.AddCard(chocobo);
        chocobo.SetZone(ZoneType.Battlefield);
        triggers.BindCard(chocobo);

        var forest = new Land("Forest");
        forest.SetOwner(_alice);
        forest.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(forest);

        zones.MoveCardTo(forest, ZoneType.Battlefield, controller: _alice);

        triggers.PendingCount.Should().Be(1,
            "landfall trigger must queue when a land enters under controller's control");

        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        chocobo.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(1);
        chocobo.GetPower().Should().Be(SazhsChocoboFactory.Power + 1);
        chocobo.GetToughness().Should().Be(SazhsChocoboFactory.Toughness + 1);
    }

    [Fact]
    public void SazhsChocobo_Counter_IsPermanent_DoesNotExpireEndOfTurn()
    {
        var (zones, stack, triggers) = BuildEngine();

        var chocobo = SazhsChocoboFactory.Create(_alice, triggers);
        var svc = new ContinuousEffectsService();
        chocobo.ActiveEffects = svc;
        _alice.Zones.Battlefield.AddCard(chocobo);
        chocobo.SetZone(ZoneType.Battlefield);
        triggers.BindCard(chocobo);

        var forest = new Land("Forest");
        forest.SetOwner(_alice);
        forest.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(forest);

        zones.MoveCardTo(forest, ZoneType.Battlefield, controller: _alice);
        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        chocobo.GetPower().Should().Be(1);

        // +1/+1 counters are NOT "until end of turn" effects (CR 122 / 613.7d) —
        // they persist through the cleanup step.
        svc.ExpireEndOfTurn();

        chocobo.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(1);
        chocobo.GetPower().Should().Be(1);
        chocobo.GetToughness().Should().Be(2);
    }

    [Fact]
    public void SazhsChocobo_TwoLandDrops_StackTwoCounters()
    {
        var (zones, stack, triggers) = BuildEngine();

        var chocobo = SazhsChocoboFactory.Create(_alice, triggers);
        chocobo.ActiveEffects = new ContinuousEffectsService();
        _alice.Zones.Battlefield.AddCard(chocobo);
        chocobo.SetZone(ZoneType.Battlefield);
        triggers.BindCard(chocobo);

        foreach (var name in new[] { "Forest", "Island" })
        {
            var land = new Land(name);
            land.SetOwner(_alice);
            land.SetZone(ZoneType.Hand);
            _alice.Zones.Hand.AddCard(land);

            zones.MoveCardTo(land, ZoneType.Battlefield, controller: _alice);
            triggers.PutPendingTriggersOnStack(_alice);
            stack.Pop()!.Resolve();
        }

        chocobo.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(2);
        chocobo.GetPower().Should().Be(2);
        chocobo.GetToughness().Should().Be(3);
    }

    [Fact]
    public void SazhsChocobo_OpponentsLandEnters_DoesNotFire()
    {
        var (zones, _, triggers) = BuildEngine();

        var chocobo = SazhsChocoboFactory.Create(_alice, triggers);
        chocobo.ActiveEffects = new ContinuousEffectsService();
        _alice.Zones.Battlefield.AddCard(chocobo);
        chocobo.SetZone(ZoneType.Battlefield);
        triggers.BindCard(chocobo);

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
