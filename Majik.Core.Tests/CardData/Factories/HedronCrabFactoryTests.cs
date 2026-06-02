using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="HedronCrabFactory"/> (Zendikar, {U}).
///
/// Covers:
/// - Identity (Creature — Homarid, 0/2, {U}, Blue, owner / controller).
/// - <see cref="NamedCardFactory"/> dispatch.
/// - Landfall trigger attached (CR 603.1 / 702.142) with target-player
///   TargetRequest.
/// - Land entering under controller's control queues the trigger; mills 3
///   to chosen target player (CR 701.13).
/// - Trigger does NOT fire when a land enters under the opponent's
///   control.
/// - No-target fallback mills the controller (mirrors Bojuka Bog).
/// </summary>
[Trait("Color", "U")]
public class HedronCrabFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void HedronCrab_Identity_CreatureHomarid_0_2_BlueU()
    {
        var crab = HedronCrabFactory.Create(_alice);

        crab.Name.Should().Be("Hedron Crab");
        crab.HasType(CardType.Creature).Should().BeTrue();
        crab.ManaCost.Should().Be("{U}");
        crab.ManaCostValue.TotalValue.Should().Be(1);
        CardColors.GetColors(crab).Should().Contain(ManaColor.Blue);
        crab.Power.Should().Be(0);
        crab.Toughness.Should().Be(2);
        crab.Subtypes.Should().Contain(CardSubtype.Homarid);
        crab.Owner.Should().BeSameAs(_alice);
        crab.Controller.Should().BeSameAs(_alice);
    }
    [Fact]
    public void HedronCrab_LandfallTrigger_HasTargetPlayerRequest()
    {
        var crab = HedronCrabFactory.Create(_alice);

        var trigger = crab.Abilities.OfType<TriggeredAbility>().Should().ContainSingle().Subject;
        trigger.Source.Should().BeSameAs(crab);
        trigger.Controller.Should().BeSameAs(_alice);
        trigger.ActiveZones.Should().Contain(ZoneType.Battlefield);

        trigger.TargetRequests.Should().HaveCount(1);
        trigger.TargetRequests[0].Description.Should().Be("target player");
        trigger.TargetRequests[0].MinTargets.Should().Be(1);
        trigger.TargetRequests[0].MaxTargets.Should().Be(1);
    }

    // -----------------------------------------------------------------------
    // Landfall — fires on owner's land ETB, mills 3 to chosen target
    // -----------------------------------------------------------------------

    [Fact]
    public void HedronCrab_OwnersLandEnters_QueuesTrigger_MillsThree()
    {
        var (zones, stack, triggers) = BuildEngine();

        // Seed Bob's library so we can verify exactly 3 mills.
        for (int i = 0; i < 10; i++)
        {
            var c = new Instant($"Junk{i}", "{U}");
            c.SetOwner(_bob);
            _bob.Zones.Library.AddCard(c);
            c.SetZone(ZoneType.Library);
        }

        // Hedron Crab in play under Alice's control.
        var crab = HedronCrabFactory.Create(_alice, triggers);
        _alice.Zones.Battlefield.AddCard(crab);
        crab.SetZone(ZoneType.Battlefield);
        triggers.BindCard(crab);

        // Drop a land under Alice's control.
        var island = new Land("Island");
        island.SetOwner(_alice);
        island.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(island);

        zones.MoveCardTo(island, ZoneType.Battlefield, controller: _alice);

        triggers.PendingCount.Should().Be(1,
            "landfall trigger must queue when a land enters under controller's control");

        // Caster picks Bob.
        var trigger = crab.Abilities.OfType<TriggeredAbility>().Single();
        trigger.SetChosenTargets(new[]
        {
            (IReadOnlyList<object>)new object[] { _bob },
        });

        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        _bob.Zones.Graveyard.Count.Should().Be(HedronCrabFactory.MillCount);
        _bob.Zones.Library.Count.Should().Be(7);
    }

    [Fact]
    public void HedronCrab_OpponentsLandEnters_DoesNotFire()
    {
        var (zones, _, triggers) = BuildEngine();

        var crab = HedronCrabFactory.Create(_alice, triggers);
        _alice.Zones.Battlefield.AddCard(crab);
        crab.SetZone(ZoneType.Battlefield);
        triggers.BindCard(crab);

        // Bob plays a land — should NOT fire Alice's landfall.
        var swamp = new Land("Swamp");
        swamp.SetOwner(_bob);
        swamp.SetZone(ZoneType.Hand);
        _bob.Zones.Hand.AddCard(swamp);

        zones.MoveCardTo(swamp, ZoneType.Battlefield, controller: _bob);

        triggers.PendingCount.Should().Be(0,
            "landfall only triggers on a land entering under YOUR control");
    }

    [Fact]
    public void HedronCrab_NoTargetChosen_MillsController()
    {
        var (zones, stack, triggers) = BuildEngine();

        // Alice's own library deep enough to mill 3.
        for (int i = 0; i < 5; i++)
        {
            var c = new Instant($"Junk{i}", "{U}");
            c.SetOwner(_alice);
            _alice.Zones.Library.AddCard(c);
            c.SetZone(ZoneType.Library);
        }

        var crab = HedronCrabFactory.Create(_alice, triggers);
        _alice.Zones.Battlefield.AddCard(crab);
        crab.SetZone(ZoneType.Battlefield);
        triggers.BindCard(crab);

        var island = new Land("Island");
        island.SetOwner(_alice);
        island.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(island);

        zones.MoveCardTo(island, ZoneType.Battlefield, controller: _alice);

        // No SetChosenTargets — fallback to controller.
        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        _alice.Zones.Graveyard.Count.Should().Be(HedronCrabFactory.MillCount,
            "no-target fallback mills the controller");
        _alice.Zones.Library.Count.Should().Be(2);
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
