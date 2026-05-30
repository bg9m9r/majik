using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Combat;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="PlatedGeopedeFactory"/> (Zendikar, {1}{R}).
///
/// Plated Geopede — Creature — Insect 1/1. Oracle text (verified against
/// Scryfall):
///   "First strike
///    Landfall — Whenever a land you control enters, this creature gets
///    +2/+2 until end of turn."
///
/// Same landfall + self-pump shape as <see cref="SteppeLynxFactory"/>
/// (<see cref="Triggers.OnLandEntersUnderControl"/>, CR 603.6a;
/// <see cref="PumpUntilEndOfTurnEffect"/> +2/+2, Layer 7c CR 613.1g,
/// expiry CR 514.2) plus a First strike <see cref="KeywordAbility"/>
/// marker (CR 702.7) like <see cref="YouthfulKnightFactory"/>.
///
/// Coverage:
/// - Identity (Creature — Insect, 1/1, {1}{R}, red, owner/controller).
/// - NamedCardFactory dispatch.
/// - First strike keyword marker (CR 702.7).
/// - Landfall trigger attached, self-affecting (no targets).
/// - Controller's land ETB queues the trigger; resolving gives +2/+2.
/// - The +2/+2 expires in the cleanup step (CR 514.2).
/// - Opponent's land ETB does NOT fire (CR 603.6a — "a land you control").
/// </summary>
public class PlatedGeopedeFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void PlatedGeopede_Identity_CreatureInsect_1_1_Red1R()
    {
        var geopede = PlatedGeopedeFactory.Create(_alice);

        geopede.Name.Should().Be("Plated Geopede");
        geopede.HasType(CardType.Creature).Should().BeTrue();
        geopede.ManaCost.Should().Be("{1}{R}");
        geopede.ManaCostValue.TotalValue.Should().Be(2);
        CardColors.GetColors(geopede).Should().Contain(ManaColor.Red);
        geopede.Power.Should().Be(1);
        geopede.Toughness.Should().Be(1);
        geopede.Subtypes.Should().Contain(CardSubtype.Insect);
        geopede.Owner.Should().BeSameAs(_alice);
        geopede.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void PlatedGeopede_DispatchesViaNamedCardFactory()
    {
        var dispatched = NamedCardFactory.Create("Plated Geopede", _alice);

        dispatched.Should().BeOfType<Creature>();
        dispatched.Name.Should().Be("Plated Geopede");
        dispatched.HasType(CardType.Creature).Should().BeTrue();
    }

    [Fact]
    public void PlatedGeopede_HasFirstStrikeMarker()
    {
        var geopede = PlatedGeopedeFactory.Create(_alice);

        // CR 702.7 — First strike keyword marker.
        CombatAbilities.HasFirstStrike(geopede).Should().BeTrue();
    }

    [Fact]
    public void PlatedGeopede_LandfallTrigger_IsSelfAffecting_NoTargets()
    {
        var geopede = PlatedGeopedeFactory.Create(_alice);

        var trigger = geopede.Abilities.OfType<TriggeredAbility>().Should().ContainSingle().Subject;
        trigger.Source.Should().BeSameAs(geopede);
        trigger.Controller.Should().BeSameAs(_alice);
        trigger.ActiveZones.Should().Contain(ZoneType.Battlefield);
        trigger.TargetRequests.Should().BeEmpty(
            "landfall pump affects the Geopede itself — no target is chosen");
    }

    // -----------------------------------------------------------------------
    // Landfall — fires on controller's land ETB, pumps the Geopede +2/+2
    // -----------------------------------------------------------------------

    [Fact]
    public void PlatedGeopede_OwnersLandEnters_QueuesTrigger_PumpsPlusTwoPlusTwo()
    {
        var (zones, stack, triggers) = BuildEngine();

        var geopede = PlatedGeopedeFactory.Create(_alice, triggers);
        geopede.ActiveEffects = new ContinuousEffectsService();
        _alice.Zones.Battlefield.AddCard(geopede);
        geopede.SetZone(ZoneType.Battlefield);
        triggers.BindCard(geopede);

        var plains = new Land("Plains");
        plains.SetOwner(_alice);
        plains.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(plains);

        zones.MoveCardTo(plains, ZoneType.Battlefield, controller: _alice);

        triggers.PendingCount.Should().Be(1,
            "landfall trigger must queue when a land enters under controller's control");

        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        geopede.GetPower().Should().Be(PlatedGeopedeFactory.Power + PlatedGeopedeFactory.PumpAmount);
        geopede.GetToughness().Should().Be(PlatedGeopedeFactory.Toughness + PlatedGeopedeFactory.PumpAmount);
    }

    [Fact]
    public void PlatedGeopede_Pump_ExpiresAtEndOfTurn()
    {
        var (zones, stack, triggers) = BuildEngine();

        var geopede = PlatedGeopedeFactory.Create(_alice, triggers);
        var svc = new ContinuousEffectsService();
        geopede.ActiveEffects = svc;
        _alice.Zones.Battlefield.AddCard(geopede);
        geopede.SetZone(ZoneType.Battlefield);
        triggers.BindCard(geopede);

        var plains = new Land("Plains");
        plains.SetOwner(_alice);
        plains.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(plains);

        zones.MoveCardTo(plains, ZoneType.Battlefield, controller: _alice);
        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        geopede.GetPower().Should().Be(3);

        // CR 514.2 — "until end of turn" effects expire in the cleanup step.
        svc.ExpireEndOfTurn();

        geopede.GetPower().Should().Be(PlatedGeopedeFactory.Power);
        geopede.GetToughness().Should().Be(PlatedGeopedeFactory.Toughness);
    }

    [Fact]
    public void PlatedGeopede_OpponentsLandEnters_DoesNotFire()
    {
        var (zones, _, triggers) = BuildEngine();

        var geopede = PlatedGeopedeFactory.Create(_alice, triggers);
        geopede.ActiveEffects = new ContinuousEffectsService();
        _alice.Zones.Battlefield.AddCard(geopede);
        geopede.SetZone(ZoneType.Battlefield);
        triggers.BindCard(geopede);

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
