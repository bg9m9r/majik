using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="SparasHeadquartersFactory"/> — the Streets of
/// New Capenna Brokers triland (Forest Plains Island). Oracle text:
///   "({T}: Add {G}, {W}, or {U}.)
///    This land enters tapped.
///    Cycling {3} ({3}, Discard this card: Draw a card.)"
///
/// Covers:
/// - Identity (Land + all three printed land subtypes Forest/Plains/Island).
/// - Three mana abilities producing {G}, {W}, {U} respectively (CR 605.1).
/// - Cycling ability shape (ManaCostCost {3} + DiscardSelfCost via the shared
///   <see cref="Majik.Core.Keywords.CyclingFactory"/> primitive, CR 702.32).
/// - End-to-end cycling: pays {3}, discards self, draws one card, publishes
///   <see cref="Majik.Core.Events.CardCycledEvent"/>.
/// - Dispatcher routing through <see cref="NamedCardFactory"/>.
/// </summary>
[Trait("Color", "C")]
public class SparasHeadquartersFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------
    [Fact]
    public void SparasHeadquarters_HasThreeManaAbilities_ProducingGWU()
    {
        var land = (Land)NamedCardFactory.Create("Spara's Headquarters", _alice);
        var mana = land.Abilities.OfType<ManaAbility>().ToList();

        mana.Should().HaveCount(3, "triland taps for {G}, {W}, or {U}");
        mana.Should().Contain(m => m.ManaGenerated.Green == 1);
        mana.Should().Contain(m => m.ManaGenerated.White == 1);
        mana.Should().Contain(m => m.ManaGenerated.Blue == 1);
    }

    // -----------------------------------------------------------------------
    // Cycling ability shape — CR 702.32
    // -----------------------------------------------------------------------

    [Fact]
    public void SparasHeadquarters_HasCyclingActivatedAbility_WithGenericThreeAndDiscardSelf()
    {
        var land = (Land)NamedCardFactory.Create("Spara's Headquarters", _alice);
        var cycling = land.Abilities.OfType<ActivatedAbility>().Should().ContainSingle().Subject;

        cycling.Costs.Should().HaveCount(2, "cycling = {3} mana cost + DiscardSelfCost");
        cycling.Costs.OfType<DiscardSelfCost>().Should().HaveCount(1);

        var manaCost = cycling.Costs.OfType<ManaCostCost>().Single().Cost;
        manaCost.Generic.Should().Be(3, "Cycling {3} charges 3 generic mana");
        manaCost.Green.Should().Be(0);
        manaCost.White.Should().Be(0);
        manaCost.Blue.Should().Be(0);
    }

    [Fact]
    public void SparasHeadquarters_HasCyclingKeywordMarker()
    {
        var land = (Land)NamedCardFactory.Create("Spara's Headquarters", _alice);
        land.Abilities.OfType<KeywordAbility>()
            .Should().Contain(k => k.Keyword == "Cycling");
    }

    // -----------------------------------------------------------------------
    // End-to-end cycling — pays {3}, discards, draws, publishes event
    // -----------------------------------------------------------------------

    [Fact]
    public void SparasHeadquarters_Cycling_EndToEnd_PaysThreeDiscardsSelfDrawsOne()
    {
        var topCard = new Card("Llanowar Elves", "{G}");
        topCard.SetOwner(_alice);
        _alice.Zones.Library.AddCard(topCard);
        topCard.SetZone(ZoneType.Library);

        var bus = new Majik.Core.Events.EventBus();
        Majik.Core.Events.CardCycledEvent? captured = null;
        bus.Subscribe<Majik.Core.Events.CardCycledEvent>(e => captured = e);

        var triland = SparasHeadquartersFactory.Create(_alice, eventBus: bus);
        _alice.Zones.Hand.AddCard(triland);
        triland.SetZone(ZoneType.Hand);

        _alice.AddManaToPool(ManaCost.Parse("3"));

        var cycling = triland.Abilities.OfType<ActivatedAbility>().Single();
        foreach (var cost in cycling.Costs)
        {
            cost.CanPay(_alice).Should().BeTrue($"{cost.Description}");
            cost.Pay(_alice);
        }
        triland.Zone.Should().Be(ZoneType.Graveyard, "discarded self");

        foreach (var effect in cycling.Effects) effect.Execute();

        _alice.Zones.Hand.GetCards().Should().Contain(topCard, "cycle drew one card");
        captured.Should().NotBeNull("CR 702.32d publication");
        captured!.Card.Should().BeSameAs(triland);
    }
}
