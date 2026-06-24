using System.Linq;
using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="TatyovaBenthicDruidFactory"/> (Dominaria,
/// {3}{G}{U}, Legendary Creature — Merfolk Druid 3/3).
///
/// Oracle text:
///   "Landfall — Whenever a land you control enters, you gain 1 life and
///    draw a card."
///
/// Covers ONLY the card's UNIQUE behaviour (its landfall trigger + the
/// gain-1-life-and-draw-a-card resolve) plus a single identity assert for
/// the non-vanilla stats. Dispatch + well-formedness are already asserted
/// for every implemented card by CardFactoryContractTests.
/// </summary>
[Trait("Color", "M")]
public class TatyovaBenthicDruidTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void Tatyova_Identity_LegendaryMerfolkDruid_3_3_AtCost3GU()
    {
        var c = TatyovaBenthicDruidFactory.Create(_alice);

        c.Name.Should().Be("Tatyova, Benthic Druid");
        c.ManaCost.Should().Be("{3}{G}{U}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSupertype(CardSupertype.Legendary).Should().BeTrue();
        c.HasSubtype(CardSubtype.Merfolk).Should().BeTrue();
        c.HasSubtype(CardSubtype.Druid).Should().BeTrue();
        c.Power.Should().Be(3);
        c.Toughness.Should().Be(3);
    }

    // -----------------------------------------------------------------------
    // Landfall trigger (CR 614 / CR 603.6a)
    // -----------------------------------------------------------------------

    [Fact]
    public void Tatyova_Landfall_FiresOnLandEnteringUnderControllerControl()
    {
        var c = TatyovaBenthicDruidFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(c);
        c.SetZone(ZoneType.Battlefield);
        var trigger = c.Abilities.OfType<TriggeredAbility>().Single();

        var land = new Land("Forest", supertypes: null, subtypes: new[] { CardSubtype.Forest });
        land.SetOwner(_alice);
        land.SetController(_alice);

        var moved = new CardMovedEvent(land, ZoneType.Library, ZoneType.Battlefield);
        trigger.IsTriggered(moved).Should().BeTrue(
            "CR 614 — a land entering under the controller's control fires landfall");
    }

    [Fact]
    public void Tatyova_Landfall_DoesNotFireForOpponentLand()
    {
        var bob = new Player("Bob", 20);
        var c = TatyovaBenthicDruidFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(c);
        c.SetZone(ZoneType.Battlefield);
        var trigger = c.Abilities.OfType<TriggeredAbility>().Single();

        var land = new Land("Island", supertypes: null, subtypes: new[] { CardSubtype.Island });
        land.SetOwner(bob);
        land.SetController(bob);

        var moved = new CardMovedEvent(land, ZoneType.Library, ZoneType.Battlefield);
        trigger.IsTriggered(moved).Should().BeFalse(
            "landfall is gated to lands entering under YOUR control (CR 614)");
    }

    [Fact]
    public void Tatyova_Landfall_DoesNotFireForNonLandCard()
    {
        var c = TatyovaBenthicDruidFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(c);
        c.SetZone(ZoneType.Battlefield);
        var trigger = c.Abilities.OfType<TriggeredAbility>().Single();

        var bear = new Creature("Grizzly Bears", "1G", 2, 2);
        bear.SetOwner(_alice);
        bear.SetController(_alice);

        var moved = new CardMovedEvent(bear, ZoneType.Hand, ZoneType.Battlefield);
        trigger.IsTriggered(moved).Should().BeFalse(
            "landfall only triggers off lands — a creature ETB doesn't qualify (CR 614)");
    }

    // -----------------------------------------------------------------------
    // Resolve effect — gain 1 life and draw a card
    // -----------------------------------------------------------------------

    [Fact]
    public void Tatyova_Resolve_GainsOneLifeAndDrawsOneCard()
    {
        var alice = new Player("Alice", 20);
        var c = TatyovaBenthicDruidFactory.Create(alice);
        var trigger = c.Abilities.OfType<TriggeredAbility>().Single();

        // Seed the library so the draw has a card to take (CR 120).
        var top = new Creature("Card A", "1G", 1, 1);
        top.SetOwner(alice);
        alice.Zones.Library.AddCard(top);

        var lifeBefore = alice.LifeTotal;
        var handBefore = alice.Zones.Hand.GetCards().Count();

        foreach (var effect in trigger.Effects) effect.Execute();

        alice.LifeTotal.Should().Be(lifeBefore + 1, "CR 119.3 — landfall gains 1 life");
        alice.Zones.Hand.GetCards().Count().Should().Be(handBefore + 1,
            "CR 120 — landfall draws one card");
        alice.Zones.Hand.GetCards().Should().Contain(top,
            "the drawn card is the top of the library");
    }
}
