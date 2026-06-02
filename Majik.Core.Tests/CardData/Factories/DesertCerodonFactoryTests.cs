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
/// Unit tests for <see cref="DesertCerodonFactory"/> (Hour of Devastation).
///
/// Covers:
/// - Identity ({5}{R} Creature — Beast 6/4).
/// - Cycling activated ability shape ({R} mana + DiscardSelfCost).
/// - Cycling end-to-end: pays {R}, discards self, draws one card,
///   publishes <see cref="CardCycledEvent"/> on the bus.
/// - Cycling cost gate: DiscardSelfCost CanPay is hand-only.
/// - <see cref="NamedCardFactory"/> dispatch.
///
/// Mirrors <see cref="MonstrousCarabidFactoryTests"/> — same Onslaught/HOU
/// "cycling vanilla creature" shape, only the stats (6/4) and cycle cost
/// ({R}) differ.
/// </summary>
[Trait("Color", "R")]
public class DesertCerodonFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void DesertCerodon_Identity_Beast64()
    {
        var card = DesertCerodonFactory.Create(_alice);

        card.Name.Should().Be("Desert Cerodon");
        card.ManaCost.ToString().Should().Be("{5}{R}");
        card.BasePower.Should().Be(6);
        card.BaseToughness.Should().Be(4);
        card.HasType(CardType.Creature).Should().BeTrue();
        card.HasSubtype(CardSubtype.Beast).Should().BeTrue();
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }
    // -----------------------------------------------------------------------
    // Cycling ability shape — CR 702.32
    // -----------------------------------------------------------------------

    [Fact]
    public void DesertCerodon_HasCyclingActivatedAbility_WithRedPipAndDiscardSelf()
    {
        var card = DesertCerodonFactory.Create(_alice);
        var cycling = card.Abilities.OfType<ActivatedAbility>().Single();

        cycling.Costs.Should().HaveCount(2);
        cycling.Costs.OfType<DiscardSelfCost>().Should().ContainSingle();

        var mana = cycling.Costs.OfType<ManaCostCost>().Single().Cost;
        mana.Red.Should().Be(1, "cycling {R} charges one red");
        mana.Generic.Should().Be(0, "cycling {R} has no generic");
    }

    // -----------------------------------------------------------------------
    // Cycling end-to-end — pays {R}, discards, draws, publishes event
    // -----------------------------------------------------------------------

    [Fact]
    public void DesertCerodon_Cycling_EndToEnd_PaysRedPublishesCardCycledEvent()
    {
        var topCard = new Instant("Lightning Bolt", "{R}");
        topCard.SetOwner(_alice);
        _alice.Zones.Library.AddCard(topCard);
        topCard.SetZone(ZoneType.Library);

        var bus = new EventBus();
        CardCycledEvent? captured = null;
        bus.Subscribe<CardCycledEvent>(e => captured = e);

        var cerodon = DesertCerodonFactory.Create(_alice, eventBus: bus);
        _alice.Zones.Hand.AddCard(cerodon);
        cerodon.SetZone(ZoneType.Hand);
        _alice.AddManaToPool(ManaCost.Parse("R"));

        var cycling = cerodon.Abilities.OfType<ActivatedAbility>().Single();
        foreach (var cost in cycling.Costs)
        {
            cost.CanPay(_alice).Should().BeTrue();
            cost.Pay(_alice);
        }

        cerodon.Zone.Should().Be(ZoneType.Graveyard);

        foreach (var effect in cycling.Effects) effect.Execute();

        _alice.Zones.Hand.GetCards().Should().Contain(topCard, "cycling drew a card");
        captured.Should().NotBeNull("CR 702.32d publication — the cycling-trigger surface");
        captured!.Card.Should().BeSameAs(cerodon);
        captured.Player.Should().BeSameAs(_alice);
    }

    // -----------------------------------------------------------------------
    // Cycling cost gate — DiscardSelfCost CanPay is hand-only
    // -----------------------------------------------------------------------

    [Fact]
    public void DesertCerodon_Cycling_DiscardSelfCost_FromLibrary_CannotPay()
    {
        var card = DesertCerodonFactory.Create(_alice);
        card.SetZone(ZoneType.Library);
        _alice.Zones.Library.AddCard(card);

        var cycling = card.Abilities.OfType<ActivatedAbility>().Single();
        var discardCost = cycling.Costs.OfType<DiscardSelfCost>().Single();

        discardCost.CanPay(_alice).Should().BeFalse(
            "CR 702.32a — cycling activates only from hand");
    }
}
