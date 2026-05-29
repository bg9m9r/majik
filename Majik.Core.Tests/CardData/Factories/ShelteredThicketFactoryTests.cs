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
/// Unit tests for <see cref="ShelteredThicketFactory"/> — the Amonkhet
/// R/G dual-type cycling tapland. Type line: <c>Land — Mountain Forest</c>.
/// Oracle text (verified against Scryfall):
///   "({T}: Add {R} or {G}.)
///    This land enters tapped.
///    Cycling {2} ({2}, Discard this card: Draw a card.)"
///
/// Covers:
/// - Identity (Land + the printed Mountain and Forest subtypes, CR 205.3i).
/// - Two mana abilities producing {R} and {G} respectively (CR 605.1 — mana
///   abilities don't use the stack).
/// - Cycling {2} ability shape (ManaCostCost("{2}") + DiscardSelfCost via the
///   shared <see cref="Majik.Core.Keywords.CyclingFactory"/> primitive, CR 702.32).
/// - End-to-end cycle: pays {2}, discards self, draws one card, publishes
///   <see cref="Majik.Core.Events.CardCycledEvent"/> when a bus is supplied.
/// - Dispatcher routing through <see cref="NamedCardFactory"/>.
///
/// "This land enters tapped" (CR 614.1c) is applied on the production load
/// path by <see cref="EntersTappedBinder"/> from the oracle text, not by this
/// factory (same posture as the Guildgate / Alpine Meadow factories).
/// </summary>
public class ShelteredThicketFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void ShelteredThicket_Dispatch_ReturnsLandWithMountainAndForestSubtypes()
    {
        var card = NamedCardFactory.Create("Sheltered Thicket", _alice);

        card.Should().BeAssignableTo<Land>();
        card.Name.Should().Be("Sheltered Thicket");
        card.HasSubtype(CardSubtype.Mountain).Should().BeTrue();
        card.HasSubtype(CardSubtype.Forest).Should().BeTrue();
    }

    [Fact]
    public void ShelteredThicket_HasTwoManaAbilities_ProducingRedAndGreen()
    {
        var land = (Land)NamedCardFactory.Create("Sheltered Thicket", _alice);
        var mana = land.Abilities.OfType<ManaAbility>().ToList();

        mana.Should().HaveCount(2, "Sheltered Thicket taps for {R} or {G}");
        mana.Should().Contain(m => m.ManaGenerated.Red == 1);
        mana.Should().Contain(m => m.ManaGenerated.Green == 1);
    }

    [Fact]
    public void ShelteredThicket_HasCyclingActivatedAbility_WithGenericTwoAndDiscardSelfCosts()
    {
        var land = (Land)NamedCardFactory.Create("Sheltered Thicket", _alice);
        var cycling = land.Abilities.OfType<ActivatedAbility>().Should().ContainSingle().Subject;

        cycling.Costs.Should().HaveCount(2, "cycling = mana cost + DiscardSelfCost");
        cycling.Costs.OfType<DiscardSelfCost>().Should().HaveCount(1);

        var manaCost = cycling.Costs.OfType<ManaCostCost>().Single().Cost;
        manaCost.Generic.Should().Be(2, "Sheltered Thicket's cycling cost is {2}");
        manaCost.Red.Should().Be(0);
        manaCost.Green.Should().Be(0);
    }

    [Fact]
    public void ShelteredThicket_HasCyclingKeywordMarker()
    {
        var land = (Land)NamedCardFactory.Create("Sheltered Thicket", _alice);
        land.Abilities.OfType<KeywordAbility>()
            .Should().Contain(k => k.Keyword == "Cycling");
    }

    [Fact]
    public void ShelteredThicket_Cycling_EndToEnd_PaysGenericTwoDiscardsSelfDrawsOne()
    {
        // Seed library so the draw resolves.
        var topCard = new Card("Llanowar Elves", "{G}");
        topCard.SetOwner(_alice);
        _alice.Zones.Library.AddCard(topCard);
        topCard.SetZone(ZoneType.Library);

        var bus = new Majik.Core.Events.EventBus();
        Majik.Core.Events.CardCycledEvent? captured = null;
        bus.Subscribe<Majik.Core.Events.CardCycledEvent>(e => captured = e);

        var thicket = ShelteredThicketFactory.Create(_alice, bus);
        _alice.Zones.Hand.AddCard(thicket);
        thicket.SetZone(ZoneType.Hand);

        // {2} generic — pay with two green.
        _alice.AddManaToPool(ManaCost.Parse("{G}{G}"));

        var cycling = thicket.Abilities.OfType<ActivatedAbility>().Single();
        foreach (var cost in cycling.Costs)
        {
            cost.CanPay(_alice).Should().BeTrue($"{cost.Description}");
            cost.Pay(_alice);
        }
        thicket.Zone.Should().Be(ZoneType.Graveyard, "discarded self");

        foreach (var effect in cycling.Effects) effect.Execute();

        _alice.Zones.Hand.GetCards().Should().Contain(topCard, "cycle drew one card");
        captured.Should().NotBeNull("CR 702.32d publication");
        captured!.Card.Should().BeSameAs(thicket);
    }
}
