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
/// Tests for <see cref="TheUnderworldCookbookFactory"/> (Modern Horizons 3,
/// {1}).
///
/// Distinct printing from <see cref="UnderworldCookbookFactory"/> — same
/// flavour, different oracle text (verified against Scryfall):
///   "{T}, Discard a card: Create a Food token.
///    {4}, {T}, Sacrifice this artifact: Return target creature card from
///     your graveyard to your hand."
///
/// Coverage:
///   - Identity (Artifact, {1}, owner / controller).
///   - <see cref="NamedCardFactory"/> dispatch for the printed name
///     "The Underworld Cookbook".
///   - Two activated abilities with the printed cost shapes:
///       * {T}, Discard a card: Create a Food token.
///       * {4}, {T}, Sacrifice this artifact: Return target creature card
///         from your graveyard to your hand.
///   - Food-creation activation: discards from hand + creates Food token
///     (NO draw — unlike the MH2 Underworld Cookbook).
///   - Graveyard-return activation: sacrifices this artifact + returns a
///     creature card from graveyard to hand.
///   - CanPay edge cases: empty hand blocks ability #1.
/// </summary>
public class TheUnderworldCookbookTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void TheUnderworldCookbook_Identity_Artifact_AtCost1()
    {
        var card = TheUnderworldCookbookFactory.Create(_alice);

        card.Name.Should().Be("The Underworld Cookbook");
        card.ManaCost.Should().Be("{1}");
        card.HasType(CardType.Artifact).Should().BeTrue();
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_TheUnderworldCookbook()
    {
        var card = NamedCardFactory.Create("The Underworld Cookbook", _alice);

        card.Should().BeOfType<Artifact>();
        card.Name.Should().Be("The Underworld Cookbook");
        card.HasType(CardType.Artifact).Should().BeTrue();
        card.ManaCost.Should().Be("{1}");
    }

    [Fact]
    public void TheUnderworldCookbook_HasTwoActivatedAbilities()
    {
        var card = TheUnderworldCookbookFactory.Create(_alice);

        var abilities = card.Abilities.OfType<ActivatedAbility>().ToList();
        abilities.Should().HaveCount(2,
            "the printed Food-creation activation + the printed graveyard-return activation");

        // First ability: {T}, discard a card. No mana pip.
        abilities[0].Costs.OfType<DiscardACardCost>().Should().HaveCount(1);
        abilities[0].Costs.OfType<AdditionalCost>()
            .Should().ContainSingle(c => c.CostType == AdditionalCostType.Tap);
        abilities[0].Costs.OfType<ManaCostCost>().Should().BeEmpty(
            "ability #1 has no mana pip (unlike the MH2 Underworld Cookbook's {1})");

        // Second ability: {4}, {T}, Sacrifice this artifact.
        abilities[1].Costs.OfType<ManaCostCost>().Should().HaveCount(1);
        abilities[1].Costs.OfType<AdditionalCost>()
            .Should().Contain(c => c.CostType == AdditionalCostType.Tap);
        abilities[1].Costs.OfType<AdditionalCost>()
            .Should().Contain(c => c.CostType == AdditionalCostType.Sacrifice);
    }

    [Fact]
    public void FoodCreation_Activation_DiscardsAndCreatesFood_NoDraw()
    {
        var card = TheUnderworldCookbookFactory.Create(_alice);
        SeatOnBattlefield(card);

        // Hand: one card to discard.
        var hand1 = new Instant("Filler", "{R}") { Owner = _alice };
        _alice.Zones.Hand.AddCard(hand1);
        hand1.SetZone(ZoneType.Hand);

        // Library: a card that must NOT be drawn (this printing has no draw).
        var libTop = new Instant("Top", "{R}") { Owner = _alice };
        _alice.Zones.Library.AddCard(libTop);
        libTop.SetZone(ZoneType.Library);

        var ability = card.Abilities.OfType<ActivatedAbility>().First();
        // Pay the non-mana cost (tap has no mana pool dependency); exercise
        // the discard cost + effect directly (mirrors UnderworldCookbookTests).
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

        // NO draw — the library card stays put (CR 121.1 — this printing's
        // oracle text omits the "then draw a card" of the MH2 Cookbook).
        libTop.Zone.Should().Be(ZoneType.Library);
        _alice.Zones.Hand.GetCards().Should().NotContain(libTop);
    }

    [Fact]
    public void FoodCreation_CanPay_FailsWithEmptyHand()
    {
        var card = TheUnderworldCookbookFactory.Create(_alice);
        SeatOnBattlefield(card);

        var ability = card.Abilities.OfType<ActivatedAbility>().First();
        var discardCost = ability.Costs.OfType<DiscardACardCost>().Single();

        discardCost.CanPay(_alice).Should().BeFalse(
            "the discard cost cannot be paid with an empty hand (CR 117.1)");
    }

    [Fact]
    public void GraveyardReturn_Activation_SacrificesSelfAndReturnsCreature()
    {
        var card = TheUnderworldCookbookFactory.Create(_alice);
        SeatOnBattlefield(card);

        // Seat a creature card in Alice's graveyard.
        var graveCreature = new Creature("Cat", "{1}{B}", power: 1, toughness: 1)
        {
            Owner = _alice,
        };
        _alice.Zones.Graveyard.AddCard(graveCreature);
        graveCreature.SetZone(ZoneType.Graveyard);

        var ability = card.Abilities.OfType<ActivatedAbility>().Skip(1).First();
        foreach (var effect in ability.Effects)
            effect.Execute();

        // This artifact was sacrificed (battlefield -> graveyard, CR 701.16).
        card.Zone.Should().Be(ZoneType.Graveyard);
        _alice.Zones.Graveyard.GetCards().Should().Contain(card);

        // Creature card returned to hand (deterministic first-creature fallback).
        graveCreature.Zone.Should().Be(ZoneType.Hand);
        _alice.Zones.Hand.GetCards().Should().Contain(graveCreature);
        _alice.Zones.Graveyard.GetCards().Should().NotContain(graveCreature);
    }

    [Fact]
    public void GraveyardReturn_NonCreatureInGraveyard_IsIgnoredByFallback()
    {
        var card = TheUnderworldCookbookFactory.Create(_alice);
        SeatOnBattlefield(card);

        // A non-creature card in the graveyard — should be skipped by the
        // creature-only predicate (CR 205.3).
        var graveInstant = new Instant("Bolt", "{R}") { Owner = _alice };
        _alice.Zones.Graveyard.AddCard(graveInstant);
        graveInstant.SetZone(ZoneType.Graveyard);

        var ability = card.Abilities.OfType<ActivatedAbility>().Skip(1).First();
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
