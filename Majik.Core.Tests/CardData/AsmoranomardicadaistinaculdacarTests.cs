using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Tests for <see cref="AsmoranomardicadaistinaculdacarFactory"/> — the REAL
/// Modern Horizons 2 card (verified against the embedded seed):
///   Legendary Creature — Human Wizard 3/3, mana cost {0} (empty).
///   "As long as you've discarded a card this turn, you may pay {B/R} to
///    cast this spell.
///    When ~ enters, you may search your library for a card named The
///    Underworld Cookbook, reveal it, put it into your hand, then shuffle.
///    Sacrifice two Foods: Target creature deals 6 damage to itself."
///
/// (Replaces the prior tests that asserted a FICTIONAL 4/4 Human Shaman
/// {B}{R}{G} Food-tutor.)
/// </summary>
public class AsmoranomardicadaistinaculdacarTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void Asmoran_Identity_LegendaryHumanWizard_3_3_ZeroCost()
    {
        var card = AsmoranomardicadaistinaculdacarFactory.Create(_alice);

        card.Name.Should().Be("Asmoranomardicadaistinaculdacar");
        card.ManaCost.Should().BeNullOrEmpty("the printed mana cost is empty / {0}");
        card.HasType(CardType.Creature).Should().BeTrue();
        card.HasSupertype(CardSupertype.Legendary).Should().BeTrue();
        card.HasSubtype(CardSubtype.Human).Should().BeTrue();
        card.HasSubtype(CardSubtype.Wizard).Should().BeTrue();
        card.HasSubtype(CardSubtype.Shaman).Should().BeFalse("the fictional Shaman subtype is gone");
        card.BasePower.Should().Be(3);
        card.BaseToughness.Should().Be(3);
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void SeedOracleText_MatchesRealCard_NotFictional()
    {
        var entity = new EmbeddedCardRepository().GetByName("Asmoranomardicadaistinaculdacar");
        entity.Should().NotBeNull();
        entity!.TypeLine.Should().Contain("Human Wizard");
        entity.TypeLine.Should().NotContain("Shaman");
        (entity.ManaCost ?? "").Should().BeNullOrEmpty();
        entity.Power.Should().Be("3");
        entity.Toughness.Should().Be("3");
        entity.OracleText.Should().Contain("The Underworld Cookbook");
        entity.OracleText.Should().Contain("Sacrifice two Foods");
        entity.OracleText.Should().Contain("discarded a card this turn");
    }

    [Fact]
    public void NamedCardFactory_Dispatches_Asmoran()
    {
        var card = NamedCardFactory.Create("Asmoranomardicadaistinaculdacar", _alice);

        card.Should().BeOfType<Creature>();
        card.Name.Should().Be("Asmoranomardicadaistinaculdacar");
        card.HasSupertype(CardSupertype.Legendary).Should().BeTrue();
    }

    // ----------------------------------------------------------------
    // ETB tutor: The Underworld Cookbook -> hand, then shuffle.
    // ----------------------------------------------------------------

    [Fact]
    public void Etb_TutorsTheUnderworldCookbook_ToHand()
    {
        var card = AsmoranomardicadaistinaculdacarFactory.Create(_alice);
        SeatOnBattlefield(card);

        var cookbook = new Artifact("The Underworld Cookbook", "{1}") { Owner = _alice };
        _alice.Zones.Library.AddCard(cookbook);
        cookbook.SetZone(ZoneType.Library);

        var decoy = new Artifact("Sol Ring", "{1}") { Owner = _alice };
        _alice.Zones.Library.AddCard(decoy);
        decoy.SetZone(ZoneType.Library);

        var etb = card.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var effect in etb.Effects)
            effect.Execute();

        cookbook.Zone.Should().Be(ZoneType.Hand);
        _alice.Zones.Hand.GetCards().Should().Contain(cookbook);
        _alice.Zones.Library.GetCards().Should().Contain(decoy,
            "the non-named card stays in the library");
    }

    [Fact]
    public void Etb_NoCookbookInLibrary_IsCleanNoOp()
    {
        var card = AsmoranomardicadaistinaculdacarFactory.Create(_alice);
        SeatOnBattlefield(card);

        var decoy = new Artifact("Sol Ring", "{1}") { Owner = _alice };
        _alice.Zones.Library.AddCard(decoy);
        decoy.SetZone(ZoneType.Library);

        var etb = card.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var effect in etb.Effects)
            effect.Execute();

        _alice.Zones.Hand.GetCards().Should().NotContain(decoy);
        _alice.Zones.Library.GetCards().Should().Contain(decoy);
    }

    // ----------------------------------------------------------------
    // Sacrifice two Foods: Target creature deals 6 damage to itself.
    // ----------------------------------------------------------------

    [Fact]
    public void SacFoods_Ability_HasTwoSacrificeCosts_AndTargetsACreature_WhenTwoFoods()
    {
        var card = AsmoranomardicadaistinaculdacarFactory.Create(_alice);
        SeatOnBattlefield(card);
        SeatFood(); SeatFood();

        // Re-create now that two Foods are on the battlefield so the cost
        // builder picks them up.
        card = AsmoranomardicadaistinaculdacarFactory.Create(_alice);
        SeatOnBattlefield(card);

        var ability = card.Abilities.OfType<ActivatedAbility>().Single();
        ability.Costs.OfType<AdditionalCost>()
            .Count(c => c.CostType == AdditionalCostType.Sacrifice)
            .Should().Be(2, "the cost is sacrificing two Foods");
        ability.TargetRequests.Should().ContainSingle();
        ability.TargetRequests[0].MinTargets.Should().Be(1);
        ability.TargetRequests[0].MaxTargets.Should().Be(1);
    }

    [Fact]
    public void SacFoods_TargetCreatureDeals6DamageToItself()
    {
        SeatFood(); SeatFood();
        var card = AsmoranomardicadaistinaculdacarFactory.Create(_alice);
        SeatOnBattlefield(card);

        var victim = new Creature("Grizzly Bears", "{1}{G}", 2, 2) { Owner = _alice };
        _alice.Zones.Battlefield.AddCard(victim);
        victim.SetZone(ZoneType.Battlefield);

        var ability = card.Abilities.OfType<ActivatedAbility>().Single();
        ability.SetChosenTargets(new IReadOnlyList<object>[] { new object[] { victim } });
        foreach (var effect in ability.Effects)
            effect.Execute();

        victim.Damage.Should().Be(6, "the target creature deals 6 damage to itself");
    }

    [Fact]
    public void SacFoods_CostUnpayable_WithFewerThanTwoFoods()
    {
        // Only one Food on the battlefield.
        SeatFood();
        var card = AsmoranomardicadaistinaculdacarFactory.Create(_alice);
        SeatOnBattlefield(card);

        AsmoranomardicadaistinaculdacarFactory.CanSacrificeTwoFoods(_alice)
            .Should().BeFalse();

        var ability = card.Abilities.OfType<ActivatedAbility>().Single();
        ability.Costs.OfType<AdditionalCost>()
            .Count(c => c.CostType == AdditionalCostType.Sacrifice)
            .Should().Be(0, "the activation cannot be paid with fewer than two Foods");
    }

    // ----------------------------------------------------------------
    // Alternative cast cost: {B/R} if you've discarded a card this turn.
    // ----------------------------------------------------------------

    [Fact]
    public void AltCost_AvailableOnlyAfterDiscardingThisTurn()
    {
        var turnState = new TurnState();
        var card = AsmoranomardicadaistinaculdacarFactory.Create(_alice);
        var altCost = AsmoranomardicadaistinaculdacarFactory.BuildAlternativeCost(turnState);

        altCost.CanCastFor(card, _alice).Should().BeFalse(
            "no card discarded this turn yet");

        turnState.RecordCardDiscarded(_alice);

        altCost.CanCastFor(card, _alice).Should().BeTrue(
            "a card has now been discarded this turn (CR 118.9)");
    }

    [Fact]
    public void AltCost_ManaCost_IsHybridBlackRed()
    {
        var turnState = new TurnState();
        var altCost = AsmoranomardicadaistinaculdacarFactory.BuildAlternativeCost(turnState);

        altCost.AlternativeManaCost.HybridPips.Should().ContainSingle(
            "the alternative cost is a single {B/R} hybrid pip");
        var pip = altCost.AlternativeManaCost.HybridPips[0];
        (pip.Color1 == ManaColor.Black || pip.Color2 == ManaColor.Black).Should().BeTrue();
        (pip.Color1 == ManaColor.Red || pip.Color2 == ManaColor.Red).Should().BeTrue();
    }

    private void SeatOnBattlefield(Creature card)
    {
        _alice.Zones.Battlefield.AddCard(card);
        card.SetZone(ZoneType.Battlefield);
    }

    private void SeatFood()
    {
        var food = new Artifact("Food", "", subtypes: new[] { CardSubtype.Food })
        {
            Owner = _alice,
        };
        food.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(food);
        food.SetZone(ZoneType.Battlefield);
    }
}
