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
/// Unit tests for <see cref="ZagothTriomeFactory"/> — the Ikoria Triome
/// (Swamp Forest Island triland). Oracle text:
///   "({T}: Add {B}, {G}, or {U}.)
///    This land enters tapped.
///    Cycling {3} ({3}, Discard this card: Draw a card.)"
///
/// Covers:
/// - Identity (Land + all three printed land subtypes Swamp/Forest/Island).
/// - Three mana abilities producing {B}, {G}, {U} respectively (CR 605.1).
/// - Cycling ability shape (ManaCostCost {3} + DiscardSelfCost via the shared
///   <see cref="Majik.Core.Keywords.CyclingFactory"/> primitive, CR 702.32).
/// - End-to-end cycling: pays {3}, discards self, draws one card, publishes
///   <see cref="Majik.Core.Events.CardCycledEvent"/>.
/// - Dispatcher routing through <see cref="NamedCardFactory"/>.
/// </summary>
public class ZagothTriomeFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void ZagothTriome_Dispatch_ReturnsLandWithAllThreeSubtypes()
    {
        var card = NamedCardFactory.Create("Zagoth Triome", _alice);

        card.Should().BeAssignableTo<Land>();
        card.Name.Should().Be("Zagoth Triome");
        card.HasSubtype(CardSubtype.Swamp).Should().BeTrue();
        card.HasSubtype(CardSubtype.Forest).Should().BeTrue();
        card.HasSubtype(CardSubtype.Island).Should().BeTrue();
    }

    [Fact]
    public void ZagothTriome_HasThreeManaAbilities_ProducingBGU()
    {
        var land = (Land)NamedCardFactory.Create("Zagoth Triome", _alice);
        var mana = land.Abilities.OfType<ManaAbility>().ToList();

        mana.Should().HaveCount(3, "Triome taps for {B}, {G}, or {U}");
        mana.Should().Contain(m => m.ManaGenerated.Black == 1);
        mana.Should().Contain(m => m.ManaGenerated.Green == 1);
        mana.Should().Contain(m => m.ManaGenerated.Blue == 1);
    }

    // -----------------------------------------------------------------------
    // Cycling ability shape — CR 702.32
    // -----------------------------------------------------------------------

    [Fact]
    public void ZagothTriome_HasCyclingActivatedAbility_WithGenericThreeAndDiscardSelf()
    {
        var land = (Land)NamedCardFactory.Create("Zagoth Triome", _alice);
        var cycling = land.Abilities.OfType<ActivatedAbility>().Should().ContainSingle().Subject;

        cycling.Costs.Should().HaveCount(2, "cycling = {3} mana cost + DiscardSelfCost");
        cycling.Costs.OfType<DiscardSelfCost>().Should().HaveCount(1);

        var manaCost = cycling.Costs.OfType<ManaCostCost>().Single().Cost;
        manaCost.Generic.Should().Be(3, "Cycling {3} charges 3 generic mana");
        manaCost.Black.Should().Be(0);
        manaCost.Green.Should().Be(0);
        manaCost.Blue.Should().Be(0);
    }

    [Fact]
    public void ZagothTriome_HasCyclingKeywordMarker()
    {
        var land = (Land)NamedCardFactory.Create("Zagoth Triome", _alice);
        land.Abilities.OfType<KeywordAbility>()
            .Should().Contain(k => k.Keyword == "Cycling");
    }

    // -----------------------------------------------------------------------
    // End-to-end cycling — pays {3}, discards, draws, publishes event
    // -----------------------------------------------------------------------

    [Fact]
    public void ZagothTriome_Cycling_EndToEnd_PaysThreeDiscardsSelfDrawsOne()
    {
        var topCard = new Card("Llanowar Elves", "{G}");
        topCard.SetOwner(_alice);
        _alice.Zones.Library.AddCard(topCard);
        topCard.SetZone(ZoneType.Library);

        var bus = new Majik.Core.Events.EventBus();
        Majik.Core.Events.CardCycledEvent? captured = null;
        bus.Subscribe<Majik.Core.Events.CardCycledEvent>(e => captured = e);

        var triome = ZagothTriomeFactory.Create(_alice, eventBus: bus);
        _alice.Zones.Hand.AddCard(triome);
        triome.SetZone(ZoneType.Hand);

        _alice.AddManaToPool(ManaCost.Parse("3"));

        var cycling = triome.Abilities.OfType<ActivatedAbility>().Single();
        foreach (var cost in cycling.Costs)
        {
            cost.CanPay(_alice).Should().BeTrue($"{cost.Description}");
            cost.Pay(_alice);
        }
        triome.Zone.Should().Be(ZoneType.Graveyard, "discarded self");

        foreach (var effect in cycling.Effects) effect.Execute();

        _alice.Zones.Hand.GetCards().Should().Contain(topCard, "cycle drew one card");
        captured.Should().NotBeNull("CR 702.32d publication");
        captured!.Card.Should().BeSameAs(triome);
    }
}
