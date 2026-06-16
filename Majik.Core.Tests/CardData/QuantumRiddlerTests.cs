using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Primitives;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Tests for <see cref="QuantumRiddlerFactory"/> (Edge of Eternities,
/// {3}{U}{U}). Creature — Sphinx 4/6:
///   "Flying
///    When this creature enters, draw a card.
///    As long as you have one or fewer cards in hand, if you would draw
///    one or more cards, you draw that many cards plus one instead.
///    Warp {1}{U}"
///
/// Covers:
/// - Identity (Sphinx Creature, mana cost {3}{U}{U}, P/T 4/6,
///   owner/controller).
/// - <see cref="NamedCardFactory"/> dispatcher.
/// - Flying + Warp keyword markers attached.
/// - Conditional additional-draw replacement (CR 614.12) — "draw that many
///   plus one instead" while hand &lt;= 1, riding the DrawCountIntent
///   quantity tier of the ReplacementBus; ETB/LTB lifecycle.
/// - ETB triggered ability shape (TriggeredAbility on
///   <see cref="CardMovedEvent"/> to battlefield).
/// - ETB trigger resolution: controller draws a card.
/// </summary>
public class QuantumRiddlerTests
{
    private readonly Player _alice = new("Alice", 20);

    private static Card NewCardInLibrary(Player owner, string name = "LibraryCard")
    {
        var c = new Card(name, "");
        c.SetOwner(owner);
        owner.Zones.Library.AddCard(c);
        c.SetZone(ZoneType.Library);
        return c;
    }

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void QuantumRiddler_Identity()
    {
        var card = QuantumRiddlerFactory.Create(_alice);

        card.Name.Should().Be("Quantum Riddler");
        card.ManaCost.Should().Be("{3}{U}{U}");
        card.HasType(CardType.Creature).Should().BeTrue();
        card.Power.Should().Be(4);
        card.Toughness.Should().Be(6);
        card.HasSubtype(CardSubtype.Sphinx).Should().BeTrue("Quantum Riddler is a Sphinx");
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_QuantumRiddler()
    {
        var card = NamedCardFactory.Create("Quantum Riddler", _alice);

        card.Should().BeOfType<Creature>("Quantum Riddler is a Creature instance");
        card.Name.Should().Be("Quantum Riddler");
        card.HasType(CardType.Creature).Should().BeTrue();
        card.HasSubtype(CardSubtype.Sphinx).Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // Ability shape
    // -----------------------------------------------------------------------

    [Fact]
    public void QuantumRiddler_HasFlyingAndWarpKeywordMarkers()
    {
        var card = QuantumRiddlerFactory.Create(_alice);

        var keywords = card.Abilities.OfType<KeywordAbility>().Select(k => k.Keyword).ToList();
        keywords.Should().Contain("Flying", "Flying is printed (CR 702.9)");
        keywords.Should().Contain("Warp", "Warp keyword marker attached (mechanic deferred)");
    }

    // -----------------------------------------------------------------------
    // Conditional additional-draw replacement (CR 614.12) — "As long as you
    // have one or fewer cards in hand, if you would draw one or more cards,
    // you draw that many cards plus one instead."
    // -----------------------------------------------------------------------

    private static Card AddCardToHand(Player owner, string name = "HandCard")
    {
        var c = new Card(name, "");
        c.SetOwner(owner);
        owner.Zones.Hand.AddCard(c);
        c.SetZone(ZoneType.Hand);
        return c;
    }

    [Fact]
    public void QuantumRiddler_WithEmptyHand_DrawOne_DrawsTwoInstead()
    {
        // Hand size 0 (<= 1), Quantum Riddler on battlefield: a draw-1
        // instruction yields two cards (CR 614.12 — "draw that many plus
        // one instead").
        var bus = new EventBus();
        _alice.AttachReplacementBus(new ReplacementBus());

        var riddler = QuantumRiddlerFactory.Create(_alice, bus, triggers: null);
        riddler.SetZone(ZoneType.Battlefield);
        bus.Publish(new CardMovedEvent(riddler, ZoneType.Library, ZoneType.Battlefield));

        var c1 = NewCardInLibrary(_alice, "L1");
        var c2 = NewCardInLibrary(_alice, "L2");

        var drawn = Fx.DrawCards(_alice, 1);

        drawn.Should().HaveCount(2, "hand was empty (<= 1) so draw-1 becomes draw-2 (CR 614.12)");
        _alice.Zones.Hand.GetCards().Should().BeEquivalentTo(new[] { c1, c2 });
    }

    [Fact]
    public void QuantumRiddler_WithTwoCardsInHand_DrawOne_DrawsOneOnly()
    {
        // Hand size 2 (> 1): clause inactive, draw-1 stays draw-1.
        var bus = new EventBus();
        _alice.AttachReplacementBus(new ReplacementBus());
        AddCardToHand(_alice, "H1");
        AddCardToHand(_alice, "H2");

        var riddler = QuantumRiddlerFactory.Create(_alice, bus, triggers: null);
        riddler.SetZone(ZoneType.Battlefield);
        bus.Publish(new CardMovedEvent(riddler, ZoneType.Library, ZoneType.Battlefield));

        NewCardInLibrary(_alice, "L1");
        NewCardInLibrary(_alice, "L2");

        var drawn = Fx.DrawCards(_alice, 1);

        drawn.Should().HaveCount(1, "hand has 2 cards (> 1) so the +1 clause is inactive");
    }

    [Fact]
    public void QuantumRiddler_OffBattlefield_DoesNotModifyDraw()
    {
        var bus = new EventBus();
        _alice.AttachReplacementBus(new ReplacementBus());

        var riddler = QuantumRiddlerFactory.Create(_alice, bus, triggers: null);
        // Never moves to the battlefield → replacement inactive.

        NewCardInLibrary(_alice, "L1");
        NewCardInLibrary(_alice, "L2");

        var drawn = Fx.DrawCards(_alice, 1);

        drawn.Should().HaveCount(1, "Quantum Riddler is not on the battlefield");
    }

    [Fact]
    public void QuantumRiddler_LeftBattlefield_StopsModifyingDraw()
    {
        var bus = new EventBus();
        _alice.AttachReplacementBus(new ReplacementBus());

        var riddler = QuantumRiddlerFactory.Create(_alice, bus, triggers: null);
        riddler.SetZone(ZoneType.Battlefield);
        bus.Publish(new CardMovedEvent(riddler, ZoneType.Library, ZoneType.Battlefield));

        // Now leaves the battlefield.
        riddler.SetZone(ZoneType.Graveyard);
        bus.Publish(new CardMovedEvent(riddler, ZoneType.Battlefield, ZoneType.Graveyard));

        NewCardInLibrary(_alice, "L1");
        NewCardInLibrary(_alice, "L2");

        var drawn = Fx.DrawCards(_alice, 1);

        drawn.Should().HaveCount(1, "Quantum Riddler left the battlefield → replacement unregistered");
    }

    [Fact]
    public void QuantumRiddler_HasEtbTrigger()
    {
        var card = QuantumRiddlerFactory.Create(_alice);

        card.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1,
            "the ETB draw-a-card trigger is attached");
    }

    // -----------------------------------------------------------------------
    // ETB trigger — controller draws a card on enter
    // -----------------------------------------------------------------------

    [Fact]
    public void QuantumRiddler_Etb_DrawsTopOfLibraryIntoHand()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var riddler = QuantumRiddlerFactory.Create(_alice, bus, triggers);
        riddler.SetZone(ZoneType.Battlefield);

        var top = NewCardInLibrary(_alice);

        // Simulate Quantum Riddler entering the battlefield.
        bus.Publish(new CardMovedEvent(riddler, ZoneType.Library, ZoneType.Battlefield));

        triggers.PendingCount.Should().Be(1);
        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        _alice.Zones.Hand.GetCards().Should().ContainSingle().Which.Should().BeSameAs(top);
        _alice.Zones.Library.GetCards().Should().BeEmpty();
    }
}
