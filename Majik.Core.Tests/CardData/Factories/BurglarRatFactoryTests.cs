using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for <see cref="BurglarRatFactory"/> — Creature — Rat {1}{B} 1/1 with a
/// single ETB trigger (Scryfall verified):
///   "When this creature enters, each opponent discards a card."
///
/// Covers:
///   - Card identity (name, cost, type, subtype, P/T).
///   - ETB trigger shape (self-ETB, no target request, battlefield active zone).
///   - Resolve: each opponent discards a card (CR 701.8).
///   - Resolve: an opponent with an empty hand is a clean no-op.
///   - Resolve: the controller does NOT discard (only opponents).
///
/// NamedCardFactory dispatch + well-formedness is asserted globally by
/// CardFactoryContractTests, so no dispatch test is duplicated here.
/// </summary>
[Trait("Color", "B")]
public class BurglarRatFactoryTests
{
    private static TriggeredAbility GetEtb(Creature c) =>
        c.Abilities.OfType<TriggeredAbility>().Single();

    [Fact]
    public void BurglarRat_Identity()
    {
        var alice = new Player("Alice", 20);
        var c = BurglarRatFactory.Create(alice);

        c.Name.Should().Be("Burglar Rat");
        c.ManaCost.Should().Be("{1}{B}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Rat).Should().BeTrue();
        c.BasePower.Should().Be(1);
        c.BaseToughness.Should().Be(1);
        c.Owner.Should().BeSameAs(alice);
        c.Controller.Should().BeSameAs(alice);
    }

    [Fact]
    public void BurglarRat_Etb_HasSelfTriggerNoTargets()
    {
        var alice = new Player("Alice", 20);
        var etb = GetEtb(BurglarRatFactory.Create(alice));

        // "Each opponent discards" is not a targeted ability (CR 115.1a).
        etb.TargetRequests.Should().BeEmpty();
        etb.ActiveZones.Should().Contain(ZoneType.Battlefield);
        etb.Effects.Should().ContainSingle(e =>
            e.Description.Contains("each opponent discards"));
    }

    [Fact]
    public void BurglarRat_Etb_EachOpponentDiscardsACard()
    {
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);

        // Bob holds a card he can discard (CR 701.8).
        var bears = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        bears.SetOwner(bob);
        bob.Zones.Hand.AddCard(bears);
        bears.SetZone(ZoneType.Hand);

        var rat = BurglarRatFactory.Create(alice, triggers: null, opponentAgent: null);
        var etb = GetEtb(rat);

        Majik.Core.Tests.Helpers.ContextResolve.Resolve(etb, alice, alice, bob);

        bob.Zones.Hand.GetCards().Should().NotContain(bears,
            "CR 701.8 — each opponent discards a card");
        bob.Zones.Graveyard.GetCards().Should().Contain(bears);
    }

    [Fact]
    public void BurglarRat_Etb_EmptyHandedOpponent_NoOp()
    {
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20); // empty hand — cannot discard.

        var rat = BurglarRatFactory.Create(alice, triggers: null, opponentAgent: null);
        var etb = GetEtb(rat);

        Action act = () =>
            Majik.Core.Tests.Helpers.ContextResolve.Resolve(etb, alice, alice, bob);

        act.Should().NotThrow();
        bob.Zones.Graveyard.GetCards().Should().BeEmpty(
            "CR 701.8 — a player with no cards in hand cannot discard");
    }

    [Fact]
    public void BurglarRat_Etb_ControllerDoesNotDiscard()
    {
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);

        // Alice (the Rat's controller) holds a card; she is NOT an opponent of
        // herself, so the discard must skip her (CR 109.1 / CR 102.1).
        var aliceCard = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        aliceCard.SetOwner(alice);
        alice.Zones.Hand.AddCard(aliceCard);
        aliceCard.SetZone(ZoneType.Hand);

        var rat = BurglarRatFactory.Create(alice, triggers: null, opponentAgent: null);
        var etb = GetEtb(rat);

        Majik.Core.Tests.Helpers.ContextResolve.Resolve(etb, alice, alice, bob);

        alice.Zones.Hand.GetCards().Should().Contain(aliceCard,
            "only OPPONENTS discard — the controller keeps her cards");
        alice.Zones.Graveyard.GetCards().Should().BeEmpty();
    }
}
