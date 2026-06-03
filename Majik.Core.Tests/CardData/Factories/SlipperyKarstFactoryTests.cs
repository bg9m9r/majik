using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Costs;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="SlipperyKarstFactory"/> — Slippery Karst
/// (Urza's Saga). Land.
///
/// Oracle text (verified against Scryfall 2026-06-02):
///   "This land enters tapped.
///    {T}: Add {G}.
///    Cycling {2} ({2}, Discard this card: Draw a card.)"
///
/// NOTE: Slippery Karst is NOT a member of the Onslaught monocolour
/// cycling-land cycle (Tranquil Thicket et al.). Those have Cycling
/// {color}; Slippery Karst has Cycling {2} (generic, CR 702.32) and no
/// printed land subtype — so it routes through a JSON base shape +
/// thin factory rather than the parametric OnslaughtCyclingLandFactory.
///
/// Covers:
/// - Identity (Land, no printed subtype, {T}: Add {G}).
/// - Cycling ability shape (ManaCostCost({2}) + DiscardSelfCost via the
///   shared <see cref="Majik.Core.Keywords.CyclingFactory"/> primitive).
/// - Cycling cost charges 2 generic, not green.
/// - End-to-end cycle: pays {2}, discards self, draws one card, publishes
///   <see cref="Majik.Core.Events.CardCycledEvent"/> when a bus is supplied.
/// - Dispatcher routing through <see cref="NamedCardFactory"/>.
/// </summary>
[Trait("Color", "G")]
public class SlipperyKarstFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void SlipperyKarst_IsLand()
    {
        var land = NamedCardFactory.Create("Slippery Karst", _alice);
        land.Should().BeOfType<Land>();
    }

    [Fact]
    public void SlipperyKarst_HasManaAbilityProducingGreen()
    {
        var land = (Land)NamedCardFactory.Create("Slippery Karst", _alice);
        var mana = land.Abilities.OfType<ManaAbility>().Should().ContainSingle().Subject;
        mana.ManaGenerated.Green.Should().Be(1, "{T}: Add {G}");
        mana.ManaGenerated.Generic.Should().Be(0);
    }

    [Fact]
    public void SlipperyKarst_HasCyclingKeywordMarker()
    {
        var land = (Land)NamedCardFactory.Create("Slippery Karst", _alice);
        land.Abilities.OfType<KeywordAbility>()
            .Should().Contain(k => k.Keyword == "Cycling");
    }

    [Fact]
    public void SlipperyKarst_HasCyclingActivatedAbility_WithGenericManaAndDiscardSelfCosts()
    {
        var land = (Land)NamedCardFactory.Create("Slippery Karst", _alice);
        var cycling = land.Abilities.OfType<ActivatedAbility>().Should().ContainSingle().Subject;

        cycling.Costs.Should().HaveCount(2, "cycling = mana cost + DiscardSelfCost");
        cycling.Costs.OfType<DiscardSelfCost>().Should().HaveCount(1);

        // CR 702.32 — Slippery Karst's printed cycling cost is {2} (generic).
        var manaCost = cycling.Costs.OfType<ManaCostCost>().Single().Cost;
        manaCost.Generic.Should().Be(2, "cycling {2} charges 2 generic mana");
        manaCost.Green.Should().Be(0, "cycling cost is generic, not green");
    }

    [Fact]
    public void SlipperyKarst_Cycling_EndToEnd_PaysTwoGenericDiscardsSelfDrawsOne()
    {
        // Seed library so the draw resolves.
        var topCard = new Card("Llanowar Elves", "{G}");
        topCard.SetOwner(_alice);
        _alice.Zones.Library.AddCard(topCard);
        topCard.SetZone(ZoneType.Library);

        var bus = new Majik.Core.Events.EventBus();
        Majik.Core.Events.CardCycledEvent? captured = null;
        bus.Subscribe<Majik.Core.Events.CardCycledEvent>(e => captured = e);

        var karst = SlipperyKarstFactory.Create(
            _alice,
            eventBus: bus,
            replacements: null);
        _alice.Zones.Hand.AddCard(karst);
        karst.SetZone(ZoneType.Hand);

        // {2} generic — pay with two green to prove generic accepts any colour.
        _alice.AddManaToPool(ManaCost.Parse("GG"));

        var cycling = karst.Abilities.OfType<ActivatedAbility>().Single();
        foreach (var cost in cycling.Costs)
        {
            cost.CanPay(_alice).Should().BeTrue($"{cost.Description}");
            cost.Pay(_alice);
        }
        karst.Zone.Should().Be(ZoneType.Graveyard, "discarded self");

        foreach (var effect in cycling.Effects) effect.Execute();

        _alice.Zones.Hand.GetCards().Should().Contain(topCard, "cycle drew one card");
        captured.Should().NotBeNull("CR 702.32d publication");
        captured!.Card.Should().BeSameAs(karst);
    }
}
