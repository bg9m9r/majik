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
/// Unit tests for <see cref="DesertOfTheGlorifiedFactory"/> — the Hour of
/// Devastation "Desert of the …" monocolour cycling tap-land cycle member.
///
/// Oracle text (verified against Scryfall):
///   "This land enters tapped.
///    {T}: Add {B}.
///    Cycling {1}{B} ({1}{B}, Discard this card: Draw a card.)"
///
/// Covers:
/// - Identity (Land — Desert subtype).
/// - {T}: Add {B} mana ability (CR 605.1).
/// - Enters-tapped replacement (CR 614.1c) registers on the supplied bus.
/// - Cycling ability shape — ManaCostCost({1}{B}) + DiscardSelfCost via the
///   shared <see cref="Majik.Core.Keywords.CyclingFactory"/> primitive
///   (CR 702.32) and the Cycling keyword marker.
/// - End-to-end cycle: pays {1}{B}, discards self, draws one card,
///   publishes <see cref="Majik.Core.Events.CardCycledEvent"/> (CR 702.32d).
/// - Dispatcher routing through <see cref="NamedCardFactory"/>.
/// </summary>
public class DesertOfTheGlorifiedFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void Dispatch_ReturnsLandWithDesertSubtype()
    {
        var card = NamedCardFactory.Create("Desert of the Glorified", _alice);

        card.Should().BeAssignableTo<Land>();
        card.Name.Should().Be("Desert of the Glorified");
        card.HasSubtype(CardSubtype.Desert).Should().BeTrue("Land — Desert");
    }

    [Fact]
    public void HasManaAbilityProducingBlack()
    {
        var land = (Land)NamedCardFactory.Create("Desert of the Glorified", _alice);
        var mana = land.Abilities.OfType<ManaAbility>().Should().ContainSingle().Subject;
        mana.ManaGenerated.Black.Should().Be(1, "{T}: Add {B}");
    }

    [Fact]
    public void HasCyclingKeywordMarker()
    {
        var land = (Land)NamedCardFactory.Create("Desert of the Glorified", _alice);
        land.Abilities.OfType<KeywordAbility>()
            .Should().Contain(k => k.Keyword == "Cycling");
    }

    [Fact]
    public void Cycling_ChargesOneGenericAndOneBlack()
    {
        // CR 702.32 — Desert of the Glorified's printed cycling cost is {1}{B}.
        var land = (Land)NamedCardFactory.Create("Desert of the Glorified", _alice);
        _alice.Zones.Hand.AddCard(land);
        land.SetZone(ZoneType.Hand);

        var cycling = land.Abilities.OfType<ActivatedAbility>().Single();
        cycling.Costs.Should().HaveCount(2, "cycling = mana cost + DiscardSelfCost");
        cycling.Costs.OfType<DiscardSelfCost>().Should().HaveCount(1);

        var mana = cycling.Costs.OfType<ManaCostCost>().Single().Cost;
        mana.Black.Should().Be(1, "cycling {1}{B} charges 1 {B}");
        mana.Generic.Should().Be(1, "cycling {1}{B} charges 1 generic");
        mana.Blue.Should().Be(0);
    }

    [Fact]
    public void EntersTapped_RegistersReplacementOnBus()
    {
        var bus = new Majik.Core.Events.EventBus();
        var replacements = new ReplacementBus();

        var land = DesertOfTheGlorifiedFactory.Create(_alice, eventBus: bus, replacements: replacements);

        // CR 614.1c — moving to the battlefield should be replaced to enter tapped.
        var intent = new ZoneMoveIntent(land, ZoneType.Hand, ZoneType.Battlefield);
        var replaced = replacements.Apply(intent);
        replaced.Should().NotBeNull();
        replaced!.EntersTapped.Should().BeTrue("This land enters tapped");
    }

    [Fact]
    public void Cycling_EndToEnd_PaysGenericPlusBlackDiscardsSelfDrawsOne()
    {
        var topCard = new Card("Dark Ritual", "{B}");
        topCard.SetOwner(_alice);
        _alice.Zones.Library.AddCard(topCard);
        topCard.SetZone(ZoneType.Library);

        var bus = new Majik.Core.Events.EventBus();
        Majik.Core.Events.CardCycledEvent? captured = null;
        bus.Subscribe<Majik.Core.Events.CardCycledEvent>(e => captured = e);

        var land = DesertOfTheGlorifiedFactory.Create(_alice, eventBus: bus, replacements: null);
        _alice.Zones.Hand.AddCard(land);
        land.SetZone(ZoneType.Hand);

        // {1}{B} — one generic + one black.
        _alice.AddManaToPool(ManaCost.Parse("{1}{B}"));

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
