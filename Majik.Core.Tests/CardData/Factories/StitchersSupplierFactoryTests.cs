using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for <see cref="StitchersSupplierFactory"/> — Creature — Zombie
/// {B} 1/1 with two triggers:
///   "When this creature enters or dies, mill three cards."
///
/// Covers:
/// - Card identity (name, cost, type, subtype, P/T, owner/controller).
/// - <see cref="NamedCardFactory"/> dispatch.
/// - Two TriggeredAbilities attached: one Battlefield-only (ETB), one
///   Battlefield+Graveyard (dies — matches Young Wolf / Undying's zone
///   posture).
/// - Live ETB: when Stitcher's Supplier enters via ZoneService, the
///   ETB trigger surfaces and resolves to mill 3 from its controller's
///   library.
/// - Live dies: when Stitcher's Supplier moves Battlefield → Graveyard
///   via ZoneService, the dies trigger surfaces and resolves to mill
///   3 more.
/// - Mill caps at library size — empty library after partial mill is
///   a clean no-op for the trigger itself (the loss only fires later
///   via empty-library draw-step SBA, CR 704.5b).
/// </summary>
[Trait("Color", "B")]
public class StitchersSupplierFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    private static void StackLibrary(Player owner, int count)
    {
        for (var i = 0; i < count; i++)
        {
            var c = new Creature($"Pile {i}", "{0}", 1, 1);
            c.SetOwner(owner);
            owner.Zones.Library.AddCard(c);
            c.SetZone(ZoneType.Library);
        }
    }

    // ------------------------------------------------------------------
    // Shape
    // ------------------------------------------------------------------

    [Fact]
    public void StitchersSupplier_IsZombie_1_1_AtCostB()
    {
        var s = StitchersSupplierFactory.Create(_alice);

        s.Name.Should().Be("Stitcher's Supplier");
        s.ManaCost.Should().Be("{B}");
        s.HasType(CardType.Creature).Should().BeTrue();
        s.HasSubtype(CardSubtype.Zombie).Should().BeTrue();
        s.BasePower.Should().Be(1);
        s.BaseToughness.Should().Be(1);
        s.Owner.Should().BeSameAs(_alice);
        s.Controller.Should().BeSameAs(_alice);
    }
    [Fact]
    public void StitchersSupplier_DiesTrigger_IsActiveInGraveyard()
    {
        // The dies trigger must include Graveyard in its active zones,
        // because ZoneService stamps card.Zone = Graveyard *before*
        // publishing the CardMovedEvent. Same posture as Young Wolf /
        // Undying.
        var s = StitchersSupplierFactory.Create(_alice);

        var triggers = s.Abilities.OfType<TriggeredAbility>().ToList();
        triggers.Should().HaveCount(2);

        // One trigger should be Battlefield-only (ETB); the other
        // should include Graveyard (dies).
        triggers.Any(t =>
            t.ActiveZones.Contains(ZoneType.Battlefield)
            && !t.ActiveZones.Contains(ZoneType.Graveyard))
            .Should().BeTrue("the ETB trigger is Battlefield-only");
        triggers.Any(t => t.ActiveZones.Contains(ZoneType.Graveyard))
            .Should().BeTrue("the dies trigger must include Graveyard");
    }

    // ------------------------------------------------------------------
    // Live ETB
    // ------------------------------------------------------------------

    [Fact]
    public void StitchersSupplier_EntersBattlefield_MillsThree()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);
        var zones = new ZoneService(bus);

        StackLibrary(_alice, 10);
        _alice.Zones.Library.GetCards().Should().HaveCount(10);

        var s = StitchersSupplierFactory.Create(_alice, triggers);
        s.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(s);

        zones.MoveCard(s, ZoneType.Hand, ZoneType.Battlefield, _alice);

        triggers.PendingCount.Should().Be(1,
            "the ETB trigger must queue on Stitcher's Supplier entering");
        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        _alice.Zones.Library.GetCards().Should().HaveCount(7,
            "ETB mill of 3 takes the library from 10 → 7");
        _alice.Zones.Graveyard.GetCards().Should().HaveCount(3);
    }

    // ------------------------------------------------------------------
    // Live dies
    // ------------------------------------------------------------------

    [Fact]
    public void StitchersSupplier_Dies_MillsThree()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);
        var zones = new ZoneService(bus);

        StackLibrary(_alice, 10);

        var s = StitchersSupplierFactory.Create(_alice, triggers);
        s.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(s);

        // Bypass ETB by NOT registering on entry — Stitcher's Supplier
        // is already on the battlefield here. Now kill it: Battlefield
        // → Graveyard via ZoneService.
        zones.MoveCard(s, ZoneType.Battlefield, ZoneType.Graveyard, _alice);

        triggers.PendingCount.Should().Be(1,
            "the dies trigger must queue on Battlefield → Graveyard");
        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        // Library 10 → 7 from the dies trigger; Stitcher's Supplier
        // itself was also added to the graveyard via the zone move.
        _alice.Zones.Library.GetCards().Should().HaveCount(7,
            "dies-trigger mill of 3 takes the library from 10 → 7");
        // Graveyard contains the 3 milled cards + Stitcher's Supplier.
        _alice.Zones.Graveyard.GetCards().Should().HaveCount(4);
        _alice.Zones.Graveyard.GetCards().Should().Contain(s);
    }

    // ------------------------------------------------------------------
    // Library smaller than mill count
    // ------------------------------------------------------------------

    [Fact]
    public void StitchersSupplier_LibrarySmallerThanThree_MillsRemaining()
    {
        // CR 701.13 — milling caps at remaining library size; no
        // direct loss from the mill itself (the loss only happens
        // later from an empty-library draw, CR 704.5b).
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);
        var zones = new ZoneService(bus);

        StackLibrary(_alice, 1); // only 1 card in the library

        var s = StitchersSupplierFactory.Create(_alice, triggers);
        s.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(s);

        zones.MoveCard(s, ZoneType.Hand, ZoneType.Battlefield, _alice);

        triggers.PendingCount.Should().Be(1);
        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        _alice.Zones.Library.GetCards().Should().BeEmpty(
            "milled the single remaining card; the rest is a clean no-op");
        _alice.Zones.Graveyard.GetCards().Should().HaveCount(1);
        _alice.LifeTotal.Should().Be(20,
            "no life loss from mill alone — CR 704.5b only fires on a draw");
    }
}
