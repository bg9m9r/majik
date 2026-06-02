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
/// Unit tests for <see cref="IrrigatedFarmlandFactory"/> — the W/U "bicycle"
/// (cycling dual) land from Amonkhet. Type line: <c>Land — Plains Island</c>.
/// Oracle text:
///   "({T}: Add {W} or {U}.)
///    This land enters tapped.
///    Cycling {2} ({2}, Discard this card: Draw a card.)"
///
/// Covers:
/// - Identity (Land + the printed Plains and Island land subtypes, CR 205.3i).
/// - Two mana abilities producing {W} and {U} respectively (CR 605.1 — mana
///   abilities don't use the stack). The subtypes would also feed the L4
///   mana-derivation pipeline (CR 305.6) but the explicit abilities make the
///   shape observable without an active ContinuousEffectsService — same
///   posture as the Onslaught cycling-land cycle.
/// - Cycling {2} (CR 702.32) — ManaCostCost("2") + DiscardSelfCost via the
///   shared <see cref="Majik.Core.Keywords.CyclingFactory"/> primitive, plus
///   the "Cycling" <see cref="KeywordAbility"/> marker.
/// - End-to-end cycle: pays {2}, discards self, draws one, publishes
///   <see cref="Majik.Core.Events.CardCycledEvent"/> (CR 702.32d).
/// - Dispatcher routing through <see cref="NamedCardFactory"/>.
///
/// "This land enters tapped" (CR 614.1c) is applied on the production load
/// path by <see cref="EntersTappedBinder"/> from the oracle text, not by this
/// factory (same posture as the Alpine Meadow / Guildgate factories).
/// </summary>
[Trait("Color", "C")]
public class IrrigatedFarmlandFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    [Fact]
    public void IrrigatedFarmland_HasTwoManaAbilities_ProducingWhiteAndBlue()
    {
        var land = (Land)NamedCardFactory.Create("Irrigated Farmland", _alice);
        var mana = land.Abilities.OfType<ManaAbility>().ToList();

        mana.Should().HaveCount(2, "Irrigated Farmland taps for {W} or {U}");
        mana.Should().Contain(m => m.ManaGenerated.White == 1);
        mana.Should().Contain(m => m.ManaGenerated.Blue == 1);
    }

    [Fact]
    public void IrrigatedFarmland_HasCyclingActivatedAbility_WithGenericManaAndDiscardSelfCosts()
    {
        var land = (Land)NamedCardFactory.Create("Irrigated Farmland", _alice);
        var cycling = land.Abilities.OfType<ActivatedAbility>().Should().ContainSingle().Subject;

        cycling.Costs.Should().HaveCount(2, "cycling = mana cost + DiscardSelfCost");
        cycling.Costs.OfType<DiscardSelfCost>().Should().HaveCount(1);

        var manaCost = cycling.Costs.OfType<ManaCostCost>().Single().Cost;
        manaCost.Generic.Should().Be(2, "Cycling {2} charges 2 generic mana");
        manaCost.White.Should().Be(0);
        manaCost.Blue.Should().Be(0);
    }

    [Fact]
    public void IrrigatedFarmland_HasCyclingKeywordMarker()
    {
        var land = (Land)NamedCardFactory.Create("Irrigated Farmland", _alice);
        land.Abilities.OfType<KeywordAbility>()
            .Should().Contain(k => k.Keyword == "Cycling");
    }

    [Fact]
    public void IrrigatedFarmland_Cycling_EndToEnd_PaysTwoDiscardsSelfDrawsOne()
    {
        // Seed library so the draw resolves.
        var topCard = new Card("Opt", "{U}");
        topCard.SetOwner(_alice);
        _alice.Zones.Library.AddCard(topCard);
        topCard.SetZone(ZoneType.Library);

        var bus = new Majik.Core.Events.EventBus();
        Majik.Core.Events.CardCycledEvent? captured = null;
        bus.Subscribe<Majik.Core.Events.CardCycledEvent>(e => captured = e);

        var land = IrrigatedFarmlandFactory.Create(_alice, eventBus: bus);
        _alice.Zones.Hand.AddCard(land);
        land.SetZone(ZoneType.Hand);

        _alice.AddManaToPool(ManaCost.Parse("2"));

        var cycling = land.Abilities.OfType<ActivatedAbility>().Single();
        foreach (var cost in cycling.Costs)
        {
            cost.CanPay(_alice).Should().BeTrue($"{cost.Description}");
            cost.Pay(_alice);
        }
        land.Zone.Should().Be(ZoneType.Graveyard, "discarded self");

        foreach (var effect in cycling.Effects) effect.Execute();

        _alice.Zones.Hand.GetCards().Should().Contain(topCard, "cycle drew one card");
        captured.Should().NotBeNull("CR 702.32d publication");
        captured!.Card.Should().BeSameAs(land);
    }
}
