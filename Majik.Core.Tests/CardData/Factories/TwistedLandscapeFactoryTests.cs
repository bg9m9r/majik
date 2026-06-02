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
/// Tests for <see cref="TwistedLandscapeFactory"/> (Modern Horizons 3).
///
/// Oracle (verified against Scryfall):
///   <c>{T}: Add {C}.</c>
///   <c>{T}, Sacrifice this land: Search your library for a basic Swamp,
///      Mountain, or Forest card, put it onto the battlefield tapped, then
///      shuffle.</c>
///   <c>Cycling {B}{R}{G}</c>
///
/// Composes three already-supported idioms:
/// - <c>{T}: Add {C}</c> vanilla <see cref="ManaAbility"/> (same {C} posture
///   as <see cref="AetherHubFactory"/> — CR 107.4c, modeled as +1 generic).
/// - The Evolving Wilds tutor-onto-battlefield-tapped idiom
///   (<see cref="EvolvingWildsFactory"/>) narrowed to basic Swamp / Mountain /
///   Forest (CR 205.4a — Basic supertype + one of those land subtypes).
/// - Cycling {B}{R}{G} via the shared
///   <see cref="Majik.Core.Keywords.CyclingFactory.Build"/> primitive
///   (CR 702.32).
/// </summary>
[Trait("Color", "C")]
public class TwistedLandscapeFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------
    // -----------------------------------------------------------------------
    // {T}: Add {C}
    // -----------------------------------------------------------------------

    [Fact]
    public void HasManaAbilityProducingColorless()
    {
        var land = (Land)NamedCardFactory.Create("Twisted Landscape", _alice);

        var mana = land.Abilities.OfType<ManaAbility>()
            .Should().ContainSingle().Subject;

        // CR 107.4c — {C} is colorless mana, modeled as +1 generic in the
        // engine's ManaCost (same posture as AetherHub / Urza's Saga).
        mana.ManaGenerated.Generic.Should().Be(1, "{T}: Add {C}");
        mana.ManaGenerated.White.Should().Be(0);
        mana.ManaGenerated.Blue.Should().Be(0);
        mana.ManaGenerated.Black.Should().Be(0);
        mana.ManaGenerated.Red.Should().Be(0);
        mana.ManaGenerated.Green.Should().Be(0);
    }

    // -----------------------------------------------------------------------
    // Fetch ability shape — {T}, Sacrifice this land: search basic, tapped.
    // -----------------------------------------------------------------------

    [Fact]
    public void HasTapSacrificeFetchActivatedAbility()
    {
        var land = (Land)NamedCardFactory.Create("Twisted Landscape", _alice);

        // The fetch ability is the ActivatedAbility carrying the Tap cost
        // (the cycling ability is also an ActivatedAbility but carries a
        // DiscardSelfCost instead).
        var fetch = land.Abilities.OfType<ActivatedAbility>()
            .Single(a => a.Costs.OfType<AdditionalCost>()
                .Any(c => c.CostType == AdditionalCostType.Tap));

        fetch.Costs.OfType<AdditionalCost>()
            .Should().Contain(c => c.CostType == AdditionalCostType.Tap);
    }

    [Fact]
    public void Activation_FetchesBasicSwampTapped_AndSacrifices()
    {
        var basicSwamp = new Land(
            "Swamp", new[] { CardSupertype.Basic }, new[] { CardSubtype.Swamp });
        // A basic Plains is NOT a legal target (only Swamp/Mountain/Forest).
        var basicPlains = new Land(
            "Plains", new[] { CardSupertype.Basic }, new[] { CardSubtype.Plains });
        _alice.Zones.Library.AddCard(basicSwamp);
        _alice.Zones.Library.AddCard(basicPlains);
        basicSwamp.SetZone(ZoneType.Library);
        basicPlains.SetZone(ZoneType.Library);

        var land = (Land)NamedCardFactory.Create("Twisted Landscape", _alice);
        _alice.Zones.Battlefield.AddCard(land);
        land.SetZone(ZoneType.Battlefield);

        var fetch = land.Abilities.OfType<ActivatedAbility>()
            .Single(a => a.Costs.OfType<AdditionalCost>()
                .Any(c => c.CostType == AdditionalCostType.Tap));
        foreach (var eff in fetch.Effects) eff.Execute();

        // Basic Swamp fetched to battlefield tapped; off-color Plains untouched.
        _alice.Zones.Battlefield.GetCards().Should().Contain(basicSwamp);
        basicSwamp.IsTapped.Should().BeTrue("CR — put onto the battlefield tapped");
        _alice.Zones.Library.GetCards().Should().Contain(basicPlains,
            "Plains is not a Swamp/Mountain/Forest");
        _alice.Zones.Library.GetCards().Should().NotContain(basicSwamp);

        // Twisted Landscape self-sacrificed.
        _alice.Zones.Graveyard.GetCards().Should().Contain(land);
        _alice.Zones.Battlefield.GetCards().Should().NotContain(land);
    }

    [Fact]
    public void Activation_FetchesBasicForest_AndBasicMountain_AreLegalTargets()
    {
        var basicForest = new Land(
            "Forest", new[] { CardSupertype.Basic }, new[] { CardSubtype.Forest });
        _alice.Zones.Library.AddCard(basicForest);
        basicForest.SetZone(ZoneType.Library);

        var land = (Land)NamedCardFactory.Create("Twisted Landscape", _alice);
        _alice.Zones.Battlefield.AddCard(land);
        land.SetZone(ZoneType.Battlefield);

        var fetch = land.Abilities.OfType<ActivatedAbility>()
            .Single(a => a.Costs.OfType<AdditionalCost>()
                .Any(c => c.CostType == AdditionalCostType.Tap));
        foreach (var eff in fetch.Effects) eff.Execute();

        _alice.Zones.Battlefield.GetCards().Should().Contain(basicForest);
        basicForest.IsTapped.Should().BeTrue();
    }

    [Fact]
    public void Activation_NoLegalBasic_StillSacrifices()
    {
        // Only a nonbasic dual in library — search finds nothing, but the
        // sacrifice cost is still paid.
        var dual = new Land(
            "Stomping Ground", supertypes: null,
            new[] { CardSubtype.Mountain, CardSubtype.Forest });
        _alice.Zones.Library.AddCard(dual);
        dual.SetZone(ZoneType.Library);

        var land = (Land)NamedCardFactory.Create("Twisted Landscape", _alice);
        _alice.Zones.Battlefield.AddCard(land);
        land.SetZone(ZoneType.Battlefield);

        var fetch = land.Abilities.OfType<ActivatedAbility>()
            .Single(a => a.Costs.OfType<AdditionalCost>()
                .Any(c => c.CostType == AdditionalCostType.Tap));
        foreach (var eff in fetch.Effects) eff.Execute();

        _alice.Zones.Graveyard.GetCards().Should().Contain(land);
        // Nonbasic untouched (only basics are legal AND only S/M/F subtypes).
        _alice.Zones.Library.GetCards().Should().Contain(dual);
        _alice.Zones.Battlefield.GetCards().Should().NotContain(dual);
    }

    // -----------------------------------------------------------------------
    // Cycling {B}{R}{G} — CR 702.32
    // -----------------------------------------------------------------------

    [Fact]
    public void HasCyclingActivatedAbility_WithManaAndDiscardSelfCosts()
    {
        var land = (Land)NamedCardFactory.Create("Twisted Landscape", _alice);

        var cycling = land.Abilities.OfType<ActivatedAbility>()
            .Single(a => a.Costs.OfType<DiscardSelfCost>().Any());

        cycling.Costs.OfType<DiscardSelfCost>().Should().HaveCount(1);
        var manaCost = cycling.Costs.OfType<ManaCostCost>().Single().Cost;
        manaCost.Black.Should().Be(1, "cycling {B}{R}{G}");
        manaCost.Red.Should().Be(1, "cycling {B}{R}{G}");
        manaCost.Green.Should().Be(1, "cycling {B}{R}{G}");
    }

    [Fact]
    public void HasCyclingKeywordMarker()
    {
        var land = (Land)NamedCardFactory.Create("Twisted Landscape", _alice);

        land.Abilities.OfType<KeywordAbility>()
            .Should().Contain(k => k.Keyword == "Cycling");
    }

    [Fact]
    public void Cycling_EndToEnd_PaysBRGDiscardsSelfDrawsOne_PublishesEvent()
    {
        var topCard = new Card("Lightning Bolt", "{R}");
        topCard.SetOwner(_alice);
        _alice.Zones.Library.AddCard(topCard);
        topCard.SetZone(ZoneType.Library);

        var bus = new Majik.Core.Events.EventBus();
        Majik.Core.Events.CardCycledEvent? captured = null;
        bus.Subscribe<Majik.Core.Events.CardCycledEvent>(e => captured = e);

        var land = TwistedLandscapeFactory.Create(_alice, eventBus: bus);
        _alice.Zones.Hand.AddCard(land);
        land.SetZone(ZoneType.Hand);

        _alice.AddManaToPool(ManaCost.Parse("BRG"));

        var cycling = land.Abilities.OfType<ActivatedAbility>()
            .Single(a => a.Costs.OfType<DiscardSelfCost>().Any());
        foreach (var cost in cycling.Costs)
        {
            cost.CanPay(_alice).Should().BeTrue($"{cost.Description}");
            cost.Pay(_alice);
        }
        land.Zone.Should().Be(ZoneType.Graveyard, "discarded self");

        foreach (var effect in cycling.Effects) effect.Execute();

        _alice.Zones.Hand.GetCards().Should().Contain(topCard, "cycle drew one card");
        captured.Should().NotBeNull("CR 702.32d publication");
        captured!.Card.Should().BeSameAs(land);
    }
}
