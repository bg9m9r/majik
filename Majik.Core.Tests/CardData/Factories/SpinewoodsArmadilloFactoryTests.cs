using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for <see cref="SpinewoodsArmadilloFactory"/> — Creature — Armadillo
/// {4}{G}{G} 7/7 (Bloomburrow). Oracle:
///   "Reach
///    Ward {3}
///    {1}{G}, Discard this card: Search your library for a basic land card or a
///    Desert card, reveal it, put it into your hand, then shuffle. You gain 3
///    life."
///
/// Covers ONLY the card's unique behaviour (keyword markers + the Channel-style
/// discard tutor / lifegain activated ability) plus a single identity assert.
/// Dispatch + well-formedness are covered for every card automatically by
/// <c>CardFactoryContractTests</c>.
/// </summary>
[Trait("Color", "G")]
public class SpinewoodsArmadilloFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    private static Land BasicForest(Player owner)
    {
        var forest = new Land("Forest",
            supertypes: new[] { CardSupertype.Basic },
            subtypes: new[] { CardSubtype.Forest });
        forest.SetOwner(owner);
        return forest;
    }

    private static Land Desert(Player owner)
    {
        // CR 205.3i — a "Desert card" is any card with the Desert subtype
        // (nonbasic land here, e.g. Ifnir Deadlands).
        var desert = new Land("Ifnir Deadlands", subtypes: new[] { CardSubtype.Desert });
        desert.SetOwner(owner);
        return desert;
    }

    // -----------------------------------------------------------------------
    // Identity (non-vanilla stats)
    // -----------------------------------------------------------------------

    [Fact]
    public void SpinewoodsArmadillo_Identity()
    {
        var c = SpinewoodsArmadilloFactory.Create(_alice);

        c.Name.Should().Be("Spinewoods Armadillo");
        c.ManaCost.Should().Be("{4}{G}{G}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Armadillo).Should().BeTrue();
        c.Power.Should().Be(7);
        c.Toughness.Should().Be(7);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    // -----------------------------------------------------------------------
    // Keyword markers (Reach + Ward) — CR 702.9 / CR 702.21
    // -----------------------------------------------------------------------

    [Fact]
    public void HasReachAndWardKeywordMarkers()
    {
        var c = SpinewoodsArmadilloFactory.Create(_alice);

        var keywords = c.Abilities.OfType<KeywordAbility>().Select(k => k.Keyword).ToList();
        keywords.Should().Contain("Reach");
        keywords.Should().Contain("Ward");
    }

    // -----------------------------------------------------------------------
    // Channel-style activated ability shape — {1}{G}, Discard this card
    // -----------------------------------------------------------------------

    [Fact]
    public void HasExactlyOneActivatedAbility_WithManaAndDiscardSelfCosts()
    {
        var c = SpinewoodsArmadilloFactory.Create(_alice);

        var ability = c.Abilities.OfType<ActivatedAbility>().Should().ContainSingle().Subject;
        ability.Costs.OfType<DiscardSelfCost>().Should().HaveCount(1,
            "the ability is activated by discarding this card from hand (CR 702.74a)");

        var mana = ability.Costs.OfType<ManaCostCost>().Single().Cost;
        mana.Generic.Should().Be(1, "the {1} component");
        mana.Green.Should().Be(1, "the {G} component");
    }

    [Fact]
    public void DiscardSelfCost_GatesActivationToHandZone()
    {
        var c = SpinewoodsArmadilloFactory.Create(_alice);
        _alice.Zones.Hand.AddCard(c);
        var cost = new DiscardSelfCost(c);

        cost.CanPay(_alice).Should().BeTrue("Channel-style abilities activate from the hand (CR 702.74a)");
    }

    // -----------------------------------------------------------------------
    // Activated-ability resolve — tutor (basic OR Desert) + gain 3 life
    // -----------------------------------------------------------------------

    [Fact]
    public void Resolve_TutorsABasicLandIntoHand_AndGains3Life()
    {
        var forest = BasicForest(_alice);
        _alice.Zones.Library.AddCard(forest);
        forest.SetZone(ZoneType.Library);

        var startLife = _alice.LifeTotal;
        var c = SpinewoodsArmadilloFactory.Create(_alice);
        var ability = c.Abilities.OfType<ActivatedAbility>().Single();

        ability.Resolve();

        _alice.Zones.Hand.GetCards().Should().Contain(forest, "a basic land qualifies for the search");
        forest.Zone.Should().Be(ZoneType.Hand);
        _alice.LifeTotal.Should().Be(startLife + 3, "You gain 3 life (CR 119.3)");
    }

    [Fact]
    public void Resolve_TutorsADesertCardIntoHand()
    {
        var desert = Desert(_alice);
        _alice.Zones.Library.AddCard(desert);
        desert.SetZone(ZoneType.Library);

        var c = SpinewoodsArmadilloFactory.Create(_alice);
        var ability = c.Abilities.OfType<ActivatedAbility>().Single();

        ability.Resolve();

        _alice.Zones.Hand.GetCards().Should().Contain(desert,
            "a Desert card (Desert subtype) qualifies for the search even though it is nonbasic");
        desert.Zone.Should().Be(ZoneType.Hand);
    }

    [Fact]
    public void Resolve_NonMatchingLand_NotTutored_StillGains3Life()
    {
        // A nonbasic, non-Desert land must NOT qualify for the search.
        var bog = new Land("Bojuka Bog");
        bog.SetOwner(_alice);
        _alice.Zones.Library.AddCard(bog);
        bog.SetZone(ZoneType.Library);

        var startLife = _alice.LifeTotal;
        var c = SpinewoodsArmadilloFactory.Create(_alice);
        var ability = c.Abilities.OfType<ActivatedAbility>().Single();

        ability.Resolve();

        _alice.Zones.Hand.GetCards().Should().NotContain(bog,
            "a nonbasic, non-Desert land does not match 'basic land card or a Desert card'");
        _alice.Zones.Library.GetCards().Should().Contain(bog);
        _alice.LifeTotal.Should().Be(startLife + 3, "the lifegain happens regardless of a successful find");
    }
}
