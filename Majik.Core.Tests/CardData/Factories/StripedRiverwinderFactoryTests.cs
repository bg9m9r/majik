using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Events;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="StripedRiverwinderFactory"/> (Hour of
/// Devastation).
///
/// Covers:
/// - Identity ({6}{U} Creature — Serpent 5/5).
/// - Hexproof keyword marker (CR 702.11).
/// - Cycling activated ability shape ({U} mana + DiscardSelfCost).
/// - Cycling end-to-end: pays {U}, discards self, draws one card,
///   publishes <see cref="CardCycledEvent"/> on the bus — the
///   Living-End-enabler surface CR 702.32d subscribers (Lightning Rift,
///   Curator of Mysteries, cascade trigger of Living End itself once
///   reanimated) listen for.
/// - <see cref="NamedCardFactory"/> dispatch.
/// </summary>
public class StripedRiverwinderFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void StripedRiverwinder_Identity_Serpent55()
    {
        var card = StripedRiverwinderFactory.Create(_alice);

        card.Name.Should().Be("Striped Riverwinder");
        card.ManaCost.ToString().Should().Be("{6}{U}");
        card.BasePower.Should().Be(5);
        card.BaseToughness.Should().Be(5);
        card.HasType(CardType.Creature).Should().BeTrue();
        card.HasSubtype(CardSubtype.Serpent).Should().BeTrue();
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void StripedRiverwinder_HasHexproofKeyword()
    {
        var card = StripedRiverwinderFactory.Create(_alice);
        card.Abilities.OfType<KeywordAbility>()
            .Should().Contain(k => k.Keyword == "Hexproof");
    }

    [Fact]
    public void StripedRiverwinder_DispatchesViaNamedCardFactory()
    {
        var card = NamedCardFactory.Create("Striped Riverwinder", _alice);

        card.Should().BeOfType<Creature>();
        card.HasSubtype(CardSubtype.Serpent).Should().BeTrue();
        card.Abilities.OfType<KeywordAbility>()
            .Should().Contain(k => k.Keyword == "Hexproof");
        card.Abilities.OfType<KeywordAbility>()
            .Should().Contain(k => k.Keyword == "Cycling");
        card.Abilities.OfType<ActivatedAbility>().Should().HaveCount(1,
            "the cycling activated ability");
    }

    // -----------------------------------------------------------------------
    // Cycling ability shape — CR 702.32
    // -----------------------------------------------------------------------

    [Fact]
    public void StripedRiverwinder_HasCyclingActivatedAbility_WithBlueAndDiscardSelf()
    {
        var card = StripedRiverwinderFactory.Create(_alice);
        var cycling = card.Abilities.OfType<ActivatedAbility>().Single();

        cycling.Costs.Should().HaveCount(2);
        cycling.Costs.OfType<DiscardSelfCost>().Should().ContainSingle();

        var mana = cycling.Costs.OfType<ManaCostCost>().Single().Cost;
        mana.Blue.Should().Be(1, "cycling {U} charges one blue");
    }

    // -----------------------------------------------------------------------
    // Cycling end-to-end — pays {U}, discards, draws, publishes event
    // -----------------------------------------------------------------------

    [Fact]
    public void StripedRiverwinder_Cycling_EndToEnd_PaysBluePublishesCardCycledEvent()
    {
        var topCard = new Instant("Counterspell", "{U}{U}");
        topCard.SetOwner(_alice);
        _alice.Zones.Library.AddCard(topCard);
        topCard.SetZone(ZoneType.Library);

        var bus = new EventBus();
        CardCycledEvent? captured = null;
        bus.Subscribe<CardCycledEvent>(e => captured = e);

        var riverwinder = StripedRiverwinderFactory.Create(_alice, eventBus: bus);
        _alice.Zones.Hand.AddCard(riverwinder);
        riverwinder.SetZone(ZoneType.Hand);
        _alice.AddManaToPool(ManaCost.Parse("U"));

        var cycling = riverwinder.Abilities.OfType<ActivatedAbility>().Single();
        foreach (var cost in cycling.Costs)
        {
            cost.CanPay(_alice).Should().BeTrue();
            cost.Pay(_alice);
        }

        riverwinder.Zone.Should().Be(ZoneType.Graveyard);

        foreach (var effect in cycling.Effects) effect.Execute();

        _alice.Zones.Hand.GetCards().Should().Contain(topCard, "cycling drew a card");
        captured.Should().NotBeNull("CR 702.32d publication — the Living End enabler surface");
        captured!.Card.Should().BeSameAs(riverwinder);
        captured.Player.Should().BeSameAs(_alice);
    }

    // -----------------------------------------------------------------------
    // Cycling cost gate — DiscardSelfCost CanPay is hand-only
    // -----------------------------------------------------------------------

    [Fact]
    public void StripedRiverwinder_Cycling_DiscardSelfCost_FromLibrary_CannotPay()
    {
        var card = StripedRiverwinderFactory.Create(_alice);
        card.SetZone(ZoneType.Library);
        _alice.Zones.Library.AddCard(card);

        var cycling = card.Abilities.OfType<ActivatedAbility>().Single();
        var discardCost = cycling.Costs.OfType<DiscardSelfCost>().Single();

        discardCost.CanPay(_alice).Should().BeFalse(
            "CR 702.32a — cycling activates only from hand");
    }
}
