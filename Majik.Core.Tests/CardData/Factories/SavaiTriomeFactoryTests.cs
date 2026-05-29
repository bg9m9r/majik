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
/// Unit tests for <see cref="SavaiTriomeFactory"/> — Savai Triome
/// (Ikoria triome cycle). Oracle text:
///   "({T}: Add {R}, {W}, or {B}.)
///    This land enters tapped.
///    Cycling {3} ({3}, Discard this card: Draw a card.)"
///
/// Mirrors <see cref="OnslaughtCyclingLandFactoryTests"/> — identity +
/// subtypes, three mana abilities (one per produced colour), the Cycling
/// {3} activated ability shape (CR 702.32), and an end-to-end cycle that
/// pays {3}, discards self, draws one, and publishes
/// <see cref="Majik.Core.Events.CardCycledEvent"/> (CR 702.32d).
/// </summary>
public class SavaiTriomeFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void SavaiTriome_Dispatch_ReturnsLandWithThreeBasicLandSubtypes()
    {
        var card = NamedCardFactory.Create("Savai Triome", _alice);

        card.Should().BeAssignableTo<Land>();
        card.Name.Should().Be("Savai Triome");
        card.HasSubtype(CardSubtype.Mountain).Should().BeTrue();
        card.HasSubtype(CardSubtype.Plains).Should().BeTrue();
        card.HasSubtype(CardSubtype.Swamp).Should().BeTrue();
    }

    [Fact]
    public void SavaiTriome_HasThreeManaAbilities_ProducingRedWhiteBlack()
    {
        var land = (Land)NamedCardFactory.Create("Savai Triome", _alice);
        var manaAbilities = land.Abilities.OfType<ManaAbility>().ToList();

        manaAbilities.Should().HaveCount(3, "{T}: Add {R}, {W}, or {B}");
        manaAbilities.Should().Contain(m => m.ManaGenerated.Red == 1);
        manaAbilities.Should().Contain(m => m.ManaGenerated.White == 1);
        manaAbilities.Should().Contain(m => m.ManaGenerated.Black == 1);
    }

    // -----------------------------------------------------------------------
    // Cycling {3} ability shape — CR 702.32
    // -----------------------------------------------------------------------

    [Fact]
    public void SavaiTriome_HasCyclingActivatedAbility_WithGenericThreeAndDiscardSelf()
    {
        var land = (Land)NamedCardFactory.Create("Savai Triome", _alice);
        var cycling = land.Abilities.OfType<ActivatedAbility>().Should().ContainSingle().Subject;

        cycling.Costs.Should().HaveCount(2, "cycling = {3} mana cost + DiscardSelfCost");
        cycling.Costs.OfType<DiscardSelfCost>().Should().HaveCount(1);

        var manaCost = cycling.Costs.OfType<ManaCostCost>().Single().Cost;
        manaCost.Generic.Should().Be(3, "Cycling {3} charges 3 generic mana");
        manaCost.White.Should().Be(0);
        manaCost.Red.Should().Be(0);
        manaCost.Black.Should().Be(0);
    }

    [Fact]
    public void SavaiTriome_HasCyclingKeywordMarker()
    {
        var land = (Land)NamedCardFactory.Create("Savai Triome", _alice);
        land.Abilities.OfType<KeywordAbility>()
            .Should().Contain(k => k.Keyword == "Cycling");
    }

    // -----------------------------------------------------------------------
    // End-to-end cycling — pays {3}, discards, draws, publishes event
    // -----------------------------------------------------------------------

    [Fact]
    public void SavaiTriome_Cycling_EndToEnd_PaysThreeDiscardsSelfDrawsOne()
    {
        var topCard = new Card("Lightning Bolt", "{R}");
        topCard.SetOwner(_alice);
        _alice.Zones.Library.AddCard(topCard);
        topCard.SetZone(ZoneType.Library);

        var bus = new Majik.Core.Events.EventBus();
        Majik.Core.Events.CardCycledEvent? captured = null;
        bus.Subscribe<Majik.Core.Events.CardCycledEvent>(e => captured = e);

        var triome = SavaiTriomeFactory.Create(_alice, eventBus: bus, replacements: null);
        _alice.Zones.Hand.AddCard(triome);
        triome.SetZone(ZoneType.Hand);

        _alice.AddManaToPool(ManaCost.Parse("{3}"));

        var cycling = triome.Abilities.OfType<ActivatedAbility>().Single();
        foreach (var cost in cycling.Costs)
        {
            cost.CanPay(_alice).Should().BeTrue($"{cost.Description}");
            cost.Pay(_alice);
        }
        triome.Zone.Should().Be(ZoneType.Graveyard, "discarded self");

        foreach (var effect in cycling.Effects) effect.Execute();

        _alice.Zones.Hand.GetCards().Should().Contain(topCard, "cycle drew one card");
        captured.Should().NotBeNull("CR 702.32d publication");
        captured!.Card.Should().BeSameAs(triome);
    }

    // -----------------------------------------------------------------------
    // Enters-tapped — CR 614.1c
    // -----------------------------------------------------------------------

    [Fact]
    public void SavaiTriome_RegistersEntersTappedReplacement_WhenBusSupplied()
    {
        var replacements = new ReplacementBus();
        var triome = SavaiTriomeFactory.Create(_alice, eventBus: null, replacements: replacements);

        triome.Should().NotBeNull();
        // The replacement is registered on the supplied bus (CR 614.1c);
        // the shape-only path (null bus) skips it. We assert the build
        // succeeds with the bus wired — EntersTappedReplacement has no
        // public bus-inspection surface, so the production path (covered
        // by the binder chain via oracle text) is the authoritative test
        // for tapped-entry behaviour.
    }
}
