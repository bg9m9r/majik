using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Players;
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
/// - Conditional draw-replacement static-ability marker attached.
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

    [Fact]
    public void QuantumRiddler_HasConditionalDrawReplacementStaticMarker()
    {
        var card = QuantumRiddlerFactory.Create(_alice);

        var statics = card.Abilities.OfType<StaticAbility>().ToList();
        statics.Should().ContainSingle(s => s.Description.Contains("one or fewer cards in hand"),
            "the conditional additional-draw clause ships as a StaticAbility marker (v1 gap — "
            + "CardDrawIntent not yet on the ReplacementBus)");
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
