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
/// Unit tests for <see cref="SmolderingCraterFactory"/> — the Mirage-style
/// red cycling land.
///
/// Oracle text (verified against Scryfall):
///   "This land enters tapped.
///    {T}: Add {R}.
///    Cycling {2} ({2}, Discard this card: Draw a card.)"
///
/// Distinct from the Onslaught monocolour cycling cycle
/// (<see cref="OnslaughtCyclingLandFactory"/>): Smoldering Crater's cycling
/// cost is generic <c>{2}</c> (CR 702.32), NOT a coloured pip, and it has no
/// printed land subtype.
///
/// Covers:
/// - Identity (Land, no subtype, {T}: Add {R}).
/// - Cycling ability shape (ManaCostCost({2}) + DiscardSelfCost via the
///   shared <see cref="Majik.Core.Keywords.CyclingFactory"/> primitive).
/// - Cycling cost charges 2 generic mana.
/// - End-to-end cycle: pays {2}, discards self, draws one card, publishes
///   <see cref="Majik.Core.Events.CardCycledEvent"/> when a bus is supplied.
/// - Dispatcher routing through <see cref="NamedCardFactory"/>.
/// </summary>
[Trait("Color", "R")]
public class SmolderingCraterFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void SmolderingCrater_IsLand_DispatchesThroughNamedFactory()
    {
        var land = NamedCardFactory.Create("Smoldering Crater", _alice);
        land.Should().BeOfType<Land>();
        land.Name.Should().Be("Smoldering Crater");
    }

    [Fact]
    public void SmolderingCrater_HasManaAbilityProducingRed()
    {
        var land = (Land)NamedCardFactory.Create("Smoldering Crater", _alice);
        var mana = land.Abilities.OfType<ManaAbility>().Should().ContainSingle().Subject;
        mana.ManaGenerated.Red.Should().Be(1, "{T}: Add {R}");
        mana.ManaGenerated.Green.Should().Be(0);
    }

    // -----------------------------------------------------------------------
    // Cycling ability shape — CR 702.32, cost {2}
    // -----------------------------------------------------------------------

    [Fact]
    public void SmolderingCrater_HasCyclingActivatedAbility_WithGenericTwoAndDiscardSelfCosts()
    {
        var land = (Land)NamedCardFactory.Create("Smoldering Crater", _alice);
        var cycling = land.Abilities.OfType<ActivatedAbility>().Should().ContainSingle().Subject;

        cycling.Costs.Should().HaveCount(2, "cycling = mana cost + DiscardSelfCost");
        cycling.Costs.OfType<DiscardSelfCost>().Should().HaveCount(1);

        var manaCost = cycling.Costs.OfType<ManaCostCost>().Single().Cost;
        manaCost.Generic.Should().Be(2, "Smoldering Crater's cycling cost is {2}");
        manaCost.Red.Should().Be(0, "cycling cost is generic, not coloured");
    }

    [Fact]
    public void SmolderingCrater_HasCyclingKeywordMarker()
    {
        var land = (Land)NamedCardFactory.Create("Smoldering Crater", _alice);
        land.Abilities.OfType<KeywordAbility>()
            .Should().Contain(k => k.Keyword == "Cycling");
    }

    // -----------------------------------------------------------------------
    // End-to-end cycling — pays {2}, discards, draws, publishes event
    // -----------------------------------------------------------------------

    [Fact]
    public void SmolderingCrater_Cycling_EndToEnd_PaysTwoDiscardsSelfDrawsOne()
    {
        // Seed library so the draw resolves.
        var topCard = new Card("Lightning Bolt", "{R}");
        topCard.SetOwner(_alice);
        _alice.Zones.Library.AddCard(topCard);
        topCard.SetZone(ZoneType.Library);

        var bus = new Majik.Core.Events.EventBus();
        Majik.Core.Events.CardCycledEvent? captured = null;
        bus.Subscribe<Majik.Core.Events.CardCycledEvent>(e => captured = e);

        var crater = SmolderingCraterFactory.Create(_alice, eventBus: bus, replacements: null);
        _alice.Zones.Hand.AddCard(crater);
        crater.SetZone(ZoneType.Hand);

        _alice.AddManaToPool(ManaCost.Parse("2"));

        var cycling = crater.Abilities.OfType<ActivatedAbility>().Single();
        foreach (var cost in cycling.Costs)
        {
            cost.CanPay(_alice).Should().BeTrue($"{cost.Description}");
            cost.Pay(_alice);
        }
        crater.Zone.Should().Be(ZoneType.Graveyard, "discarded self");

        foreach (var effect in cycling.Effects) effect.Execute();

        _alice.Zones.Hand.GetCards().Should().Contain(topCard, "cycle drew one card");
        captured.Should().NotBeNull("CR 702.32d publication");
        captured!.Card.Should().BeSameAs(crater);
    }
}
