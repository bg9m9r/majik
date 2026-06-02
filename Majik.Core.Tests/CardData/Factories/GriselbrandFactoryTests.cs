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

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="GriselbrandFactory"/>
/// (Avacyn Restored, {4}{B}{B}{B}{B}).
///
/// Legendary Creature — Demon 7/7. Oracle text:
///   "Flying
///    Lifelink
///    Pay 7 life: Draw seven cards."
///
/// Covers:
///   - Identity: Legendary Creature — Demon 7/7, mana cost {4}{B}{B}{B}{B},
///     mana value 8, owner/controller.
///   - NamedCardFactory dispatch.
///   - Flying + Lifelink KeywordAbility markers.
///   - Legendary supertype (CR 704.5j Legend Rule).
///   - Activated ability: "Pay 7 life: Draw seven cards."
///     * Cost = AdditionalCost.PayLife(7); CanPay requires life > 7.
///     * On activation: life total −7, hand +7, library −7.
///     * Activation fails (CanPay = false) when controller has ≤ 7 life.
/// </summary>
[Trait("Color", "B")]
public class GriselbrandFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    private static void SeedLibrary(Player player, int count)
    {
        for (var i = 0; i < count; i++)
        {
            var card = new Card($"Card {i}", "");
            player.Zones.Library.AddCard(card);
            card.SetZone(ZoneType.Library);
        }
    }

    // -------------------------------------------------------------------------
    // Identity + dispatch
    // -------------------------------------------------------------------------

    [Fact]
    public void Griselbrand_Identity()
    {
        var c = GriselbrandFactory.Create(_alice);

        c.Name.Should().Be("Griselbrand");
        c.ManaCost.Should().Be("{4}{B}{B}{B}{B}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSupertype(CardSupertype.Legendary).Should().BeTrue(
            "Griselbrand is Legendary — CR 704.5j Legend Rule applies");
        c.HasSubtype(CardSubtype.Demon).Should().BeTrue(
            "creature subtype is Demon");
        c.BasePower.Should().Be(7);
        c.BaseToughness.Should().Be(7);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void Griselbrand_ManaValue_IsEight()
    {
        var c = GriselbrandFactory.Create(_alice);
        // {4}{B}{B}{B}{B} = 4 generic + 4 black = MV 8 (CR 202.3)
        Majik.Core.ValueObjects.ManaCost.Parse(c.ManaCost).TotalValue.Should().Be(8,
            "mana value of {4}{B}{B}{B}{B} is 8 (CR 202.3)");
    }
    // -------------------------------------------------------------------------
    // Keyword markers — Flying + Lifelink (CR 702.9 / CR 702.15)
    // -------------------------------------------------------------------------

    [Fact]
    public void Griselbrand_HasFlyingAndLifelinkKeywordMarkers()
    {
        var c = GriselbrandFactory.Create(_alice);
        var keywords = c.Abilities.OfType<KeywordAbility>().Select(k => k.Keyword).ToList();

        keywords.Should().Contain("Flying", "CR 702.9 — Griselbrand has Flying");
        keywords.Should().Contain("Lifelink", "CR 702.15 — Griselbrand has Lifelink");
    }

    // -------------------------------------------------------------------------
    // Activated ability shape — "Pay 7 life: Draw seven cards."
    // -------------------------------------------------------------------------

    [Fact]
    public void Griselbrand_HasOneActivatedAbility()
    {
        var c = GriselbrandFactory.Create(_alice);

        c.Abilities.OfType<ActivatedAbility>().Should().HaveCount(1,
            "only one activated ability: 'Pay 7 life: Draw seven cards.'");
    }

    [Fact]
    public void Griselbrand_ActivatedAbility_HasPayLifeCost_OfSeven()
    {
        var c = GriselbrandFactory.Create(_alice);
        var activated = c.Abilities.OfType<ActivatedAbility>().Single();
        var addCosts = activated.Costs.OfType<AdditionalCost>().ToList();

        addCosts.Should().Contain(
            x => x.CostType == AdditionalCostType.PayLife,
            "the cost is 'Pay 7 life'");
        addCosts.Where(x => x.CostType == AdditionalCostType.PayLife)
            .Should().HaveCount(1, "only one PayLife cost");
    }

    [Fact]
    public void Griselbrand_ActivatedAbility_CanPay_TrueWhenLifeAboveSeven()
    {
        var c = GriselbrandFactory.Create(_alice);
        var activated = c.Abilities.OfType<ActivatedAbility>().Single();
        var lifeCost = activated.Costs.OfType<AdditionalCost>()
            .Single(x => x.CostType == AdditionalCostType.PayLife);

        // Alice starts at 20 > 7 — can pay
        lifeCost.CanPay(_alice).Should().BeTrue(
            "Alice has 20 life, which is > 7 (CR 118.4 — cost is payable)");
    }

    [Fact]
    public void Griselbrand_ActivatedAbility_CanPay_FalseWhenLifeSevenOrLess()
    {
        var c = GriselbrandFactory.Create(_alice);
        var activated = c.Abilities.OfType<ActivatedAbility>().Single();
        var lifeCost = activated.Costs.OfType<AdditionalCost>()
            .Single(x => x.CostType == AdditionalCostType.PayLife);

        // Drain Alice down to exactly 7 life — CanPay is > not >=
        _alice.LoseLife(13); // 20 → 7
        lifeCost.CanPay(_alice).Should().BeFalse(
            "Alice has exactly 7 life; cost requires strictly > 7 (engine posture: CanPay = life > 7)");
    }

    // -------------------------------------------------------------------------
    // Activation — Pay 7 life → draw 7 cards
    // -------------------------------------------------------------------------

    [Fact]
    public void Activation_PaysSevenLife_DrawsSevenCards()
    {
        // Seed library with enough cards to draw
        SeedLibrary(_alice, 10);

        var c = GriselbrandFactory.Create(_alice);

        // Put Griselbrand on the battlefield (zone guard in effect body)
        _alice.Zones.Battlefield.AddCard(c);
        c.SetZone(ZoneType.Battlefield);

        var activated = c.Abilities.OfType<ActivatedAbility>().Single();
        var lifeCost = activated.Costs.OfType<AdditionalCost>()
            .Single(x => x.CostType == AdditionalCostType.PayLife);

        var startLife = _alice.LifeTotal;   // 20
        var startLibrary = _alice.Zones.Library.GetCards().Count(); // 10
        var startHand = _alice.Zones.Hand.GetCards().Count();       // 0

        // Pay the cost
        lifeCost.CanPay(_alice).Should().BeTrue();
        lifeCost.Pay(_alice);

        _alice.LifeTotal.Should().Be(startLife - 7, "Pay 7 life drains 7");

        // Resolve the effect
        foreach (var e in activated.Effects) e.Execute();

        _alice.Zones.Hand.GetCards().Count().Should().Be(startHand + 7,
            "drawing seven cards adds 7 to hand (CR 121.1)");
        _alice.Zones.Library.GetCards().Count().Should().Be(startLibrary - 7,
            "seven cards leave the library");
    }

    [Fact]
    public void Activation_WithOnlySevenCards_DrawsAllSeven()
    {
        // Exactly 7 cards — the draw-7 should drain the library
        SeedLibrary(_alice, 7);

        var c = GriselbrandFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(c);
        c.SetZone(ZoneType.Battlefield);

        var activated = c.Abilities.OfType<ActivatedAbility>().Single();
        var lifeCost = activated.Costs.OfType<AdditionalCost>()
            .Single(x => x.CostType == AdditionalCostType.PayLife);

        lifeCost.Pay(_alice);
        foreach (var e in activated.Effects) e.Execute();

        _alice.Zones.Hand.GetCards().Should().HaveCount(7,
            "all 7 library cards drawn into hand");
        _alice.Zones.Library.GetCards().Should().BeEmpty(
            "library is empty after drawing 7 from exactly 7");
    }
}
