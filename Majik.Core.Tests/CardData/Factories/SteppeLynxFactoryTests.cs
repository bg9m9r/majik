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
/// Unit tests for <see cref="SteppeLynxFactory"/> (Zendikar, {W}).
///
/// Covers:
/// - Identity (Creature — Cat, 0/1, {W}, White, owner / controller).
/// - <see cref="NamedCardFactory"/> dispatch.
/// - Landfall trigger attached (CR 603.1 / 603.6a / CR 702.142) with no
///   targets (self-affecting pump).
/// - A land entering under the controller's control queues the trigger;
///   resolving it gives the Lynx +2/+2 until end of turn (CR 514.2,
///   Layer 7c CR 613.1g).
/// - The +2/+2 expires in the cleanup step (CR 514.2).
/// - Trigger does NOT fire when a land enters under the opponent's
///   control (CR 603.6a — "a land you control").
/// </summary>
[Trait("Color", "W")]
public class SteppeLynxFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void SteppeLynx_Identity_CreatureCat_0_1_WhiteW()
    {
        var lynx = SteppeLynxFactory.Create(_alice);

        lynx.Name.Should().Be("Steppe Lynx");
        lynx.HasType(CardType.Creature).Should().BeTrue();
        lynx.ManaCost.Should().Be("{W}");
        lynx.ManaCostValue.TotalValue.Should().Be(1);
        CardColors.GetColors(lynx).Should().Contain(ManaColor.White);
        lynx.Power.Should().Be(0);
        lynx.Toughness.Should().Be(1);
        lynx.Subtypes.Should().Contain(CardSubtype.Cat);
        lynx.Owner.Should().BeSameAs(_alice);
        lynx.Controller.Should().BeSameAs(_alice);
    }
    [Fact]
    public void SteppeLynx_LandfallTrigger_IsSelfAffecting_NoTargets()
    {
        var lynx = SteppeLynxFactory.Create(_alice);

        var trigger = lynx.Abilities.OfType<TriggeredAbility>().Should().ContainSingle().Subject;
        trigger.Source.Should().BeSameAs(lynx);
        trigger.Controller.Should().BeSameAs(_alice);
        trigger.ActiveZones.Should().Contain(ZoneType.Battlefield);
        trigger.TargetRequests.Should().BeEmpty(
            "landfall pump affects the Lynx itself — no target is chosen");
    }

    // -----------------------------------------------------------------------
    // Landfall — fires on controller's land ETB, pumps the Lynx +2/+2
    // -----------------------------------------------------------------------

    [Fact]
    public void SteppeLynx_OwnersLandEnters_QueuesTrigger_PumpsPlusTwoPlusTwo()
    {
        var (zones, stack, triggers) = BuildEngine();

        var lynx = SteppeLynxFactory.Create(_alice, triggers);
        lynx.ActiveEffects = new ContinuousEffectsService();
        _alice.Zones.Battlefield.AddCard(lynx);
        lynx.SetZone(ZoneType.Battlefield);
        triggers.BindCard(lynx);

        // Drop a land under Alice's control.
        var plains = new Land("Plains");
        plains.SetOwner(_alice);
        plains.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(plains);

        zones.MoveCardTo(plains, ZoneType.Battlefield, controller: _alice);

        triggers.PendingCount.Should().Be(1,
            "landfall trigger must queue when a land enters under controller's control");

        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        lynx.GetPower().Should().Be(SteppeLynxFactory.Power + SteppeLynxFactory.PumpAmount);
        lynx.GetToughness().Should().Be(SteppeLynxFactory.Toughness + SteppeLynxFactory.PumpAmount);
    }

    [Fact]
    public void SteppeLynx_Pump_ExpiresAtEndOfTurn()
    {
        var (zones, stack, triggers) = BuildEngine();

        var lynx = SteppeLynxFactory.Create(_alice, triggers);
        var svc = new ContinuousEffectsService();
        lynx.ActiveEffects = svc;
        _alice.Zones.Battlefield.AddCard(lynx);
        lynx.SetZone(ZoneType.Battlefield);
        triggers.BindCard(lynx);

        var plains = new Land("Plains");
        plains.SetOwner(_alice);
        plains.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(plains);

        zones.MoveCardTo(plains, ZoneType.Battlefield, controller: _alice);
        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        lynx.GetPower().Should().Be(2);

        // CR 514.2 — "until end of turn" effects expire in the cleanup step.
        svc.ExpireEndOfTurn();

        lynx.GetPower().Should().Be(SteppeLynxFactory.Power);
        lynx.GetToughness().Should().Be(SteppeLynxFactory.Toughness);
    }

    [Fact]
    public void SteppeLynx_OpponentsLandEnters_DoesNotFire()
    {
        var (zones, _, triggers) = BuildEngine();

        var lynx = SteppeLynxFactory.Create(_alice, triggers);
        lynx.ActiveEffects = new ContinuousEffectsService();
        _alice.Zones.Battlefield.AddCard(lynx);
        lynx.SetZone(ZoneType.Battlefield);
        triggers.BindCard(lynx);

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
