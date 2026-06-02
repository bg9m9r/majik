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
/// Unit tests for <see cref="DriftingMeadowFactory"/> — Drifting Meadow
/// (Urza's Saga monocolour cycling-tapland cycle). Oracle text (verified
/// against Scryfall):
///
/// <code>
/// This land enters tapped.
/// {T}: Add {W}.
/// Cycling {2} ({2}, Discard this card: Draw a card.)
/// </code>
///
/// Type line: <c>Land</c> (no printed subtype). Mirrors the
/// <see cref="ScatteredGrovesFactory"/> shape (tapped land + Cycling {2})
/// but produces a single colour ({W}) and carries no land subtype.
///
/// Covers:
/// - Identity (Land, no subtype) + {T}: Add {W} mana ability.
/// - Cycling ability shape (ManaCostCost("2") + DiscardSelfCost via the
///   shared <see cref="Majik.Core.Keywords.CyclingFactory"/> primitive).
/// - End-to-end cycle: pays {2}, discards self, draws one card, publishes
///   <see cref="Majik.Core.Events.CardCycledEvent"/> when a bus is supplied.
/// - Dispatcher routing through <see cref="NamedCardFactory"/>.
/// </summary>
[Trait("Color", "W")]
public class DriftingMeadowFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void DriftingMeadow_IsLandWithNoSubtype()
    {
        var land = (Land)NamedCardFactory.Create("Drifting Meadow", _alice);

        land.Name.Should().Be("Drifting Meadow");
        land.Subtypes.Should().BeEmpty("type line is just 'Land' — no printed subtype");
    }

    [Fact]
    public void DriftingMeadow_HasManaAbilityProducingWhite()
    {
        var land = (Land)NamedCardFactory.Create("Drifting Meadow", _alice);
        var mana = land.Abilities.OfType<ManaAbility>().Should().ContainSingle().Subject;
        mana.ManaGenerated.White.Should().Be(1, "{T}: Add {W}");
    }

    // -----------------------------------------------------------------------
    // Cycling ability shape — CR 702.32
    // -----------------------------------------------------------------------

    [Fact]
    public void DriftingMeadow_HasCyclingActivatedAbility_WithGenericManaAndDiscardSelfCosts()
    {
        var land = (Land)NamedCardFactory.Create("Drifting Meadow", _alice);
        var cycling = land.Abilities.OfType<ActivatedAbility>().Should().ContainSingle().Subject;

        cycling.Costs.Should().HaveCount(2, "cycling = mana cost + DiscardSelfCost");
        cycling.Costs.OfType<DiscardSelfCost>().Should().HaveCount(1);

        var manaCost = cycling.Costs.OfType<ManaCostCost>().Single().Cost;
        manaCost.Generic.Should().Be(2, "Cycling {2} charges 2 generic mana");
        manaCost.White.Should().Be(0);
    }

    [Fact]
    public void DriftingMeadow_HasCyclingKeywordMarker()
    {
        var land = (Land)NamedCardFactory.Create("Drifting Meadow", _alice);
        land.Abilities.OfType<KeywordAbility>()
            .Should().Contain(k => k.Keyword == "Cycling");
    }

    // -----------------------------------------------------------------------
    // End-to-end cycling — pays {2}, discards, draws, publishes event
    // -----------------------------------------------------------------------

    [Fact]
    public void DriftingMeadow_Cycling_EndToEnd_PaysTwoGenericDiscardsSelfDrawsOne()
    {
        // Seed library so the draw resolves.
        var topCard = new Card("Savannah Lions", "{W}");
        topCard.SetOwner(_alice);
        _alice.Zones.Library.AddCard(topCard);
        topCard.SetZone(ZoneType.Library);

        var bus = new Majik.Core.Events.EventBus();
        Majik.Core.Events.CardCycledEvent? captured = null;
        bus.Subscribe<Majik.Core.Events.CardCycledEvent>(e => captured = e);

        var meadow = DriftingMeadowFactory.Create(
            _alice,
            eventBus: bus,
            replacements: null);
        _alice.Zones.Hand.AddCard(meadow);
        meadow.SetZone(ZoneType.Hand);

        // Cycling {2} — pay 2 generic mana.
        _alice.AddManaToPool(ManaCost.Parse("2"));

        var cycling = meadow.Abilities.OfType<ActivatedAbility>().Single();
        foreach (var cost in cycling.Costs)
        {
            cost.CanPay(_alice).Should().BeTrue($"{cost.Description}");
            cost.Pay(_alice);
        }
        meadow.Zone.Should().Be(ZoneType.Graveyard, "discarded self");

        foreach (var effect in cycling.Effects) effect.Execute();

        _alice.Zones.Hand.GetCards().Should().Contain(topCard, "cycle drew one card");
        captured.Should().NotBeNull("CR 702.32d publication");
        captured!.Card.Should().BeSameAs(meadow);
    }
}
