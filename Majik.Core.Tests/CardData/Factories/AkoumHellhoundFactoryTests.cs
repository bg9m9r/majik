using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="AkoumHellhoundFactory"/> (Battle for Zendikar, {R}).
///
/// Creature — Elemental Dog 0/1. Oracle text (verified against Scryfall 2026-06):
///   "Landfall — Whenever a land you control enters, this creature gets
///    +2/+2 until end of turn."
///
/// Same landfall trigger pattern as <see cref="SteppeLynxFactory"/> — the
/// red analogue of the white {W} 0/1. Covers:
/// - Identity (Creature — Elemental Dog, 0/1, {R}, Red, owner / controller).
/// - <see cref="NamedCardFactory"/> dispatch.
/// - Landfall trigger attached (CR 603.1 / 603.6a / CR 702.142) with no
///   targets (self-affecting pump).
/// - A land entering under the controller's control queues the trigger;
///   resolving it gives the Hellhound +2/+2 until end of turn (CR 514.2,
///   Layer 7c CR 613.1g).
/// - The +2/+2 expires in the cleanup step (CR 514.2).
/// - Trigger does NOT fire when a land enters under the opponent's control
///   (CR 603.6a — "a land you control").
/// </summary>
[Trait("Color", "R")]
public class AkoumHellhoundFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void AkoumHellhound_Identity_CreatureElementalDog_0_1_RedR()
    {
        var hound = AkoumHellhoundFactory.Create(_alice);

        hound.Name.Should().Be("Akoum Hellhound");
        hound.HasType(CardType.Creature).Should().BeTrue();
        hound.ManaCost.Should().Be("{R}");
        hound.ManaCostValue.TotalValue.Should().Be(1);
        CardColors.GetColors(hound).Should().Contain(ManaColor.Red);
        hound.Power.Should().Be(0);
        hound.Toughness.Should().Be(1);
        hound.Subtypes.Should().Contain(CardSubtype.Elemental);
        hound.Subtypes.Should().Contain(CardSubtype.Dog);
        hound.Owner.Should().BeSameAs(_alice);
        hound.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void AkoumHellhound_DispatchesThroughNamedCardFactory()
    {
        var card = NamedCardFactory.Create("Akoum Hellhound", _alice);

        card.Should().BeOfType<Creature>();
        card.Name.Should().Be("Akoum Hellhound");
    }

    [Fact]
    public void AkoumHellhound_LandfallTrigger_IsSelfAffecting_NoTargets()
    {
        var hound = AkoumHellhoundFactory.Create(_alice);

        var trigger = hound.Abilities.OfType<TriggeredAbility>().Should().ContainSingle().Subject;
        trigger.Source.Should().BeSameAs(hound);
        trigger.Controller.Should().BeSameAs(_alice);
        trigger.ActiveZones.Should().Contain(ZoneType.Battlefield);
        trigger.TargetRequests.Should().BeEmpty(
            "landfall pump affects the Hellhound itself — no target is chosen");
    }

    // -----------------------------------------------------------------------
    // Landfall — fires on controller's land ETB, pumps the Hellhound +2/+2
    // -----------------------------------------------------------------------

    [Fact]
    public void AkoumHellhound_OwnersLandEnters_QueuesTrigger_PumpsPlusTwoPlusTwo()
    {
        var (zones, stack, triggers) = BuildEngine();

        var hound = AkoumHellhoundFactory.Create(_alice, triggers);
        hound.ActiveEffects = new ContinuousEffectsService();
        _alice.Zones.Battlefield.AddCard(hound);
        hound.SetZone(ZoneType.Battlefield);
        triggers.BindCard(hound);

        // Drop a land under Alice's control.
        var mountain = new Land("Mountain");
        mountain.SetOwner(_alice);
        mountain.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(mountain);

        zones.MoveCardTo(mountain, ZoneType.Battlefield, controller: _alice);

        triggers.PendingCount.Should().Be(1,
            "landfall trigger must queue when a land enters under controller's control");

        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        hound.GetPower().Should().Be(AkoumHellhoundFactory.Power + AkoumHellhoundFactory.PumpAmount);
        hound.GetToughness().Should().Be(AkoumHellhoundFactory.Toughness + AkoumHellhoundFactory.PumpAmount);
    }

    [Fact]
    public void AkoumHellhound_Pump_ExpiresAtEndOfTurn()
    {
        var (zones, stack, triggers) = BuildEngine();

        var hound = AkoumHellhoundFactory.Create(_alice, triggers);
        var svc = new ContinuousEffectsService();
        hound.ActiveEffects = svc;
        _alice.Zones.Battlefield.AddCard(hound);
        hound.SetZone(ZoneType.Battlefield);
        triggers.BindCard(hound);

        var mountain = new Land("Mountain");
        mountain.SetOwner(_alice);
        mountain.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(mountain);

        zones.MoveCardTo(mountain, ZoneType.Battlefield, controller: _alice);
        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        hound.GetPower().Should().Be(2);

        // CR 514.2 — "until end of turn" effects expire in the cleanup step.
        svc.ExpireEndOfTurn();

        hound.GetPower().Should().Be(AkoumHellhoundFactory.Power);
        hound.GetToughness().Should().Be(AkoumHellhoundFactory.Toughness);
    }

    [Fact]
    public void AkoumHellhound_OpponentsLandEnters_DoesNotFire()
    {
        var (zones, _, triggers) = BuildEngine();

        var hound = AkoumHellhoundFactory.Create(_alice, triggers);
        hound.ActiveEffects = new ContinuousEffectsService();
        _alice.Zones.Battlefield.AddCard(hound);
        hound.SetZone(ZoneType.Battlefield);
        triggers.BindCard(hound);

        // Bob plays a land — should NOT fire Alice's landfall (CR 603.6a).
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
