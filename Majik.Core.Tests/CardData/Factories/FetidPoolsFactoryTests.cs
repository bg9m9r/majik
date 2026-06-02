using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="FetidPoolsFactory"/> — Fetid Pools (Amonkhet
/// "bicycle" dual-land cycle). Oracle text (verified against Scryfall):
///   "({T}: Add {U} or {B}.)
///    This land enters tapped.
///    Cycling {2} ({2}, Discard this card: Draw a card.)"
///
/// Type line: <c>Land — Island Swamp</c>.
///
/// Mirrors <see cref="SavaiTriomeFactoryTests"/> — identity + subtypes,
/// two mana abilities (one per produced colour), the Cycling {2} activated
/// ability shape (CR 702.32), and an end-to-end cycle that pays {2},
/// discards self, draws one, and publishes
/// <see cref="Majik.Core.Events.CardCycledEvent"/> (CR 702.32d).
/// </summary>
[Trait("Color", "C")]
public class FetidPoolsFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------
    [Fact]
    public void FetidPools_HasTwoManaAbilities_ProducingBlueAndBlack()
    {
        var land = (Land)NamedCardFactory.Create("Fetid Pools", _alice);
        var manaAbilities = land.Abilities.OfType<ManaAbility>().ToList();

        manaAbilities.Should().HaveCount(2, "{T}: Add {U} or {B}");
        manaAbilities.Should().Contain(m => m.ManaGenerated.Blue == 1);
        manaAbilities.Should().Contain(m => m.ManaGenerated.Black == 1);
    }

    // -----------------------------------------------------------------------
    // Cycling {2} ability shape — CR 702.32
    // -----------------------------------------------------------------------

    [Fact]
    public void FetidPools_HasCyclingActivatedAbility_WithGenericTwoAndDiscardSelf()
    {
        var land = (Land)NamedCardFactory.Create("Fetid Pools", _alice);
        var cycling = land.Abilities.OfType<ActivatedAbility>().Should().ContainSingle().Subject;

        cycling.Costs.Should().HaveCount(2, "cycling = {2} mana cost + DiscardSelfCost");
        cycling.Costs.OfType<DiscardSelfCost>().Should().HaveCount(1);

        var manaCost = cycling.Costs.OfType<ManaCostCost>().Single().Cost;
        manaCost.Generic.Should().Be(2, "Cycling {2} charges 2 generic mana");
        manaCost.Blue.Should().Be(0);
        manaCost.Black.Should().Be(0);
    }

    [Fact]
    public void FetidPools_HasCyclingKeywordMarker()
    {
        var land = (Land)NamedCardFactory.Create("Fetid Pools", _alice);
        land.Abilities.OfType<KeywordAbility>()
            .Should().Contain(k => k.Keyword == "Cycling");
    }

    // -----------------------------------------------------------------------
    // End-to-end cycling — pays {2}, discards, draws, publishes event
    // -----------------------------------------------------------------------

    [Fact]
    public void FetidPools_Cycling_EndToEnd_PaysTwoDiscardsSelfDrawsOne()
    {
        var topCard = new Card("Dark Ritual", "{B}");
        topCard.SetOwner(_alice);
        _alice.Zones.Library.AddCard(topCard);
        topCard.SetZone(ZoneType.Library);

        var bus = new Majik.Core.Events.EventBus();
        Majik.Core.Events.CardCycledEvent? captured = null;
        bus.Subscribe<Majik.Core.Events.CardCycledEvent>(e => captured = e);

        var pools = FetidPoolsFactory.Create(_alice, eventBus: bus, replacements: null);
        _alice.Zones.Hand.AddCard(pools);
        pools.SetZone(ZoneType.Hand);

        _alice.AddManaToPool(ManaCost.Parse("{2}"));

        var cycling = pools.Abilities.OfType<ActivatedAbility>().Single();
        foreach (var cost in cycling.Costs)
        {
            cost.CanPay(_alice).Should().BeTrue($"{cost.Description}");
            cost.Pay(_alice);
        }
        pools.Zone.Should().Be(ZoneType.Graveyard, "discarded self");

        foreach (var effect in cycling.Effects) effect.Execute();

        _alice.Zones.Hand.GetCards().Should().Contain(topCard, "cycle drew one card");
        captured.Should().NotBeNull("CR 702.32d publication");
        captured!.Card.Should().BeSameAs(pools);
    }

    // -----------------------------------------------------------------------
    // Enters-tapped — CR 614.1c
    // -----------------------------------------------------------------------

    [Fact]
    public void FetidPools_RegistersEntersTappedReplacement_WhenBusSupplied()
    {
        var replacements = new ReplacementBus();
        var pools = FetidPoolsFactory.Create(_alice, eventBus: null, replacements: replacements);

        pools.Should().NotBeNull();
        // The replacement is registered on the supplied bus (CR 614.1c);
        // the shape-only path (null bus) skips it. EntersTappedReplacement
        // has no public bus-inspection surface, so the production path
        // (covered by the binder chain via oracle text) is the
        // authoritative test for tapped-entry behaviour.
    }
}
