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
/// Unit tests for <see cref="MonstrousCarabidFactory"/> (Onslaught).
///
/// Covers:
/// - Identity ({4}{B} Creature — Insect 4/1).
/// - Cycling activated ability shape ({2} mana + DiscardSelfCost).
/// - Cycling end-to-end: pays {2}, discards self, draws one card,
///   publishes <see cref="CardCycledEvent"/> on the bus.
/// - Cycling cost gate: DiscardSelfCost CanPay is hand-only.
/// - <see cref="NamedCardFactory"/> dispatch.
/// </summary>
public class MonstrousCarabidFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void MonstrousCarabid_Identity_Insect41()
    {
        var card = MonstrousCarabidFactory.Create(_alice);

        card.Name.Should().Be("Monstrous Carabid");
        card.ManaCost.ToString().Should().Be("{4}{B}");
        card.BasePower.Should().Be(4);
        card.BaseToughness.Should().Be(1);
        card.HasType(CardType.Creature).Should().BeTrue();
        card.HasSubtype(CardSubtype.Insect).Should().BeTrue();
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void MonstrousCarabid_DispatchesViaNamedCardFactory()
    {
        var card = NamedCardFactory.Create("Monstrous Carabid", _alice);

        card.Should().BeOfType<Creature>();
        card.HasSubtype(CardSubtype.Insect).Should().BeTrue();
        card.Abilities.OfType<KeywordAbility>()
            .Should().Contain(k => k.Keyword == "Cycling");
        card.Abilities.OfType<ActivatedAbility>().Should().HaveCount(1,
            "the cycling activated ability");
    }

    // -----------------------------------------------------------------------
    // Cycling ability shape — CR 702.32
    // -----------------------------------------------------------------------

    [Fact]
    public void MonstrousCarabid_HasCyclingActivatedAbility_With2GenericAndDiscardSelf()
    {
        var card = MonstrousCarabidFactory.Create(_alice);
        var cycling = card.Abilities.OfType<ActivatedAbility>().Single();

        cycling.Costs.Should().HaveCount(2);
        cycling.Costs.OfType<DiscardSelfCost>().Should().ContainSingle();

        var mana = cycling.Costs.OfType<ManaCostCost>().Single().Cost;
        mana.Generic.Should().Be(2, "cycling {2} charges two generic");
    }

    // -----------------------------------------------------------------------
    // Cycling end-to-end — pays {2}, discards, draws, publishes event
    // -----------------------------------------------------------------------

    [Fact]
    public void MonstrousCarabid_Cycling_EndToEnd_PaysGenericPublishesCardCycledEvent()
    {
        var topCard = new Instant("Dark Ritual", "{B}");
        topCard.SetOwner(_alice);
        _alice.Zones.Library.AddCard(topCard);
        topCard.SetZone(ZoneType.Library);

        var bus = new EventBus();
        CardCycledEvent? captured = null;
        bus.Subscribe<CardCycledEvent>(e => captured = e);

        var carabid = MonstrousCarabidFactory.Create(_alice, eventBus: bus);
        _alice.Zones.Hand.AddCard(carabid);
        carabid.SetZone(ZoneType.Hand);
        _alice.AddManaToPool(ManaCost.Parse("2"));

        var cycling = carabid.Abilities.OfType<ActivatedAbility>().Single();
        foreach (var cost in cycling.Costs)
        {
            cost.CanPay(_alice).Should().BeTrue();
            cost.Pay(_alice);
        }

        carabid.Zone.Should().Be(ZoneType.Graveyard);

        foreach (var effect in cycling.Effects) effect.Execute();

        _alice.Zones.Hand.GetCards().Should().Contain(topCard, "cycling drew a card");
        captured.Should().NotBeNull("CR 702.32d publication — the Living End enabler surface");
        captured!.Card.Should().BeSameAs(carabid);
        captured.Player.Should().BeSameAs(_alice);
    }

    // -----------------------------------------------------------------------
    // Cycling cost gate — DiscardSelfCost CanPay is hand-only
    // -----------------------------------------------------------------------

    [Fact]
    public void MonstrousCarabid_Cycling_DiscardSelfCost_FromLibrary_CannotPay()
    {
        var card = MonstrousCarabidFactory.Create(_alice);
        card.SetZone(ZoneType.Library);
        _alice.Zones.Library.AddCard(card);

        var cycling = card.Abilities.OfType<ActivatedAbility>().Single();
        var discardCost = cycling.Costs.OfType<DiscardSelfCost>().Single();

        discardCost.CanPay(_alice).Should().BeFalse(
            "CR 702.32a — cycling activates only from hand");
    }
}
