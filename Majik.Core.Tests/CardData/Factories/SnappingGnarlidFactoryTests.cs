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
/// Unit tests for <see cref="SnappingGnarlidFactory"/> (Zendikar, {1}{G}).
///
/// Creature — Beast 2/2. Oracle text (verified against Scryfall 2026-06):
///   "Landfall — Whenever a land you control enters, this creature gets
///    +1/+1 until end of turn."
///
/// Same landfall trigger pattern as <see cref="AkoumHellhoundFactory"/> /
/// <see cref="SteppeLynxFactory"/> — the green {1}{G} two-drop, differing
/// only in base stats (2/2) and the smaller +1/+1 pump. Covers:
/// - Identity (Creature — Beast, 2/2, {1}{G}, Green, owner / controller).
/// - Landfall trigger attached (CR 603.1 / 603.6a / CR 702.142) with no
///   targets (self-affecting pump).
/// - A land entering under the controller's control queues the trigger;
///   resolving it gives the Gnarlid +1/+1 until end of turn (CR 514.2,
///   Layer 7c CR 613.1g).
/// - The +1/+1 expires in the cleanup step (CR 514.2).
/// - Trigger does NOT fire when a land enters under the opponent's control
///   (CR 603.6a — "a land you control").
/// </summary>
[Trait("Color", "G")]
public class SnappingGnarlidFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void SnappingGnarlid_Identity_CreatureBeast_2_2_Green1G()
    {
        var gnarlid = SnappingGnarlidFactory.Create(_alice);

        gnarlid.Name.Should().Be("Snapping Gnarlid");
        gnarlid.HasType(CardType.Creature).Should().BeTrue();
        gnarlid.ManaCost.Should().Be("{1}{G}");
        gnarlid.ManaCostValue.TotalValue.Should().Be(2);
        CardColors.GetColors(gnarlid).Should().Contain(ManaColor.Green);
        gnarlid.Power.Should().Be(2);
        gnarlid.Toughness.Should().Be(2);
        gnarlid.Subtypes.Should().Contain(CardSubtype.Beast);
        gnarlid.Owner.Should().BeSameAs(_alice);
        gnarlid.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void SnappingGnarlid_LandfallTrigger_IsSelfAffecting_NoTargets()
    {
        var gnarlid = SnappingGnarlidFactory.Create(_alice);

        var trigger = gnarlid.Abilities.OfType<TriggeredAbility>().Should().ContainSingle().Subject;
        trigger.Source.Should().BeSameAs(gnarlid);
        trigger.Controller.Should().BeSameAs(_alice);
        trigger.ActiveZones.Should().Contain(ZoneType.Battlefield);
        trigger.TargetRequests.Should().BeEmpty(
            "landfall pump affects the Gnarlid itself — no target is chosen");
    }

    // -----------------------------------------------------------------------
    // Landfall — fires on controller's land ETB, pumps the Gnarlid +1/+1
    // -----------------------------------------------------------------------

    [Fact]
    public void SnappingGnarlid_OwnersLandEnters_QueuesTrigger_PumpsPlusOnePlusOne()
    {
        var (zones, stack, triggers) = BuildEngine();

        var gnarlid = SnappingGnarlidFactory.Create(_alice, triggers);
        gnarlid.ActiveEffects = new ContinuousEffectsService();
        _alice.Zones.Battlefield.AddCard(gnarlid);
        gnarlid.SetZone(ZoneType.Battlefield);
        triggers.BindCard(gnarlid);

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

        gnarlid.GetPower().Should().Be(SnappingGnarlidFactory.Power + SnappingGnarlidFactory.PumpAmount);
        gnarlid.GetToughness().Should().Be(SnappingGnarlidFactory.Toughness + SnappingGnarlidFactory.PumpAmount);
    }

    [Fact]
    public void SnappingGnarlid_Pump_ExpiresAtEndOfTurn()
    {
        var (zones, stack, triggers) = BuildEngine();

        var gnarlid = SnappingGnarlidFactory.Create(_alice, triggers);
        var svc = new ContinuousEffectsService();
        gnarlid.ActiveEffects = svc;
        _alice.Zones.Battlefield.AddCard(gnarlid);
        gnarlid.SetZone(ZoneType.Battlefield);
        triggers.BindCard(gnarlid);

        var forest = new Land("Forest");
        forest.SetOwner(_alice);
        forest.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(forest);

        zones.MoveCardTo(forest, ZoneType.Battlefield, controller: _alice);
        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        gnarlid.GetPower().Should().Be(3);

        // CR 514.2 — "until end of turn" effects expire in the cleanup step.
        svc.ExpireEndOfTurn();

        gnarlid.GetPower().Should().Be(SnappingGnarlidFactory.Power);
        gnarlid.GetToughness().Should().Be(SnappingGnarlidFactory.Toughness);
    }

    [Fact]
    public void SnappingGnarlid_OpponentsLandEnters_DoesNotFire()
    {
        var (zones, _, triggers) = BuildEngine();

        var gnarlid = SnappingGnarlidFactory.Create(_alice, triggers);
        gnarlid.ActiveEffects = new ContinuousEffectsService();
        _alice.Zones.Battlefield.AddCard(gnarlid);
        gnarlid.SetZone(ZoneType.Battlefield);
        triggers.BindCard(gnarlid);

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
