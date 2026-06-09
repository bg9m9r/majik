using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Tests.Helpers;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="RuinCrabFactory"/> (Zendikar Rising, {U}).
///
/// Oracle text:
///   "Landfall — Whenever a land you control enters, each opponent mills
///    three cards."
///
/// Differs from <see cref="HedronCrabFactory"/> (which targets a single
/// player): Ruin Crab is untargeted and mills EACH opponent for three.
///
/// Covers:
/// - Identity (Creature — Crab, 0/3, {U}, Blue, owner / controller).
/// - <see cref="NamedCardFactory"/> dispatch.
/// - Landfall trigger attached (CR 603.1 / 603.6a / 702.142) with NO
///   target request (CR 115.1a — "each opponent" is not a target).
/// - A land entering under the controller's control queues the trigger;
///   resolving mills 3 from EACH opponent's library (CR 701.13b).
/// - The controller is never milled (CR 102.1 — "each opponent").
/// - Trigger does NOT fire when a land enters under an opponent's control.
/// </summary>
[Trait("Color", "U")]
public class RuinCrabFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void RuinCrab_Identity_CreatureCrab_0_3_BlueU()
    {
        var crab = RuinCrabFactory.Create(_alice);

        crab.Name.Should().Be("Ruin Crab");
        crab.HasType(CardType.Creature).Should().BeTrue();
        crab.ManaCost.Should().Be("{U}");
        crab.ManaCostValue.TotalValue.Should().Be(1);
        CardColors.GetColors(crab).Should().Contain(ManaColor.Blue);
        crab.Power.Should().Be(0);
        crab.Toughness.Should().Be(3);
        crab.Subtypes.Should().Contain(CardSubtype.Crab);
        crab.Owner.Should().BeSameAs(_alice);
        crab.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void RuinCrab_DispatchesThroughNamedCardFactory()
    {
        var card = NamedCardFactory.Create("Ruin Crab", _alice);
        card.Should().BeOfType<Creature>();
        card.Name.Should().Be("Ruin Crab");
    }

    [Fact]
    public void RuinCrab_LandfallTrigger_HasNoTargetRequest()
    {
        var crab = RuinCrabFactory.Create(_alice);

        var trigger = crab.Abilities.OfType<TriggeredAbility>().Should().ContainSingle().Subject;
        trigger.Source.Should().BeSameAs(crab);
        trigger.Controller.Should().BeSameAs(_alice);
        trigger.ActiveZones.Should().Contain(ZoneType.Battlefield);

        // CR 115.1a — "each opponent" is not a target; no TargetRequest.
        trigger.TargetRequests.Should().BeEmpty();
    }

    // -----------------------------------------------------------------------
    // Landfall — fires on controller's land ETB, mills 3 from each opponent
    // -----------------------------------------------------------------------

    [Fact]
    public void RuinCrab_OwnersLandEnters_QueuesTrigger_MillsEachOpponentThree()
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

        // Alice (the controller) also has a library — she must NOT be milled.
        for (int i = 0; i < 10; i++)
        {
            var c = new Instant($"AliceJunk{i}", "{U}");
            c.SetOwner(_alice);
            _alice.Zones.Library.AddCard(c);
            c.SetZone(ZoneType.Library);
        }

        var crab = RuinCrabFactory.Create(_alice, triggers);
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
            "landfall trigger must queue when a land enters under the controller's control");

        triggers.PutPendingTriggersOnStack(_alice);
        // Resolve through a live GameContext so the mill reads its opponents
        // off ctx.Game.AllPlayers (the production path).
        ContextResolve.ResolveStackTop(stack, _alice, _alice, _bob);

        // Bob (the opponent) is milled three.
        _bob.Zones.Graveyard.Count.Should().Be(RuinCrabFactory.MillCount);
        _bob.Zones.Library.Count.Should().Be(7);

        // Alice (the controller) is untouched — "each OPPONENT" (CR 102.1).
        _alice.Zones.Graveyard.Count.Should().Be(0);
        _alice.Zones.Library.Count.Should().Be(10);
    }

    [Fact]
    public void RuinCrab_OpponentsLandEnters_DoesNotFire()
    {
        var (zones, _, triggers) = BuildEngine();

        var crab = RuinCrabFactory.Create(_alice, triggers);
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
