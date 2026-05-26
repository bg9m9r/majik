using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Tests for <see cref="UnderworldCookbookFactory"/> (Modern Horizons 2,
/// {1}).
///
/// Coverage:
///   - Identity (Artifact, {1}, owner / controller).
///   - <see cref="NamedCardFactory"/> dispatch.
///   - Two activated abilities with the printed cost shapes:
///       * {1}, Discard a card: Create a Food, then draw a card.
///       * {2}, Sacrifice a Food: Return target creature card from your
///         graveyard to your hand.
///   - Food-creation activation: discards from hand + creates Food token
///     + draws a card.
///   - Graveyard-return activation: sacrifices a Food + returns a
///     creature card from graveyard to hand.
///   - CanPay edge cases: empty hand blocks ability #1; no Food blocks
///     ability #2.
/// </summary>
public class UnderworldCookbookTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void UnderworldCookbook_Identity_Artifact_AtCost1()
    {
        var card = UnderworldCookbookFactory.Create(_alice);

        card.Name.Should().Be("Underworld Cookbook");
        card.ManaCost.Should().Be("{1}");
        card.HasType(CardType.Artifact).Should().BeTrue();
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_UnderworldCookbook()
    {
        var card = NamedCardFactory.Create("Underworld Cookbook", _alice);

        card.Should().BeOfType<Artifact>();
        card.Name.Should().Be("Underworld Cookbook");
        card.HasType(CardType.Artifact).Should().BeTrue();
        card.ManaCost.Should().Be("{1}");
    }

    [Fact]
    public void UnderworldCookbook_HasTwoActivatedAbilities()
    {
        var card = UnderworldCookbookFactory.Create(_alice);

        var abilities = card.Abilities.OfType<ActivatedAbility>().ToList();
        abilities.Should().HaveCount(2,
            "the printed Food-creation activation + the printed graveyard-return activation");

        // First ability: {1}, discard a card.
        abilities[0].Costs.OfType<ManaCostCost>().Should().HaveCount(1);
        abilities[0].Costs.OfType<DiscardACardCost>().Should().HaveCount(1);

        // Second ability: {2}, sacrifice a Food.
        abilities[1].Costs.OfType<ManaCostCost>().Should().HaveCount(1);
        abilities[1].Costs.OfType<UnderworldCookbookFactory.SacrificeAFoodCost>()
            .Should().HaveCount(1);
    }

    [Fact]
    public void FoodCreation_Activation_DiscardsAndCreatesFoodAndDraws()
    {
        var card = UnderworldCookbookFactory.Create(_alice);
        SeatOnBattlefield(card);

        // Hand: one card to discard.
        var hand1 = new Instant("Filler", "{R}") { Owner = _alice };
        _alice.Zones.Hand.AddCard(hand1);
        hand1.SetZone(ZoneType.Hand);

        // Library: one card to draw.
        var libTop = new Instant("Top", "{R}") { Owner = _alice };
        _alice.Zones.Library.AddCard(libTop);
        libTop.SetZone(ZoneType.Library);

        var ability = card.Abilities.OfType<ActivatedAbility>().First();
        // Pay costs (skip mana — Player has no mana pool wired in this
        // shape test; ManaCostCost CanPay returns false with empty pool,
        // so we exercise the non-mana cost + the effect directly, matching
        // InsolentNeonateTests' posture).
        ability.Costs.OfType<DiscardACardCost>().Single().Pay(_alice);
        foreach (var effect in ability.Effects)
            effect.Execute();

        // Discard happened.
        _alice.Zones.Hand.GetCards().Should().NotContain(hand1);
        _alice.Zones.Graveyard.GetCards().Should().Contain(hand1);

        // Food token created.
        _alice.Zones.Battlefield.GetCards()
            .OfType<Artifact>()
            .Where(a => a.HasSubtype(CardSubtype.Food))
            .Should().HaveCount(1, "the activation creates one Food token");

        // Draw happened.
        _alice.Zones.Hand.GetCards().Should().Contain(libTop);
        libTop.Zone.Should().Be(ZoneType.Hand);
    }

    [Fact]
    public void FoodCreation_CanPay_FailsWithEmptyHand()
    {
        var card = UnderworldCookbookFactory.Create(_alice);
        SeatOnBattlefield(card);

        var ability = card.Abilities.OfType<ActivatedAbility>().First();
        var discardCost = ability.Costs.OfType<DiscardACardCost>().Single();

        discardCost.CanPay(_alice).Should().BeFalse(
            "the discard cost cannot be paid with an empty hand (CR 117.1)");
    }

    [Fact]
    public void GraveyardReturn_Activation_SacrificesFoodAndReturnsCreature()
    {
        var card = UnderworldCookbookFactory.Create(_alice);
        SeatOnBattlefield(card);

        // Seat a Food token by reusing TokenFactory via Cookbook's first
        // ability is fixture-heavy — directly mint a Food-shaped artifact
        // on the battlefield to test the sacrifice cost in isolation.
        var food = new Artifact("Food", "", subtypes: new[] { CardSubtype.Food })
        {
            Owner = _alice,
            Controller = _alice,
            IsToken = true,
        };
        _alice.Zones.Battlefield.AddCard(food);
        food.SetZone(ZoneType.Battlefield);

        // Seat a creature card in Alice's graveyard.
        var graveCreature = new Creature("Cat", "{1}{B}", power: 1, toughness: 1)
        {
            Owner = _alice,
        };
        _alice.Zones.Graveyard.AddCard(graveCreature);
        graveCreature.SetZone(ZoneType.Graveyard);

        var ability = card.Abilities.OfType<ActivatedAbility>().Skip(1).First();
        ability.Costs.OfType<UnderworldCookbookFactory.SacrificeAFoodCost>()
            .Single().Pay(_alice);
        foreach (var effect in ability.Effects)
            effect.Execute();

        // Food was sacrificed.
        food.Zone.Should().Be(ZoneType.Graveyard);
        _alice.Zones.Graveyard.GetCards().Should().Contain(food);

        // Creature card returned to hand (deterministic first-creature fallback).
        graveCreature.Zone.Should().Be(ZoneType.Hand);
        _alice.Zones.Hand.GetCards().Should().Contain(graveCreature);
        _alice.Zones.Graveyard.GetCards().Should().NotContain(graveCreature);
    }

    [Fact]
    public void GraveyardReturn_CanPay_FailsWithoutFood()
    {
        var card = UnderworldCookbookFactory.Create(_alice);
        SeatOnBattlefield(card);
        // No Food on the battlefield.

        var ability = card.Abilities.OfType<ActivatedAbility>().Skip(1).First();
        var sacCost = ability.Costs
            .OfType<UnderworldCookbookFactory.SacrificeAFoodCost>()
            .Single();

        sacCost.CanPay(_alice).Should().BeFalse(
            "the sacrifice-a-Food cost cannot be paid without a Food on the battlefield (CR 117.1)");
    }

    [Fact]
    public void GraveyardReturn_NonCreatureInGraveyard_IsIgnoredByFallback()
    {
        var card = UnderworldCookbookFactory.Create(_alice);
        SeatOnBattlefield(card);

        var food = new Artifact("Food", "", subtypes: new[] { CardSubtype.Food })
        {
            Owner = _alice,
            Controller = _alice,
            IsToken = true,
        };
        _alice.Zones.Battlefield.AddCard(food);
        food.SetZone(ZoneType.Battlefield);

        // A non-creature card in the graveyard — should be skipped by the
        // creature-only predicate (CR 205.3).
        var graveInstant = new Instant("Bolt", "{R}") { Owner = _alice };
        _alice.Zones.Graveyard.AddCard(graveInstant);
        graveInstant.SetZone(ZoneType.Graveyard);

        var ability = card.Abilities.OfType<ActivatedAbility>().Skip(1).First();
        ability.Costs.OfType<UnderworldCookbookFactory.SacrificeAFoodCost>()
            .Single().Pay(_alice);
        foreach (var effect in ability.Effects)
            effect.Execute();

        // Non-creature stays in the graveyard — clean no-op for the
        // return effect (CR 608.2b — illegal-target at resolution).
        graveInstant.Zone.Should().Be(ZoneType.Graveyard);
    }

    private void SeatOnBattlefield(Artifact card)
    {
        _alice.Zones.Battlefield.AddCard(card);
        card.SetZone(ZoneType.Battlefield);
    }
}
