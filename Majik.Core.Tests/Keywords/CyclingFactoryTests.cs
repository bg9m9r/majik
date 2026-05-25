using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Costs;
using Majik.Core.Events;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.Keywords;

/// <summary>
/// Tests for <see cref="CyclingFactory"/> — the shared activated-from-hand
/// builder for the Cycling keyword (CR 702.32).
///
/// Covers:
/// - Build attaches a <see cref="KeywordAbility"/> "Cycling" marker plus a
///   single <see cref="ActivatedAbility"/> with cost stack
///   <c>[cycleCost, DiscardSelfCost]</c>.
/// - Hand-zone gate via <see cref="DiscardSelfCost.CanPay"/>
///   (CR 702.32a — activates only while in hand).
/// - Mana-cost cycling end-to-end (charges the mana, discards, draws).
/// - <see cref="PayLifeCost"/> alt-cost cycling end-to-end (charges life,
///   discards, draws).
/// - <see cref="CardCycledEvent"/> publication on resolve when a bus is
///   supplied (CR 702.32d).
/// - Bus omitted → ability still attached, event not published.
/// - <see cref="ArgumentException"/> when Owner isn't wired.
/// </summary>
public class CyclingFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Shape — KeywordAbility marker + activated-ability cost stack
    // -----------------------------------------------------------------------

    [Fact]
    public void Build_AttachesCyclingKeywordMarker()
    {
        var card = MakeCardInHand("Krosan Tusker");

        CyclingFactory.Build(card, new ManaCostCost("{G}"));

        card.Abilities.OfType<KeywordAbility>()
            .Should().ContainSingle(k => k.Keyword == "Cycling");
    }

    [Fact]
    public void Build_AttachesSingleActivatedAbility_WithCycleCostPlusDiscardSelf()
    {
        var card = MakeCardInHand("Krosan Tusker");

        var ability = CyclingFactory.Build(card, new ManaCostCost("{G}"));

        ability.Costs.Should().HaveCount(2,
            "cycling = caller-supplied cost + DiscardSelfCost (CR 702.32a)");
        ability.Costs.OfType<ManaCostCost>().Should().ContainSingle()
            .Which.Cost.Green.Should().Be(1);
        ability.Costs.OfType<DiscardSelfCost>().Should().ContainSingle();
        ability.TargetRequests.Should().BeEmpty(
            "cycling draws a card with no targets");
        card.Abilities.OfType<ActivatedAbility>().Should().HaveCount(1,
            "exactly one activated ability is attached");
    }

    // -----------------------------------------------------------------------
    // Hand-zone gate — CR 702.32a
    // -----------------------------------------------------------------------

    [Fact]
    public void DiscardSelfCost_PayableWhenCardInHand()
    {
        var card = MakeCardInHand("Krosan Tusker");
        var ability = CyclingFactory.Build(card, new ManaCostCost("{G}"));
        var discard = ability.Costs.OfType<DiscardSelfCost>().Single();

        discard.CanPay(_alice).Should().BeTrue(
            "CR 702.32a — cycling activates only while card is in hand");
    }

    [Fact]
    public void DiscardSelfCost_RejectedWhenCardInLibrary()
    {
        var card = new Card("Krosan Tusker", "{4}{G}");
        card.SetOwner(_alice);
        _alice.Zones.Library.AddCard(card);
        card.SetZone(ZoneType.Library);

        var ability = CyclingFactory.Build(card, new ManaCostCost("{G}"));
        var discard = ability.Costs.OfType<DiscardSelfCost>().Single();

        discard.CanPay(_alice).Should().BeFalse(
            "card in library — cycling can't activate (CR 702.32a)");
    }

    // -----------------------------------------------------------------------
    // End-to-end activation — mana-cost cycling
    // -----------------------------------------------------------------------

    [Fact]
    public void ManaCostCycling_EndToEnd_ChargesManaDiscardsSelfDrawsOne()
    {
        // Seed library with one card so the draw resolves.
        var topCard = new Card("Lightning Bolt", "{R}");
        topCard.SetOwner(_alice);
        _alice.Zones.Library.AddCard(topCard);
        topCard.SetZone(ZoneType.Library);

        var card = MakeCardInHand("Krosan Tusker");
        _alice.AddManaToPool(ManaCost.Parse("G"));

        var ability = CyclingFactory.Build(card, new ManaCostCost("{G}"));

        // Pay both costs — mirrors SpellCastFlow / AbilityActivator order.
        foreach (var cost in ability.Costs)
        {
            cost.CanPay(_alice).Should().BeTrue($"{cost.Description}");
            cost.Pay(_alice);
        }
        card.Zone.Should().Be(ZoneType.Graveyard, "discarded self");
        _alice.Zones.Hand.GetCards().Should().NotContain(card);

        // Resolve effect — draws one.
        foreach (var effect in ability.Effects) effect.Execute();

        _alice.Zones.Hand.GetCards().Should().Contain(topCard,
            "cycling resolve draws one card (CR 702.32a)");
        topCard.Zone.Should().Be(ZoneType.Hand);
    }

    // -----------------------------------------------------------------------
    // End-to-end — PayLifeCost alt-cost (Street Wraith-style)
    // -----------------------------------------------------------------------

    [Fact]
    public void PayLifeCycling_EndToEnd_ChargesLifeDiscardsSelfDrawsOne()
    {
        var topCard = new Card("Dark Confidant", "{1}{B}");
        topCard.SetOwner(_alice);
        _alice.Zones.Library.AddCard(topCard);
        topCard.SetZone(ZoneType.Library);

        var card = MakeCardInHand("Street Wraith (test stub)");
        _alice.LifeTotal = 20;

        var ability = CyclingFactory.Build(card, new PayLifeCost(2));

        foreach (var cost in ability.Costs)
        {
            cost.CanPay(_alice).Should().BeTrue($"{cost.Description}");
            cost.Pay(_alice);
        }
        _alice.LifeTotal.Should().Be(18, "paid 2 life");
        card.Zone.Should().Be(ZoneType.Graveyard);

        foreach (var effect in ability.Effects) effect.Execute();

        _alice.Zones.Hand.GetCards().Should().Contain(topCard);
    }

    // -----------------------------------------------------------------------
    // CardCycledEvent publication — CR 702.32d
    // -----------------------------------------------------------------------

    [Fact]
    public void Cycle_PublishesCardCycledEvent_WhenBusSupplied()
    {
        var topCard = new Card("Lightning Bolt", "{R}");
        topCard.SetOwner(_alice);
        _alice.Zones.Library.AddCard(topCard);

        var card = MakeCardInHand("Krosan Tusker");
        _alice.AddManaToPool(ManaCost.Parse("G"));

        var bus = new EventBus();
        CardCycledEvent? captured = null;
        bus.Subscribe<CardCycledEvent>(e => captured = e);

        var ability = CyclingFactory.Build(card, new ManaCostCost("{G}"), bus);

        foreach (var cost in ability.Costs) cost.Pay(_alice);
        foreach (var effect in ability.Effects) effect.Execute();

        captured.Should().NotBeNull("CR 702.32d — cycling publishes the cycled event");
        captured!.Card.Should().BeSameAs(card);
        captured.Player.Should().BeSameAs(_alice);
    }

    [Fact]
    public void Cycle_NoBus_NoEventPublished_AbilityStillAttached()
    {
        var card = MakeCardInHand("Krosan Tusker");

        var ability = CyclingFactory.Build(card, new ManaCostCost("{G}"));

        ability.Should().NotBeNull("ability is attached even on shape-only path");
        card.Abilities.OfType<ActivatedAbility>().Should().HaveCount(1);
        // No bus → no observable event surface to assert; the resolve
        // body's eventBus reference is null and the publish branch is
        // skipped (shape-only path).
    }

    // -----------------------------------------------------------------------
    // Argument validation
    // -----------------------------------------------------------------------

    [Fact]
    public void Build_ThrowsWhenOwnerNotWired()
    {
        var card = new Card("Krosan Tusker", "{4}{G}"); // no SetOwner

        var act = () => CyclingFactory.Build(card, new ManaCostCost("{G}"));

        act.Should().Throw<ArgumentException>("the resolve body draws for the owner");
    }

    [Fact]
    public void Build_ThrowsOnNullSource()
    {
        var act = () => CyclingFactory.Build(null!, new ManaCostCost("{G}"));

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Build_ThrowsOnNullCycleCost()
    {
        var card = MakeCardInHand("Krosan Tusker");

        var act = () => CyclingFactory.Build(card, null!);

        act.Should().Throw<ArgumentNullException>();
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private Card MakeCardInHand(string name)
    {
        var card = new Card(name, "{4}{G}");
        card.SetOwner(_alice);
        _alice.Zones.Hand.AddCard(card);
        card.SetZone(ZoneType.Hand);
        return card;
    }
}
