using FluentAssertions;
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
/// Tests for <see cref="DemandAnswersFactory"/> — Instant {1}{R}
/// (Murders at Karlov Manor).
///
/// "As an additional cost to cast this spell, sacrifice an artifact or
///  discard a card. Draw two cards."
///
/// Covers:
///   - Identity (Instant, {1}{R}, owner / controller) + NamedCardFactory
///     dispatch (built from the embedded JSON definition).
///   - SpellDefinition shape:
///     <see cref="SacrificeAnArtifactOrDiscardCardAdditionalCost"/>
///     additional cost (CR 601.2f), no modes, no X, no target requests.
///   - Resolve: caster draws two cards (CR 121.1).
///   - Resolve: empty library mid-draw → draws what's available, SBA loss
///     flag set (CR 704.5b).
///   - Cost picks sac mode when an artifact is available, discard mode
///     otherwise (CR 601.2f — disjunctive additional cost).
///   - CanPay is false only when the caster controls no artifact AND has
///     no card in hand (CR 117.1).
/// </summary>
public class DemandAnswersTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void Identity_NameTypeAndManaCost()
    {
        var card = DemandAnswersFactory.Create(_alice);

        card.Name.Should().Be("Demand Answers");
        card.ManaCost.Should().Be("{1}{R}");
        card.HasType(CardType.Instant).Should().BeTrue();
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_DemandAnswers()
    {
        var card = NamedCardFactory.Create("Demand Answers", _alice);

        card.Should().BeOfType<Instant>();
        card.Name.Should().Be("Demand Answers");
        card.HasType(CardType.Instant).Should().BeTrue();
        card.ManaCost.Should().Be("{1}{R}");
        card.Owner.Should().BeSameAs(_alice);
    }

    // -----------------------------------------------------------------------
    // SpellDefinition shape
    // -----------------------------------------------------------------------

    [Fact]
    public void SpellDefinition_DeclaresSacArtifactOrDiscardCost_NoTargets()
    {
        var def = DemandAnswersFactory.BuildSpellDefinition(_alice);

        def.AdditionalCostsOrEmpty.Should().ContainSingle()
            .Which.Should().BeOfType<SacrificeAnArtifactOrDiscardCardAdditionalCost>(
                "Demand Answers prints 'As an additional cost to cast this spell, sacrifice an artifact or discard a card.' (CR 601.2f)");
        def.HasVariableX.Should().BeFalse();
        def.Modes.Should().BeEmpty();
        def.TargetRequests.Should().BeEmpty("Demand Answers has no targets");
    }

    // -----------------------------------------------------------------------
    // Resolve — draw two cards
    // -----------------------------------------------------------------------

    [Fact]
    public void Resolve_DrawsTwoCards()
    {
        var top1 = SeedLibraryCard(_alice, "Top1");
        var top2 = SeedLibraryCard(_alice, "Top2");
        SeedLibraryCard(_alice, "Top3");

        foreach (var e in DemandAnswersFactory.BuildResolveEffect(_alice)) e.Execute();

        _alice.Zones.Hand.GetCards().Should().HaveCount(2)
            .And.Contain(new[] { top1, top2 });
        _alice.Zones.Library.GetCards().Should().ContainSingle();
        _alice.TriedToDrawFromEmptyLibrary.Should().BeFalse();
    }

    [Fact]
    public void Resolve_EmptyLibraryMidDraw_DrawsWhatsAvailable_FlagsSbaLoss()
    {
        var only = SeedLibraryCard(_alice, "Only");

        foreach (var e in DemandAnswersFactory.BuildResolveEffect(_alice)) e.Execute();

        _alice.Zones.Hand.GetCards().Should().ContainSingle()
            .Which.Should().BeSameAs(only);
        _alice.Zones.Library.GetCards().Should().BeEmpty();
        _alice.TriedToDrawFromEmptyLibrary.Should().BeTrue(
            "second draw hit an empty library — SBA flag must be set (CR 704.5b)");
    }

    // -----------------------------------------------------------------------
    // Cost: prefers sacrificing an artifact, falls back to discard
    // -----------------------------------------------------------------------

    [Fact]
    public void Cost_PrefersSacrificeWhenArtifactAvailable()
    {
        var treasure = new Artifact("Treasure", "");
        treasure.SetOwner(_alice);
        treasure.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(treasure);
        treasure.SetZone(ZoneType.Battlefield);

        var spareCard = new Instant("Bogus Spell", "{R}");
        spareCard.SetOwner(_alice);
        _alice.Zones.Hand.AddCard(spareCard);
        spareCard.SetZone(ZoneType.Hand);

        var cost = new SacrificeAnArtifactOrDiscardCardAdditionalCost();
        cost.CanPay(_alice).Should().BeTrue();
        cost.Pay(_alice).Should().BeTrue();

        cost.Sacrificed.Should().BeSameAs(treasure,
            "artifact is available — sac mode wins (v1 deterministic)");
        cost.Discarded.Should().BeNull();
        treasure.Zone.Should().Be(ZoneType.Graveyard);
        spareCard.Zone.Should().Be(ZoneType.Hand, "the spare hand card was NOT discarded");
    }

    [Fact]
    public void Cost_FallsBackToDiscardWhenNoArtifact()
    {
        var spareCard = new Instant("Bogus Spell", "{R}");
        spareCard.SetOwner(_alice);
        _alice.Zones.Hand.AddCard(spareCard);
        spareCard.SetZone(ZoneType.Hand);

        var cost = new SacrificeAnArtifactOrDiscardCardAdditionalCost();
        cost.CanPay(_alice).Should().BeTrue();
        cost.Pay(_alice).Should().BeTrue();

        cost.Sacrificed.Should().BeNull();
        cost.Discarded.Should().BeSameAs(spareCard,
            "no artifact to sacrifice — discard mode is the only payable mode");
        spareCard.Zone.Should().Be(ZoneType.Graveyard);
    }

    [Fact]
    public void Cost_CanPay_FalseWhenNoArtifactAndEmptyHand()
    {
        var cost = new SacrificeAnArtifactOrDiscardCardAdditionalCost();
        cost.CanPay(_alice).Should().BeFalse(
            "neither mode can be paid (CR 117.1)");
    }

    [Fact]
    public void Cost_DoesNotSacrificeNonArtifactPermanents()
    {
        // A creature on the battlefield is NOT a legal sacrifice for this
        // cost — only artifacts. With no artifact and a card in hand, the
        // discard mode must be used.
        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        bear.SetOwner(_alice);
        bear.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(bear);
        bear.SetZone(ZoneType.Battlefield);

        var spareCard = new Instant("Bogus Spell", "{R}");
        spareCard.SetOwner(_alice);
        _alice.Zones.Hand.AddCard(spareCard);
        spareCard.SetZone(ZoneType.Hand);

        var cost = new SacrificeAnArtifactOrDiscardCardAdditionalCost();
        cost.Pay(_alice).Should().BeTrue();

        cost.Sacrificed.Should().BeNull("a creature is not an artifact");
        cost.Discarded.Should().BeSameAs(spareCard);
        bear.Zone.Should().Be(ZoneType.Battlefield, "the creature was not sacrificed");
        spareCard.Zone.Should().Be(ZoneType.Graveyard);
    }

    private static ICard SeedLibraryCard(Player p, string name)
    {
        var c = new Card(name, "");
        c.SetOwner(p);
        p.Zones.Library.AddCard(c);
        c.SetZone(ZoneType.Library);
        return c;
    }
}
