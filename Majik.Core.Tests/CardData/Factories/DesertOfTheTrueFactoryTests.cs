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
/// Unit tests for <see cref="DesertOfTheTrueFactory"/> — the Amonkhet
/// monowhite cycling Desert. Oracle text (verified against Scryfall):
///   "This land enters tapped.
///    {T}: Add {W}.
///    Cycling {1}{W} ({1}{W}, Discard this card: Draw a card.)"
///
/// Covers:
/// - Identity (Land + printed Desert land subtype, CR 305.6).
/// - One mana ability producing {W} (CR 605.1).
/// - Cycling ability shape (ManaCostCost {1}{W} + DiscardSelfCost via the
///   shared <see cref="Majik.Core.Keywords.CyclingFactory"/> primitive,
///   CR 702.32).
/// - End-to-end cycling: pays {1}{W}, discards self, draws one card,
///   publishes <see cref="Majik.Core.Events.CardCycledEvent"/> (CR 702.32d).
/// - Dispatcher routing through <see cref="NamedCardFactory"/>.
/// </summary>
public class DesertOfTheTrueFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void DesertOfTheTrue_Dispatch_ReturnsLandWithDesertSubtype()
    {
        var card = NamedCardFactory.Create("Desert of the True", _alice);

        card.Should().BeAssignableTo<Land>();
        card.Name.Should().Be("Desert of the True");
        card.HasSubtype(CardSubtype.Desert).Should().BeTrue("the printed Desert land subtype");
    }

    [Fact]
    public void DesertOfTheTrue_HasManaAbilityProducingWhite()
    {
        var land = (Land)NamedCardFactory.Create("Desert of the True", _alice);
        var mana = land.Abilities.OfType<ManaAbility>().Should().ContainSingle().Subject;
        mana.ManaGenerated.White.Should().Be(1, "{T}: Add {W}");
    }

    // -----------------------------------------------------------------------
    // Cycling ability shape — CR 702.32
    // -----------------------------------------------------------------------

    [Fact]
    public void DesertOfTheTrue_HasCyclingActivatedAbility_WithGenericOnePlusWhiteAndDiscardSelf()
    {
        var land = (Land)NamedCardFactory.Create("Desert of the True", _alice);
        var cycling = land.Abilities.OfType<ActivatedAbility>().Should().ContainSingle().Subject;

        cycling.Costs.Should().HaveCount(2, "cycling = {1}{W} mana cost + DiscardSelfCost");
        cycling.Costs.OfType<DiscardSelfCost>().Should().HaveCount(1);

        var manaCost = cycling.Costs.OfType<ManaCostCost>().Single().Cost;
        manaCost.Generic.Should().Be(1, "Cycling {1}{W} charges 1 generic mana");
        manaCost.White.Should().Be(1, "Cycling {1}{W} charges 1 white mana");
        manaCost.Blue.Should().Be(0);
        manaCost.Black.Should().Be(0);
        manaCost.Red.Should().Be(0);
        manaCost.Green.Should().Be(0);
    }

    [Fact]
    public void DesertOfTheTrue_HasCyclingKeywordMarker()
    {
        var land = (Land)NamedCardFactory.Create("Desert of the True", _alice);
        land.Abilities.OfType<KeywordAbility>()
            .Should().Contain(k => k.Keyword == "Cycling");
    }

    // -----------------------------------------------------------------------
    // End-to-end cycling — pays {1}{W}, discards, draws, publishes event
    // -----------------------------------------------------------------------

    [Fact]
    public void DesertOfTheTrue_Cycling_EndToEnd_PaysOneWhiteDiscardsSelfDrawsOne()
    {
        var topCard = new Card("Savannah Lions", "{W}");
        topCard.SetOwner(_alice);
        _alice.Zones.Library.AddCard(topCard);
        topCard.SetZone(ZoneType.Library);

        var bus = new Majik.Core.Events.EventBus();
        Majik.Core.Events.CardCycledEvent? captured = null;
        bus.Subscribe<Majik.Core.Events.CardCycledEvent>(e => captured = e);

        var desert = DesertOfTheTrueFactory.Create(_alice, eventBus: bus);
        _alice.Zones.Hand.AddCard(desert);
        desert.SetZone(ZoneType.Hand);

        _alice.AddManaToPool(ManaCost.Parse("{1}{W}"));

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
