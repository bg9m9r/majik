using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="DesertOfTheIndomitableFactory"/> — the Amonkhet
/// "Desert of the Indomitable" tapped cycling land (the green sibling of the
/// Desert of the Fervent cycle). Oracle text verified against Scryfall:
///   "This land enters tapped.
///    {T}: Add {G}.
///    Cycling {1}{G} ({1}{G}, Discard this card: Draw a card.)"
///
/// Covers:
/// - Identity (Land + Desert subtype).
/// - Mana ability shape ({T}: Add {G}, CR 605.1).
/// - Cycling ability shape (ManaCostCost({1}{G}) + DiscardSelfCost via the
///   shared <see cref="CyclingFactory"/> primitive, CR 702.32).
/// - Cycling cost charges {1}{G} specifically.
/// - End-to-end cycle: pays {1}{G}, discards self, draws one,
///   publishes <see cref="Majik.Core.Events.CardCycledEvent"/> (CR 702.32d).
/// - Dispatcher routing through <see cref="NamedCardFactory"/>.
/// </summary>
public class DesertOfTheIndomitableFactoryTests
{
    private const string CardName = "Desert of the Fervent's sibling Desert of the Indomitable";
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void Dispatch_ReturnsDesertLand()
    {
        var card = NamedCardFactory.Create(CardName, _alice);

        card.Should().BeAssignableTo<Land>();
        card.Name.Should().Be(CardName);
        card.HasSubtype(CardSubtype.Desert).Should().BeTrue("Type line: Land — Desert");
    }

    [Fact]
    public void HasManaAbilityProducingGreen()
    {
        var land = (Land)NamedCardFactory.Create(CardName, _alice);
        var mana = land.Abilities.OfType<ManaAbility>().Should().ContainSingle().Subject;
        mana.ManaGenerated.Green.Should().Be(1, "{T}: Add {G}");
    }

    [Fact]
    public void HasCyclingActivatedAbility_WithManaAndDiscardSelfCosts()
    {
        var land = (Land)NamedCardFactory.Create(CardName, _alice);
        var cycling = land.Abilities.OfType<ActivatedAbility>().Should().ContainSingle().Subject;

        cycling.Costs.Should().HaveCount(2, "cycling = mana cost + DiscardSelfCost");
        cycling.Costs.OfType<DiscardSelfCost>().Should().HaveCount(1);

        var manaCost = cycling.Costs.OfType<ManaCostCost>().Single().Cost;
        manaCost.Generic.Should().Be(1, "Cycling {1}{G} charges 1 generic");
        manaCost.Green.Should().Be(1, "Cycling {1}{G} charges 1 green");
    }

    [Fact]
    public void HasCyclingKeywordMarker()
    {
        var land = (Land)NamedCardFactory.Create(CardName, _alice);
        land.Abilities.OfType<KeywordAbility>()
            .Should().Contain(k => k.Keyword == "Cycling");
    }

    [Fact]
    public void Cycling_EndToEnd_PaysOneGreenDiscardsSelfDrawsOne()
    {
        // Seed library so the draw resolves.
        var topCard = new Card("Llanowar Elves", "{G}");
        topCard.SetOwner(_alice);
        _alice.Zones.Library.AddCard(topCard);
        topCard.SetZone(ZoneType.Library);

        var bus = new Majik.Core.Events.EventBus();
        Majik.Core.Events.CardCycledEvent? captured = null;
        bus.Subscribe<Majik.Core.Events.CardCycledEvent>(e => captured = e);

        var desert = DesertOfTheIndomitableFactory.Create(_alice, eventBus: bus, replacements: null);
        _alice.Zones.Hand.AddCard(desert);
        desert.SetZone(ZoneType.Hand);

        // {1}{G}: pay one generic (any) + one green.
        _alice.AddManaToPool(ManaCost.Parse("1G"));

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
