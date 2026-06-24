using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Tests.Helpers;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="FanaticOfTheHarrowingFactory"/> — Fanatic of the
/// Harrowing (Outlaws of Thunder Junction, {3}{B}, Creature — Human Cleric 2/2).
///
/// Oracle text (Scryfall verified):
///   "When this creature enters, each player discards a card. If you discarded
///    a card this way, draw a card."
///
/// Covers the card's UNIQUE behaviour:
/// - Identity (name, type, P/T 2/2, Human Cleric subtypes, cost {3}{B}).
/// - ETB trigger (CR 603.1) iterates EACH player (controller included —
///   CR 109.5 / 800.4 "each player"): every player with a card in hand
///   discards one (CR 701.8).
/// - Conditional draw rider (CR 603.2 / intervening-if at resolution): if the
///   controller ("you") discarded a card this way, the controller draws a card.
/// - If the controller had an empty hand (discarded nothing this way), no draw.
/// </summary>
[Trait("Color", "B")]
public class FanaticOfTheHarrowingFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    private static Card HandCard(Player owner, string name, string cost)
    {
        var c = new Card(name, cost) { Owner = owner };
        owner.Zones.Hand.AddCard(c);
        c.SetZone(ZoneType.Hand);
        return c;
    }

    private static Card LibraryCard(Player owner, string name, string cost)
    {
        var c = new Card(name, cost) { Owner = owner };
        owner.Zones.Library.AddCard(c);
        c.SetZone(ZoneType.Library);
        return c;
    }

    private static TriggeredAbility SelectEtbTrigger(Creature fanatic) =>
        fanatic.Abilities.OfType<TriggeredAbility>()
            .Where(t => t.Condition is EventTriggerCondition<CardMovedEvent>)
            .Single(t => t.Effects.Any(e => e.Description != null
                && e.Description.Contains("each player discards")));

    [Fact]
    public void FanaticOfTheHarrowing_Identity()
    {
        var c = FanaticOfTheHarrowingFactory.Create(_alice);

        c.Name.Should().Be("Fanatic of the Harrowing");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.Power.Should().Be(2);
        c.Toughness.Should().Be(2);
        c.HasSubtype(CardSubtype.Human).Should().BeTrue();
        c.HasSubtype(CardSubtype.Cleric).Should().BeTrue();
        c.ManaCost.Should().Be("{3}{B}");
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void FanaticOfTheHarrowing_Etb_EachPlayerDiscards_AndYouDrawIfYouDiscarded()
    {
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);

        // Each player has a card in hand to discard.
        var aliceDiscard = HandCard(alice, "Alice spell", "B");
        var bobDiscard = HandCard(bob, "Bob spell", "U");

        // Controller's library has a card to draw.
        var aliceDraw = LibraryCard(alice, "Alice draw", "B");

        var fanatic = FanaticOfTheHarrowingFactory.Create(alice);
        var etb = SelectEtbTrigger(fanatic);
        ContextResolve.Resolve(etb, alice, alice, bob);

        // Each player discarded a card (CR 701.8).
        alice.Zones.Hand.GetCards().Should().NotContain(aliceDiscard);
        alice.Zones.Graveyard.GetCards().Should().Contain(aliceDiscard);
        bob.Zones.Hand.GetCards().Should().NotContain(bobDiscard);
        bob.Zones.Graveyard.GetCards().Should().Contain(bobDiscard);

        // "If you discarded a card this way, draw a card." Alice discarded →
        // Alice draws the top of her library.
        alice.Zones.Hand.GetCards().Should().Contain(aliceDraw);
        alice.Zones.Library.GetCards().Should().NotContain(aliceDraw);
    }

    [Fact]
    public void FanaticOfTheHarrowing_Etb_YouDidNotDiscard_NoDraw()
    {
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);

        // Controller has an EMPTY hand → discards nothing this way → no draw.
        var bobDiscard = HandCard(bob, "Bob spell", "U");
        var aliceDraw = LibraryCard(alice, "Alice draw", "B");

        var fanatic = FanaticOfTheHarrowingFactory.Create(alice);
        var etb = SelectEtbTrigger(fanatic);
        ContextResolve.Resolve(etb, alice, alice, bob);

        // Bob discarded; Alice could not.
        bob.Zones.Graveyard.GetCards().Should().Contain(bobDiscard);

        // Alice discarded nothing this way → NO draw (CR 603 conditional rider).
        alice.Zones.Hand.GetCards().Should().NotContain(aliceDraw);
        alice.Zones.Library.GetCards().Should().Contain(aliceDraw);
    }
}
