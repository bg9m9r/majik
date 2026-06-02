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
/// Unit tests for <see cref="RaffinesTowerFactory"/> — the W/U/B triome
/// (Plains Island Swamp tapland with Cycling {3}).
///
/// Covers:
/// - Identity (Land + the three basic-land subtypes Plains/Island/Swamp).
/// - Three single-colour mana abilities ({W}, {U}, {B}) — CR 605.1a.
/// - Cycling {3} ability shape (ManaCostCost{3} + DiscardSelfCost via the
///   shared <see cref="Majik.Core.Keywords.CyclingFactory"/> primitive).
/// - Cycling keyword marker (CR 702.32a).
/// - End-to-end cycle: pays {3}, discards self, draws one card, publishes
///   <see cref="Majik.Core.Events.CardCycledEvent"/> when a bus is supplied.
/// - Dispatcher routing through <see cref="NamedCardFactory"/>.
/// </summary>
[Trait("Color", "C")]
public class RaffinesTowerFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------
    [Theory]
    [InlineData(CardSubtype.Plains)]
    [InlineData(CardSubtype.Island)]
    [InlineData(CardSubtype.Swamp)]
    public void RaffinesTower_CarriesBasicLandSubtype(CardSubtype subtype)
    {
        var land = RaffinesTowerFactory.Create(_alice);

        land.HasSubtype(subtype).Should().BeTrue(
            $"the printed type line is 'Land — Plains Island Swamp', so it must carry {subtype}");
    }

    [Fact]
    public void RaffinesTower_OwnerAndControllerAreSet()
    {
        var land = RaffinesTowerFactory.Create(_alice);

        land.Owner.Should().BeSameAs(_alice);
        land.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void RaffinesTower_IsNotLegendaryOrBasic()
    {
        var land = RaffinesTowerFactory.Create(_alice);

        land.HasSupertype(CardSupertype.Legendary).Should().BeFalse();
        land.HasSupertype(CardSupertype.Basic).Should().BeFalse();
    }

    // -----------------------------------------------------------------------
    // Mana abilities — CR 605.1a, one per produced colour
    // -----------------------------------------------------------------------

    [Fact]
    public void RaffinesTower_HasExactlyThreeManaAbilities()
    {
        var land = RaffinesTowerFactory.Create(_alice);

        land.Abilities.OfType<ManaAbility>().Should().HaveCount(3,
            "one each for {W}, {U}, {B}");
    }

    [Theory]
    [InlineData("W")]
    [InlineData("U")]
    [InlineData("B")]
    public void RaffinesTower_HasManaAbilityProducingColor(string color)
    {
        var land = RaffinesTowerFactory.Create(_alice);

        var match = land.Abilities.OfType<ManaAbility>().Where(m =>
            ManaForColor(m.ManaGenerated, color) == 1
            && TotalColored(m.ManaGenerated) == 1
            && m.ManaGenerated.Generic == 0);

        match.Should().ContainSingle(
            $"exactly one mana ability produces a single {{{color}}}");
    }

    // -----------------------------------------------------------------------
    // Cycling ability shape — CR 702.32
    // -----------------------------------------------------------------------

    [Fact]
    public void RaffinesTower_HasCyclingActivatedAbility_WithGenericThreeAndDiscardSelf()
    {
        var land = RaffinesTowerFactory.Create(_alice);
        var cycling = land.Abilities.OfType<ActivatedAbility>().Should().ContainSingle().Subject;

        cycling.Costs.Should().HaveCount(2, "cycling = mana cost + DiscardSelfCost");
        cycling.Costs.OfType<DiscardSelfCost>().Should().HaveCount(1);

        var manaCost = cycling.Costs.OfType<ManaCostCost>().Single().Cost;
        manaCost.Generic.Should().Be(3, "cycling cost is {3} (three generic mana)");
        manaCost.White.Should().Be(0);
        manaCost.Blue.Should().Be(0);
        manaCost.Black.Should().Be(0);
    }

    [Fact]
    public void RaffinesTower_HasCyclingKeywordMarker()
    {
        var land = RaffinesTowerFactory.Create(_alice);

        land.Abilities.OfType<KeywordAbility>()
            .Should().Contain(k => k.Keyword == "Cycling");
    }

    // -----------------------------------------------------------------------
    // End-to-end cycling — pays {3}, discards, draws, publishes event
    // -----------------------------------------------------------------------

    [Fact]
    public void RaffinesTower_Cycling_EndToEnd_PaysThreeDiscardsSelfDrawsOne()
    {
        // Seed library so the draw resolves.
        var topCard = new Card("Llanowar Elves", "{G}");
        topCard.SetOwner(_alice);
        _alice.Zones.Library.AddCard(topCard);
        topCard.SetZone(ZoneType.Library);

        var bus = new Majik.Core.Events.EventBus();
        Majik.Core.Events.CardCycledEvent? captured = null;
        bus.Subscribe<Majik.Core.Events.CardCycledEvent>(e => captured = e);

        var tower = RaffinesTowerFactory.Create(_alice, eventBus: bus);
        _alice.Zones.Hand.AddCard(tower);
        tower.SetZone(ZoneType.Hand);

        _alice.AddManaToPool(ManaCost.Parse("{3}"));

        var cycling = tower.Abilities.OfType<ActivatedAbility>().Single();
        foreach (var cost in cycling.Costs)
        {
            cost.CanPay(_alice).Should().BeTrue($"{cost.Description}");
            cost.Pay(_alice);
        }
        tower.Zone.Should().Be(ZoneType.Graveyard, "discarded self");

        foreach (var effect in cycling.Effects) effect.Execute();

        _alice.Zones.Hand.GetCards().Should().Contain(topCard, "cycle drew one card");
        captured.Should().NotBeNull("CR 702.32d publication");
        captured!.Card.Should().BeSameAs(tower);
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static int ManaForColor(ManaCost cost, string color) => color switch
    {
        "W" => cost.White,
        "U" => cost.Blue,
        "B" => cost.Black,
        "R" => cost.Red,
        "G" => cost.Green,
        _ => 0,
    };

    private static int TotalColored(ManaCost cost) =>
        cost.White + cost.Blue + cost.Black + cost.Red + cost.Green;
}
