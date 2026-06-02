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
/// Unit tests for <see cref="DesertOfTheFerventFactory"/> — the Hour of
/// Devastation cycling desert (red member). Mirrors
/// <see cref="OnslaughtCyclingLandFactoryTests"/> but for this card's
/// shape: Land — Desert, {T}: Add {R}, Cycling {1}{R}.
///
/// Covers:
/// - Identity (Land + Desert subtype).
/// - Mana ability ({T}: Add {R}).
/// - Cycling ability shape (ManaCostCost {1}{R} + DiscardSelfCost via the
///   shared <see cref="Majik.Core.Keywords.CyclingFactory"/> primitive).
/// - Cycling keyword marker.
/// - End-to-end cycle: pays {1}{R}, discards self, draws one, publishes
///   <see cref="Majik.Core.Events.CardCycledEvent"/> when a bus is supplied.
/// - Dispatcher routing through <see cref="NamedCardFactory"/>.
/// </summary>
[Trait("Color", "C")]
public class DesertOfTheFerventFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------
    [Fact]
    public void HasManaAbilityProducingRed()
    {
        var land = (Land)NamedCardFactory.Create("Desert of the Fervent", _alice);
        var mana = land.Abilities.OfType<ManaAbility>().Should().ContainSingle().Subject;
        mana.ManaGenerated.Red.Should().Be(1, "{T}: Add {R}");
    }

    // -----------------------------------------------------------------------
    // Cycling ability shape — CR 702.32
    // -----------------------------------------------------------------------

    [Fact]
    public void HasCyclingActivatedAbility_WithManaAndDiscardSelfCosts()
    {
        var land = (Land)NamedCardFactory.Create("Desert of the Fervent", _alice);
        var cycling = land.Abilities.OfType<ActivatedAbility>().Should().ContainSingle().Subject;

        cycling.Costs.Should().HaveCount(2, "cycling = mana cost + DiscardSelfCost");
        cycling.Costs.OfType<DiscardSelfCost>().Should().HaveCount(1);

        var manaCost = cycling.Costs.OfType<ManaCostCost>().Single().Cost;
        manaCost.Red.Should().Be(1, "cycling {1}{R} charges one red");
        manaCost.Generic.Should().Be(1, "cycling {1}{R} charges one generic");
    }

    [Fact]
    public void HasCyclingKeywordMarker()
    {
        var land = (Land)NamedCardFactory.Create("Desert of the Fervent", _alice);
        land.Abilities.OfType<KeywordAbility>()
            .Should().Contain(k => k.Keyword == "Cycling");
    }

    // -----------------------------------------------------------------------
    // End-to-end cycling — pays {1}{R}, discards, draws, publishes event
    // -----------------------------------------------------------------------

    [Fact]
    public void Cycling_EndToEnd_PaysOneRedDiscardsSelfDrawsOne()
    {
        // Seed library so the draw resolves.
        var topCard = new Card("Goblin Guide", "{R}");
        topCard.SetOwner(_alice);
        _alice.Zones.Library.AddCard(topCard);
        topCard.SetZone(ZoneType.Library);

        var bus = new Majik.Core.Events.EventBus();
        Majik.Core.Events.CardCycledEvent? captured = null;
        bus.Subscribe<Majik.Core.Events.CardCycledEvent>(e => captured = e);

        var desert = DesertOfTheFerventFactory.Create(
            _alice,
            eventBus: bus,
            replacements: null);
        _alice.Zones.Hand.AddCard(desert);
        desert.SetZone(ZoneType.Hand);

        // {1}{R} = two mana; pay with {R}{R} (one red covers the generic).
        _alice.AddManaToPool(ManaCost.Parse("{R}{R}"));

        var cycling = desert.Abilities.OfType<ActivatedAbility>().Single();
        foreach (var cost in cycling.Costs)
        {
            cost.CanPay(_alice).Should().BeTrue($"{cost.Description}");
            cost.Pay(_alice);
        }
        desert.Zone.Should().Be(ZoneType.Graveyard, "discarded self");

        foreach (var effect in cycling.Effects) effect.Execute();

        _alice.Zones.Hand.GetCards().Should().Contain(topCard, "cycle drew one card");
        captured.Should().NotBeNull("CR 702.32d publication");
        captured!.Card.Should().BeSameAs(desert);
    }
}
