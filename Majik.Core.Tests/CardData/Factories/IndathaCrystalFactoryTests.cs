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
/// Unit tests for <see cref="IndathaCrystalFactory"/> — the Abzan ({W}{B}{G})
/// member of the Ikoria "Crystal" mana-rock cycle. Oracle text (verified
/// against Scryfall):
///   "{T}: Add {W}, {B}, or {G}.
///    Cycling {2} ({2}, Discard this card: Draw a card.)"
///
/// Covers:
/// - Identity (Artifact, mana cost {3}).
/// - Three mana abilities producing {W}, {B}, {G} respectively (CR 605.1).
/// - Cycling ability shape (ManaCostCost {2} + DiscardSelfCost via the shared
///   <see cref="Majik.Core.Keywords.CyclingFactory"/> primitive, CR 702.32).
/// - End-to-end cycling: pays {2}, discards self, draws one card, publishes
///   <see cref="Majik.Core.Events.CardCycledEvent"/>.
/// - Dispatcher routing through <see cref="NamedCardFactory"/>.
/// </summary>
public class IndathaCrystalFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void IndathaCrystal_IsArtifact_ThreeCost()
    {
        var crystal = IndathaCrystalFactory.Create(_alice);

        crystal.Name.Should().Be("Indatha Crystal");
        crystal.HasType(CardType.Artifact).Should().BeTrue();
        crystal.ManaCost.Should().Be("{3}");
        crystal.Owner.Should().BeSameAs(_alice);
        crystal.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_IndathaCrystal()
    {
        var card = NamedCardFactory.Create("Indatha Crystal", _alice);

        card.Should().BeOfType<Artifact>();
        card.Name.Should().Be("Indatha Crystal");
        card.HasType(CardType.Artifact).Should().BeTrue();
        card.ManaCost.Should().Be("{3}");
    }

    [Fact]
    public void IndathaCrystal_HasThreeManaAbilities_ProducingWBG()
    {
        var crystal = (Artifact)NamedCardFactory.Create("Indatha Crystal", _alice);
        var mana = crystal.Abilities.OfType<ManaAbility>().ToList();

        mana.Should().HaveCount(3, "Indatha Crystal taps for {W}, {B}, or {G}");
        mana.Should().Contain(m => m.ManaGenerated.White == 1);
        mana.Should().Contain(m => m.ManaGenerated.Black == 1);
        mana.Should().Contain(m => m.ManaGenerated.Green == 1);
        mana.Should().OnlyContain(m => m.ManaGenerated.TotalValue == 1);
    }

    // -----------------------------------------------------------------------
    // Cycling ability shape — CR 702.32
    // -----------------------------------------------------------------------

    [Fact]
    public void IndathaCrystal_HasCyclingActivatedAbility_WithGenericTwoAndDiscardSelf()
    {
        var crystal = (Artifact)NamedCardFactory.Create("Indatha Crystal", _alice);
        var cycling = crystal.Abilities.OfType<ActivatedAbility>().Should().ContainSingle().Subject;

        cycling.Costs.Should().HaveCount(2, "cycling = {2} mana cost + DiscardSelfCost");
        cycling.Costs.OfType<DiscardSelfCost>().Should().HaveCount(1);

        var manaCost = cycling.Costs.OfType<ManaCostCost>().Single().Cost;
        manaCost.Generic.Should().Be(2, "Cycling {2} charges 2 generic mana");
        manaCost.White.Should().Be(0);
        manaCost.Black.Should().Be(0);
        manaCost.Green.Should().Be(0);
    }

    [Fact]
    public void IndathaCrystal_HasCyclingKeywordMarker()
    {
        var crystal = (Artifact)NamedCardFactory.Create("Indatha Crystal", _alice);
        crystal.Abilities.OfType<KeywordAbility>()
            .Should().Contain(k => k.Keyword == "Cycling");
    }

    // -----------------------------------------------------------------------
    // End-to-end cycling — pays {2}, discards, draws, publishes event
    // -----------------------------------------------------------------------

    [Fact]
    public void IndathaCrystal_Cycling_EndToEnd_PaysTwoDiscardsSelfDrawsOne()
    {
        var topCard = new Card("Llanowar Elves", "{G}");
        topCard.SetOwner(_alice);
        _alice.Zones.Library.AddCard(topCard);
        topCard.SetZone(ZoneType.Library);

        var bus = new Majik.Core.Events.EventBus();
        Majik.Core.Events.CardCycledEvent? captured = null;
        bus.Subscribe<Majik.Core.Events.CardCycledEvent>(e => captured = e);

        var crystal = IndathaCrystalFactory.Create(_alice, eventBus: bus);
        _alice.Zones.Hand.AddCard(crystal);
        crystal.SetZone(ZoneType.Hand);

        _alice.AddManaToPool(ManaCost.Parse("2"));

        var cycling = crystal.Abilities.OfType<ActivatedAbility>().Single();
        foreach (var cost in cycling.Costs)
        {
            cost.CanPay(_alice).Should().BeTrue($"{cost.Description}");
            cost.Pay(_alice);
        }
        crystal.Zone.Should().Be(ZoneType.Graveyard, "discarded self");

        foreach (var effect in cycling.Effects) effect.Execute();

        _alice.Zones.Hand.GetCards().Should().Contain(topCard, "cycle drew one card");
        captured.Should().NotBeNull("CR 702.32d publication");
        captured!.Card.Should().BeSameAs(crystal);
    }
}
