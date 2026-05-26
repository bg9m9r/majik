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
/// Tests for <see cref="WitchsOvenFactory"/> (Throne of Eldraine, {1}).
///
/// Coverage:
///   - Identity (Artifact, {1}, owner / controller).
///   - <see cref="NamedCardFactory"/> dispatch.
///   - One activated ability with tap + sacrifice-a-creature cost shape.
///   - Resolution with a low-toughness sacrificial creature → 1 Food.
///   - Resolution with a toughness ≥4 sacrificial creature → 2 Food.
///   - CanPay fails when no creature is in play.
/// </summary>
public class WitchsOvenTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void WitchsOven_Identity_Artifact_AtCost1()
    {
        var card = WitchsOvenFactory.Create(_alice);

        card.Name.Should().Be("Witch's Oven");
        card.ManaCost.Should().Be("{1}");
        card.HasType(CardType.Artifact).Should().BeTrue();
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_WitchsOven()
    {
        var card = NamedCardFactory.Create("Witch's Oven", _alice);

        card.Should().BeOfType<Artifact>();
        card.Name.Should().Be("Witch's Oven");
        card.HasType(CardType.Artifact).Should().BeTrue();
        card.ManaCost.Should().Be("{1}");
    }

    [Fact]
    public void WitchsOven_HasOneActivatedAbility_WithTapAndSacrificeCosts()
    {
        var card = WitchsOvenFactory.Create(_alice);

        var abilities = card.Abilities.OfType<ActivatedAbility>().ToList();
        abilities.Should().HaveCount(1, "the printed tap + sacrifice → Food activation");

        var ability = abilities[0];
        ability.Costs.OfType<AdditionalCost>()
            .Should().ContainSingle(c => c.CostType == AdditionalCostType.Tap,
                "tap is the first printed cost");
        ability.Costs.OfType<WitchsOvenFactory.SacrificeACreatureCostWithCapture>()
            .Should().HaveCount(1,
                "sacrifice-a-creature is the second printed cost");
    }

    [Fact]
    public void Activation_LowToughnessSacrifice_CreatesOneFood()
    {
        var card = WitchsOvenFactory.Create(_alice);
        SeatOnBattlefield(card);

        // A 2/2 — under the toughness ≥4 threshold.
        var bear = new Creature("Bear", "{1}{G}", power: 2, toughness: 2)
        {
            Owner = _alice,
            Controller = _alice,
        };
        _alice.Zones.Battlefield.AddCard(bear);
        bear.SetZone(ZoneType.Battlefield);

        var ability = card.Abilities.OfType<ActivatedAbility>().Single();

        foreach (var cost in ability.Costs)
        {
            cost.CanPay(_alice).Should().BeTrue();
            cost.Pay(_alice);
        }
        foreach (var effect in ability.Effects)
            effect.Execute();

        // Oven is tapped, bear is sacrificed.
        card.IsTapped.Should().BeTrue();
        bear.Zone.Should().Be(ZoneType.Graveyard);

        // One Food token enters the battlefield.
        var foods = _alice.Zones.Battlefield.GetCards()
            .OfType<Artifact>()
            .Where(a => a.HasSubtype(CardSubtype.Food))
            .ToList();
        foods.Should().HaveCount(1,
            "a 2-toughness sacrifice creates exactly one Food token");
    }

    [Fact]
    public void Activation_FourToughnessSacrifice_CreatesTwoFood()
    {
        var card = WitchsOvenFactory.Create(_alice);
        SeatOnBattlefield(card);

        // A 4/4 — at the toughness ≥4 threshold.
        var bigBear = new Creature("Big Bear", "{2}{G}{G}", power: 4, toughness: 4)
        {
            Owner = _alice,
            Controller = _alice,
        };
        _alice.Zones.Battlefield.AddCard(bigBear);
        bigBear.SetZone(ZoneType.Battlefield);

        var ability = card.Abilities.OfType<ActivatedAbility>().Single();

        foreach (var cost in ability.Costs)
            cost.Pay(_alice);
        foreach (var effect in ability.Effects)
            effect.Execute();

        bigBear.Zone.Should().Be(ZoneType.Graveyard);

        var foods = _alice.Zones.Battlefield.GetCards()
            .OfType<Artifact>()
            .Where(a => a.HasSubtype(CardSubtype.Food))
            .ToList();
        foods.Should().HaveCount(2,
            "a 4-toughness sacrifice creates two Food tokens (printed rider)");
    }

    [Fact]
    public void Activation_CanPay_FailsWithNoCreature()
    {
        var card = WitchsOvenFactory.Create(_alice);
        SeatOnBattlefield(card);
        // No creatures on the battlefield.

        var ability = card.Abilities.OfType<ActivatedAbility>().Single();
        var sacCost = ability.Costs
            .OfType<WitchsOvenFactory.SacrificeACreatureCostWithCapture>()
            .Single();

        sacCost.CanPay(_alice).Should().BeFalse(
            "the sacrifice cost cannot be paid with no creatures (CR 117.1)");
    }

    private void SeatOnBattlefield(Artifact card)
    {
        _alice.Zones.Battlefield.AddCard(card);
        card.SetZone(ZoneType.Battlefield);
    }
}
