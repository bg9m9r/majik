using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Unit tests for <see cref="OmnathLocusOfCreationFactory"/> (Zendikar
/// Rising, {1}{R}{G}{W}{U}). Oracle:
///   "When this creature enters, draw a card.
///    Landfall — Whenever a land enters the battlefield under your
///    control, if this is the first time this ability has resolved this
///    turn, you gain 4 life. If it's the second time, add {R}{G}{W}{U}.
///    If it's the third time, Omnath, Locus of Creation deals 4 damage
///    to each opponent and each planeswalker you don't control."
///
/// Covers:
/// - Identity (Legendary Creature — Elemental 4/4 at {1}{R}{G}{W}{U}).
/// - NamedCardFactory dispatch.
/// - ETB triggers a draw.
/// - 1st landfall → +4 life.
/// - 2nd landfall → controller's mana pool gains R,G,W,U.
/// - 3rd landfall → 4 damage to each opponent + each foreign planeswalker.
/// - 4th landfall → no further effect this turn.
/// - Counter resets on a new turn.
/// </summary>
public class OmnathLocusOfCreationTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    private static Land NewBattlefieldLand(Player controller, string name)
    {
        var land = new Land(name, new[] { CardSupertype.Basic }, new[] { CardSubtype.Mountain });
        land.SetOwner(controller);
        land.SetController(controller);
        return land;
    }

    private static Card NewCardInLibrary(Player owner, string name)
    {
        var c = new Card(name, "");
        c.SetOwner(owner);
        owner.Zones.Library.AddCard(c);
        c.SetZone(ZoneType.Library);
        return c;
    }

    private static Planeswalker NewBattlefieldPlaneswalker(Player controller, string name)
    {
        var pw = new Planeswalker(name, "3R", 4);
        pw.SetOwner(controller);
        pw.SetController(controller);
        controller.Zones.Battlefield.AddCard(pw);
        pw.SetZone(ZoneType.Battlefield);
        return pw;
    }

    // -------------------------------------------------------------------
    // Identity
    // -------------------------------------------------------------------

    [Fact]
    public void Omnath_Identity_LegendaryElemental_4_4()
    {
        var omnath = OmnathLocusOfCreationFactory.Create(_alice);

        omnath.Name.Should().Be("Omnath, Locus of Creation");
        omnath.ManaCost.Should().Be("{1}{R}{G}{W}{U}");
        omnath.HasType(CardType.Creature).Should().BeTrue();
        omnath.HasSupertype(CardSupertype.Legendary).Should().BeTrue();
        omnath.HasSubtype(CardSubtype.Elemental).Should().BeTrue();
        omnath.BasePower.Should().Be(4);
        omnath.BaseToughness.Should().Be(4);
        omnath.Owner.Should().BeSameAs(_alice);
        omnath.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void Omnath_DispatchesViaNamedCardFactory()
    {
        var c = NamedCardFactory.Create("Omnath, Locus of Creation", _alice);

        c.Should().BeOfType<Creature>();
        c.Name.Should().Be("Omnath, Locus of Creation");
        c.HasSupertype(CardSupertype.Legendary).Should().BeTrue();
        c.HasSubtype(CardSubtype.Elemental).Should().BeTrue();
    }

    // -------------------------------------------------------------------
    // ETB
    // -------------------------------------------------------------------

    [Fact]
    public void Omnath_Etb_DrawsACard()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var omnath = OmnathLocusOfCreationFactory.Create(
            _alice,
            opponentResolver: null,
            foreignPlaneswalkerResolver: null,
            eventBus: bus,
            triggers: triggers);

        var top = NewCardInLibrary(_alice, "Top");

        // Simulate Omnath entering — publish a CardMovedEvent into Battlefield.
        _alice.Zones.Battlefield.AddCard(omnath);
        omnath.SetZone(ZoneType.Battlefield);
        bus.Publish(new CardMovedEvent(omnath, ZoneType.Hand, ZoneType.Battlefield));

        // ETB pending; landfall trigger does NOT fire (Omnath is a Creature, not a Land).
        triggers.PendingCount.Should().Be(1);
        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        _alice.Zones.Hand.GetCards().Should().Contain(top);
    }

    // -------------------------------------------------------------------
    // Landfall progression
    // -------------------------------------------------------------------

    [Fact]
    public void Landfall_FirstResolution_Gains4Life()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var omnath = OmnathLocusOfCreationFactory.Create(
            _alice, opponentResolver: () => new[] { _bob },
            foreignPlaneswalkerResolver: null, eventBus: bus, triggers: triggers);
        _alice.Zones.Battlefield.AddCard(omnath);
        omnath.SetZone(ZoneType.Battlefield);

        // ETB trigger fires too — drain it without resolving so it doesn't
        // pollute later state. The landfall trigger is its own ability.
        bus.Publish(new CardMovedEvent(omnath, ZoneType.Hand, ZoneType.Battlefield));
        triggers.PutPendingTriggersOnStack(_alice);
        while (stack.Count > 0) stack.Pop();
        // Reset Alice's life after any side effects.
        _alice.LifeTotal.Should().Be(20);

        var land = NewBattlefieldLand(_alice, "Mountain1");
        _alice.Zones.Battlefield.AddCard(land);
        land.SetZone(ZoneType.Battlefield);
        bus.Publish(new CardMovedEvent(land, ZoneType.Library, ZoneType.Battlefield));

        triggers.PendingCount.Should().Be(1);
        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        _alice.LifeTotal.Should().Be(24, "1st landfall = +4 life");
    }

    [Fact]
    public void Landfall_SecondResolution_AddsRGWUToManaPool()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var omnath = OmnathLocusOfCreationFactory.Create(
            _alice, opponentResolver: () => new[] { _bob },
            foreignPlaneswalkerResolver: null, eventBus: bus, triggers: triggers);
        _alice.Zones.Battlefield.AddCard(omnath);
        omnath.SetZone(ZoneType.Battlefield);

        // 1st landfall — gains 4 life, no mana yet.
        var l1 = NewBattlefieldLand(_alice, "L1");
        _alice.Zones.Battlefield.AddCard(l1); l1.SetZone(ZoneType.Battlefield);
        bus.Publish(new CardMovedEvent(l1, ZoneType.Library, ZoneType.Battlefield));
        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();
        _alice.ManaPool.Total.Should().Be(0, "1st landfall produces life, not mana");

        // 2nd landfall — produces {R}{G}{W}{U}.
        var l2 = NewBattlefieldLand(_alice, "L2");
        _alice.Zones.Battlefield.AddCard(l2); l2.SetZone(ZoneType.Battlefield);
        bus.Publish(new CardMovedEvent(l2, ZoneType.Library, ZoneType.Battlefield));
        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        _alice.ManaPool.White.Should().Be(1);
        _alice.ManaPool.Blue.Should().Be(1);
        _alice.ManaPool.Red.Should().Be(1);
        _alice.ManaPool.Green.Should().Be(1);
        _alice.ManaPool.Black.Should().Be(0);
    }

    [Fact]
    public void Landfall_ThirdResolution_Deals4ToEachOpponentAndForeignPlaneswalker()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        // Bob controls a planeswalker that should take damage; Alice controls
        // one that should NOT (her own planeswalkers are immune to "each
        // planeswalker you don't control").
        var bobPw = NewBattlefieldPlaneswalker(_bob, "BobWalker");
        var alicePw = NewBattlefieldPlaneswalker(_alice, "AliceWalker");

        var omnath = OmnathLocusOfCreationFactory.Create(
            _alice,
            opponentResolver: () => new[] { _bob },
            foreignPlaneswalkerResolver: () => new[] { bobPw, alicePw },
            eventBus: bus,
            triggers: triggers);
        _alice.Zones.Battlefield.AddCard(omnath);
        omnath.SetZone(ZoneType.Battlefield);

        for (var i = 1; i <= 3; i++)
        {
            var l = NewBattlefieldLand(_alice, $"L{i}");
            _alice.Zones.Battlefield.AddCard(l); l.SetZone(ZoneType.Battlefield);
            bus.Publish(new CardMovedEvent(l, ZoneType.Library, ZoneType.Battlefield));
            triggers.PutPendingTriggersOnStack(_alice);
            stack.Pop()!.Resolve();
        }

        _bob.LifeTotal.Should().Be(16, "3rd landfall: Bob takes 4 damage");
        bobPw.Loyalty.Should().Be(0, "3rd landfall: Bob's planeswalker takes 4 (4→0)");
        alicePw.Loyalty.Should().Be(4, "Alice's planeswalker is not 'one you don't control'");
        _alice.LifeTotal.Should().Be(24, "1st landfall gain still applies");
    }

    [Fact]
    public void Landfall_FourthResolution_NoFurtherEffect()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var omnath = OmnathLocusOfCreationFactory.Create(
            _alice,
            opponentResolver: () => new[] { _bob },
            foreignPlaneswalkerResolver: null,
            eventBus: bus,
            triggers: triggers);
        _alice.Zones.Battlefield.AddCard(omnath);
        omnath.SetZone(ZoneType.Battlefield);

        for (var i = 1; i <= 3; i++)
        {
            var l = NewBattlefieldLand(_alice, $"L{i}");
            _alice.Zones.Battlefield.AddCard(l); l.SetZone(ZoneType.Battlefield);
            bus.Publish(new CardMovedEvent(l, ZoneType.Library, ZoneType.Battlefield));
            triggers.PutPendingTriggersOnStack(_alice);
            stack.Pop()!.Resolve();
        }

        var bobLifeAfterThird = _bob.LifeTotal;
        var alicePoolTotalAfterThird = _alice.ManaPool.Total;
        var aliceLifeAfterThird = _alice.LifeTotal;

        // 4th landfall — must NOT deal damage, add mana, or change life.
        var l4 = NewBattlefieldLand(_alice, "L4");
        _alice.Zones.Battlefield.AddCard(l4); l4.SetZone(ZoneType.Battlefield);
        bus.Publish(new CardMovedEvent(l4, ZoneType.Library, ZoneType.Battlefield));
        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        _bob.LifeTotal.Should().Be(bobLifeAfterThird, "4th landfall has no oracle effect");
        _alice.ManaPool.Total.Should().Be(alicePoolTotalAfterThird, "4th landfall has no oracle effect");
        _alice.LifeTotal.Should().Be(aliceLifeAfterThird, "4th landfall has no oracle effect");
    }

    [Fact]
    public void Landfall_CounterResetsOnNewTurn()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var omnath = OmnathLocusOfCreationFactory.Create(
            _alice, opponentResolver: () => new[] { _bob },
            foreignPlaneswalkerResolver: null, eventBus: bus, triggers: triggers);
        _alice.Zones.Battlefield.AddCard(omnath);
        omnath.SetZone(ZoneType.Battlefield);

        // Turn 1 — fire two landfall resolutions (life + mana). Then end the
        // turn before reaching the damage clause.
        for (var i = 1; i <= 2; i++)
        {
            var l = NewBattlefieldLand(_alice, $"T1L{i}");
            _alice.Zones.Battlefield.AddCard(l); l.SetZone(ZoneType.Battlefield);
            bus.Publish(new CardMovedEvent(l, ZoneType.Library, ZoneType.Battlefield));
            triggers.PutPendingTriggersOnStack(_alice);
            stack.Pop()!.Resolve();
        }

        _alice.LifeTotal.Should().Be(24);
        _alice.EmptyManaPool();

        // Turn boundary — TurnStartedEvent resets the per-turn counter.
        bus.Publish(new TurnStartedEvent(_alice, turnNumber: 2));

        // Turn 2 — first landfall should once again gain 4 life (count starts at 0).
        var t2land = NewBattlefieldLand(_alice, "T2L1");
        _alice.Zones.Battlefield.AddCard(t2land); t2land.SetZone(ZoneType.Battlefield);
        bus.Publish(new CardMovedEvent(t2land, ZoneType.Library, ZoneType.Battlefield));
        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        _alice.LifeTotal.Should().Be(28, "turn-boundary reset → 1st landfall again gains 4 life");
    }
}
