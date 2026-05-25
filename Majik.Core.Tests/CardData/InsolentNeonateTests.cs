using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Combat;
using Majik.Core.Costs;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Tests for <see cref="InsolentNeonateFactory"/> (Shadows over Innistrad,
/// {R}).
///
/// Covers:
///   - Identity (Vampire Wizard 1/1, {R}, owner/controller, Menace).
///   - NamedCardFactory dispatch.
///   - One activated ability with the printed cost pair
///     (<see cref="DiscardACardCost"/> + <see cref="AdditionalCost.Sacrifice"/>).
///   - Resolution: sacrifices the Neonate + draws one card.
///   - Empty library on resolution flags the SBA loss flag.
/// </summary>
public class InsolentNeonateTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void InsolentNeonate_Identity_VampireWizard_1_1_AtCostR()
    {
        var card = InsolentNeonateFactory.Create(_alice);

        card.Name.Should().Be("Insolent Neonate");
        card.ManaCost.Should().Be("{R}");
        card.HasType(CardType.Creature).Should().BeTrue();
        card.HasSubtype(CardSubtype.Vampire).Should().BeTrue();
        card.HasSubtype(CardSubtype.Wizard).Should().BeTrue();
        card.BasePower.Should().Be(1);
        card.BaseToughness.Should().Be(1);
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void InsolentNeonate_HasMenace()
    {
        var card = InsolentNeonateFactory.Create(_alice);

        CombatAbilities.HasMenace(card).Should().BeTrue();
        card.Abilities.OfType<KeywordAbility>()
            .Should().ContainSingle(k => k.Keyword == "Menace");
    }

    [Fact]
    public void InsolentNeonate_NamedCardFactory_DispatchesShape()
    {
        var card = NamedCardFactory.Create("Insolent Neonate", _alice);

        card.Should().BeOfType<Creature>();
        card.Name.Should().Be("Insolent Neonate");
        card.HasSubtype(CardSubtype.Vampire).Should().BeTrue();
        card.HasSubtype(CardSubtype.Wizard).Should().BeTrue();
    }

    [Fact]
    public void InsolentNeonate_HasOneActivatedAbility_WithDiscardAndSacrificeCosts()
    {
        var card = InsolentNeonateFactory.Create(_alice);

        var abilities = card.Abilities.OfType<ActivatedAbility>().ToList();
        abilities.Should().HaveCount(1, "the printed discard+sacrifice → draw activation");

        var ability = abilities[0];
        ability.Costs.OfType<DiscardACardCost>().Should().HaveCount(1,
            "discard-a-card is the first printed cost");
        ability.Costs.OfType<AdditionalCost>()
            .Should().ContainSingle(c => c.CostType == AdditionalCostType.Sacrifice,
                "sacrifice-this-creature is the second printed cost");
    }

    [Fact]
    public void Activation_Sacrifices_AndDrawsACard()
    {
        var card = InsolentNeonateFactory.Create(_alice);
        SeatOnBattlefield(card);

        // Stock the hand with a discard candidate + library with a draw target.
        var discardable = new Instant("Filler", "R") { Owner = _alice };
        _alice.Zones.Hand.AddCard(discardable);
        discardable.SetZone(ZoneType.Hand);

        var libTop = new Instant("Top", "R") { Owner = _alice };
        _alice.Zones.Library.AddCard(libTop);
        libTop.SetZone(ZoneType.Library);

        var ability = card.Abilities.OfType<ActivatedAbility>().Single();

        // Pay each cost manually (the cost surface delegates the sacrifice
        // payment to the effect closure; see Caustic Caterpillar precedent).
        foreach (var cost in ability.Costs)
        {
            cost.CanPay(_alice).Should().BeTrue();
            cost.Pay(_alice);
        }

        // Now resolve the effect — sacrifices self + draws.
        foreach (var effect in ability.Effects)
        {
            effect.Execute();
        }

        // Discard happened.
        _alice.Zones.Hand.GetCards().Should().NotContain(discardable);
        _alice.Zones.Graveyard.GetCards().Should().Contain(discardable);

        // Sacrifice happened.
        card.Zone.Should().Be(ZoneType.Graveyard);
        _alice.Zones.Graveyard.GetCards().Should().Contain(card);
        _alice.Zones.Battlefield.GetCards().Should().NotContain(card);

        // Draw happened.
        _alice.Zones.Hand.GetCards().Should().Contain(libTop);
        libTop.Zone.Should().Be(ZoneType.Hand);
    }

    [Fact]
    public void Activation_EmptyLibrary_FlagsSBALoss()
    {
        var card = InsolentNeonateFactory.Create(_alice);
        SeatOnBattlefield(card);

        var discardable = new Instant("Filler", "R") { Owner = _alice };
        _alice.Zones.Hand.AddCard(discardable);
        discardable.SetZone(ZoneType.Hand);
        // Library deliberately empty.

        var ability = card.Abilities.OfType<ActivatedAbility>().Single();

        foreach (var cost in ability.Costs)
            cost.Pay(_alice);

        foreach (var effect in ability.Effects)
            effect.Execute();

        _alice.TriedToDrawFromEmptyLibrary.Should().BeTrue(
            "drawing from an empty library flags the SBA loss (CR 704.5b)");
        card.Zone.Should().Be(ZoneType.Graveyard);
    }

    [Fact]
    public void Activation_CanPay_FailsWithEmptyHand()
    {
        var card = InsolentNeonateFactory.Create(_alice);
        SeatOnBattlefield(card);
        // Hand deliberately empty.

        var ability = card.Abilities.OfType<ActivatedAbility>().Single();
        var discardCost = ability.Costs.OfType<DiscardACardCost>().Single();

        discardCost.CanPay(_alice).Should().BeFalse(
            "the discard cost cannot be paid with an empty hand (CR 117.1)");
    }

    private void SeatOnBattlefield(Creature card)
    {
        _alice.Zones.Battlefield.AddCard(card);
        card.SetZone(ZoneType.Battlefield);
    }
}
