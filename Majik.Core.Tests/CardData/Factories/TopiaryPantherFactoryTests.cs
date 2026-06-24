using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Events;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="TopiaryPantherFactory"/> (Modern Horizons 3).
///
/// Card: Topiary Panther — Creature — Plant Cat {4}{G}{G} 6/5. Oracle text
/// (verified against Scryfall):
///   "Trample
///    Basic landcycling {1}{G} ({1}{G}, Discard this card: Search your
///    library for a basic land card, reveal it, put it into your hand, then
///    shuffle.)"
///
/// Covers (UNIQUE behaviour only — CardFactoryContractTests already asserts
/// dispatch + well-formedness):
///   - Identity ({4}{G}{G} 6/5 Creature — Plant Cat) + Trample marker.
///   - Basic landcycling {1}{G} ability shape (ManaCostCost {1}{G} +
///     DiscardSelfCost, CR 702.32d) with both "Basic landcycling" + "Cycling"
///     keyword markers.
///   - End-to-end Basic landcycling: pays {1}{G}, discards self, tutors a
///     basic land to hand, skips non-basic land + non-land, publishes
///     CardCycledEvent (CR 702.32d).
/// </summary>
[Trait("Color", "G")]
public class TopiaryPantherFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Identity + Trample
    // -----------------------------------------------------------------------

    [Fact]
    public void TopiaryPanther_Identity_PlantCat65WithTrample()
    {
        var card = TopiaryPantherFactory.Create(_alice);

        card.Name.Should().Be("Topiary Panther");
        card.ManaCost.ToString().Should().Be("{4}{G}{G}");
        card.BasePower.Should().Be(6);
        card.BaseToughness.Should().Be(5);
        card.HasType(CardType.Creature).Should().BeTrue();
        card.HasSubtype(CardSubtype.Plant).Should().BeTrue();
        card.HasSubtype(CardSubtype.Cat).Should().BeTrue();
        card.Abilities.OfType<KeywordAbility>()
            .Should().Contain(k => k.Keyword == "Trample",
                "Topiary Panther has Trample (CR 702.19)");
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    // -----------------------------------------------------------------------
    // Basic landcycling {1}{G} — CR 702.32d
    // -----------------------------------------------------------------------

    [Fact]
    public void TopiaryPanther_HasBasicLandcyclingAndGenericCyclingMarkers()
    {
        var card = TopiaryPantherFactory.Create(_alice);

        card.Abilities.OfType<KeywordAbility>()
            .Should().Contain(k => k.Keyword == "Basic landcycling",
                "typed keyword marker (CR 702.32d)");
        card.Abilities.OfType<KeywordAbility>()
            .Should().Contain(k => k.Keyword == "Cycling",
                "typecycling IS Cycling — generic marker also surfaces");
    }

    [Fact]
    public void TopiaryPanther_HasBasicLandcyclingActivatedAbility_WithOneGreenAndDiscardSelf()
    {
        var card = TopiaryPantherFactory.Create(_alice);

        var cycling = card.Abilities.OfType<ActivatedAbility>().Should().ContainSingle().Subject;
        cycling.Costs.Should().HaveCount(2, "Basic landcycling = {1}{G} + DiscardSelfCost");
        cycling.Costs.OfType<DiscardSelfCost>().Should().ContainSingle();

        var manaCost = cycling.Costs.OfType<ManaCostCost>().Single().Cost;
        manaCost.Generic.Should().Be(1, "Basic landcycling cost is {1}{G}");
        manaCost.Green.Should().Be(1, "Basic landcycling cost is {1}{G}");
    }

    // -----------------------------------------------------------------------
    // End-to-end tutor — basic-land predicate + shuffle + event
    // -----------------------------------------------------------------------

    [Fact]
    public void BasicLandcycling_EndToEnd_PaysOneGreenDiscardsSelfTutorsBasicLand()
    {
        // Library: a basic Forest + a non-basic dual land + a non-land card.
        var forest = new Land(
            "Forest",
            supertypes: new[] { CardSupertype.Basic },
            subtypes: new[] { CardSubtype.Forest });
        forest.SetOwner(_alice);
        _alice.Zones.Library.AddCard(forest);
        forest.SetZone(ZoneType.Library);

        var stomping = new Land(
            "Stomping Ground",
            subtypes: new[] { CardSubtype.Mountain, CardSubtype.Forest });
        stomping.SetOwner(_alice);
        _alice.Zones.Library.AddCard(stomping);
        stomping.SetZone(ZoneType.Library);

        var noise = new Instant("Lightning Bolt", "{R}");
        noise.SetOwner(_alice);
        _alice.Zones.Library.AddCard(noise);
        noise.SetZone(ZoneType.Library);

        var bus = new EventBus();
        CardCycledEvent? captured = null;
        bus.Subscribe<CardCycledEvent>(e => captured = e);

        var card = TopiaryPantherFactory.Create(_alice, bus);
        _alice.Zones.Hand.AddCard(card);
        card.SetZone(ZoneType.Hand);

        _alice.AddManaToPool(ManaCost.Parse("1G"));

        var cycling = card.Abilities.OfType<ActivatedAbility>().Single();
        foreach (var cost in cycling.Costs)
        {
            cost.CanPay(_alice).Should().BeTrue($"{cost.Description}");
            cost.Pay(_alice);
        }
        card.Zone.Should().Be(ZoneType.Graveyard, "discarded self");

        foreach (var effect in cycling.Effects) effect.Execute();

        _alice.Zones.Hand.GetCards().Should().Contain(forest,
            "Basic landcycling tutored the basic Forest (CR 702.32d)");
        forest.Zone.Should().Be(ZoneType.Hand);
        _alice.Zones.Library.GetCards().Should().Contain(stomping,
            "non-basic dual land stays in library — predicate filter");
        _alice.Zones.Library.GetCards().Should().Contain(noise,
            "non-land card stays in library — predicate filter");
        captured.Should().NotBeNull("CR 702.32d publication");
        captured!.Card.Should().BeSameAs(card);
        captured.Player.Should().BeSameAs(_alice);
    }
}
