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
/// Unit tests for <see cref="StreetWraithFactory"/> (Future Sight).
///
/// Covers:
/// - Identity ({3}{B}{B} Creature — Zombie 3/4).
/// - Swampwalk keyword marker.
/// - Cycling activated ability: cost shape (<see cref="PayLifeCost"/>(2) +
///   <see cref="DiscardSelfCost"/>), hand-zone gate, life-floor gate,
///   end-to-end activation moves card hand→graveyard, pays 2 life, draws.
/// - <see cref="NamedCardFactory"/> dispatch.
/// </summary>
public class StreetWraithFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void StreetWraith_Identity_ZombieThreeFour()
    {
        var wraith = StreetWraithFactory.Create(_alice);

        wraith.Name.Should().Be("Street Wraith");
        wraith.ManaCost.ToString().Should().Be("{3}{B}{B}");
        wraith.BasePower.Should().Be(3);
        wraith.BaseToughness.Should().Be(4);
        wraith.HasType(CardType.Creature).Should().BeTrue();
        wraith.HasSubtype(CardSubtype.Zombie).Should().BeTrue();
        wraith.Owner.Should().BeSameAs(_alice);
        wraith.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void StreetWraith_HasSwampwalkKeyword()
    {
        var wraith = StreetWraithFactory.Create(_alice);

        wraith.Abilities.OfType<KeywordAbility>()
            .Should().ContainSingle(k => k.Keyword == "Swampwalk");
    }

    [Fact]
    public void StreetWraith_DispatchesViaNamedCardFactory()
    {
        var card = NamedCardFactory.Create("Street Wraith", _alice);

        card.Should().BeOfType<Creature>();
        card.Name.Should().Be("Street Wraith");
        card.HasSubtype(CardSubtype.Zombie).Should().BeTrue();
        card.Abilities.OfType<KeywordAbility>()
            .Should().ContainSingle(k => k.Keyword == "Swampwalk");
        card.Abilities.OfType<ActivatedAbility>().Should().HaveCount(1,
            "the cycling activated ability is attached");
    }

    // -----------------------------------------------------------------------
    // Cycling ability shape — CR 702.32
    // -----------------------------------------------------------------------

    [Fact]
    public void StreetWraith_HasCyclingActivatedAbility_WithPayLifeAndDiscardSelf()
    {
        var wraith = StreetWraithFactory.Create(_alice);

        var cycling = wraith.Abilities.OfType<ActivatedAbility>().Should().ContainSingle().Subject;

        cycling.Costs.Should().HaveCount(2, "cycling has Pay 2 life + Discard self");
        cycling.Costs.OfType<PayLifeCost>().Should().ContainSingle()
            .Which.Amount.Should().Be(StreetWraithFactory.CyclingLifeCost);
        cycling.Costs.OfType<DiscardSelfCost>().Should().ContainSingle();
        cycling.TargetRequests.Should().BeEmpty("cycling draws a card — no targets");
    }

    // -----------------------------------------------------------------------
    // Activation gates — CR 702.32a (hand-zone) + CR 119.4 (life floor)
    // -----------------------------------------------------------------------

    /// <summary>
    /// CR 702.32a — Cycling is activated only while the card is in the
    /// controller's hand. The <see cref="DiscardSelfCost"/> primitive
    /// gates payment on Hand-zone presence + ownership.
    /// </summary>
    [Fact]
    public void StreetWraith_DiscardSelfCost_CannotPay_FromLibrary()
    {
        var wraith = StreetWraithFactory.Create(_alice);
        wraith.SetZone(ZoneType.Library);
        _alice.Zones.Library.AddCard(wraith);

        var cycling = wraith.Abilities.OfType<ActivatedAbility>().Single();
        var discardCost = cycling.Costs.OfType<DiscardSelfCost>().Single();

        discardCost.CanPay(_alice).Should().BeFalse(
            "CR 702.32a — cycling can only be activated from hand");
    }

    [Fact]
    public void StreetWraith_DiscardSelfCost_CanPay_FromHand()
    {
        var wraith = StreetWraithFactory.Create(_alice);
        wraith.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(wraith);

        var cycling = wraith.Abilities.OfType<ActivatedAbility>().Single();
        var discardCost = cycling.Costs.OfType<DiscardSelfCost>().Single();

        discardCost.CanPay(_alice).Should().BeTrue();
    }

    /// <summary>CR 119.4 — can't pay life you don't have.</summary>
    [Fact]
    public void StreetWraith_PayLifeCost_CannotPay_AtOneLife()
    {
        _alice.LifeTotal = 1;
        var wraith = StreetWraithFactory.Create(_alice);

        var cycling = wraith.Abilities.OfType<ActivatedAbility>().Single();
        var lifeCost = cycling.Costs.OfType<PayLifeCost>().Single();

        lifeCost.CanPay(_alice).Should().BeFalse(
            "CR 119.4 — Alice has 1 life, cannot pay 2 life");
    }

    [Fact]
    public void StreetWraith_PayLifeCost_CanPay_AtTwoLife()
    {
        _alice.LifeTotal = 2;
        var wraith = StreetWraithFactory.Create(_alice);

        var cycling = wraith.Abilities.OfType<ActivatedAbility>().Single();
        var lifeCost = cycling.Costs.OfType<PayLifeCost>().Single();

        lifeCost.CanPay(_alice).Should().BeTrue("Alice has exactly 2 life");
    }

    // -----------------------------------------------------------------------
    // End-to-end activation — paying both costs + draw
    // -----------------------------------------------------------------------

    /// <summary>
    /// Paying both costs in sequence (mirroring how SpellCastFlow would
    /// invoke them) moves Street Wraith from hand → graveyard, deducts
    /// 2 life, and the effect closure draws one card.
    /// </summary>
    [Fact]
    public void StreetWraith_Cycling_EndToEnd_PaysLifeDiscardsSelfDrawsOne()
    {
        // Seed library with one card so the draw resolves.
        var topCard = new Instant("Lightning Bolt", "{R}");
        topCard.SetOwner(_alice);
        _alice.Zones.Library.AddCard(topCard);
        topCard.SetZone(ZoneType.Library);

        _alice.LifeTotal = 20;
        var wraith = StreetWraithFactory.Create(_alice);
        wraith.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(wraith);

        var cycling = wraith.Abilities.OfType<ActivatedAbility>().Single();

        // Pay both costs — order mirrors SpellCastFlow / AbilityActivator.
        foreach (var cost in cycling.Costs)
        {
            cost.CanPay(_alice).Should().BeTrue($"{cost.Description}");
            cost.Pay(_alice);
        }

        // Cost effects observable.
        _alice.LifeTotal.Should().Be(18, "paid 2 life");
        wraith.Zone.Should().Be(ZoneType.Graveyard, "discarded self");
        _alice.Zones.Hand.GetCards().Should().NotContain(wraith);
        _alice.Zones.Graveyard.GetCards().Should().Contain(wraith);

        // Resolve the activated effect (draw a card).
        foreach (var effect in cycling.Effects) effect.Execute();

        _alice.Zones.Hand.GetCards().Should().Contain(topCard,
            "Street Wraith's cycling effect draws one card");
        topCard.Zone.Should().Be(ZoneType.Hand);
    }
}
