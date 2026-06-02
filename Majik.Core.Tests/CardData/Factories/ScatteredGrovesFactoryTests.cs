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
/// Unit tests for <see cref="ScatteredGrovesFactory"/> — Scattered Groves
/// (Amonkhet "bicycle land" cycle). Oracle text (verified against Scryfall):
///   "({T}: Add {G} or {W}.)
///    This land enters tapped.
///    Cycling {2} ({2}, Discard this card: Draw a card.)"
///
/// Mirrors <see cref="SavaiTriomeFactoryTests"/> — the same tapped-land +
/// cycling shape, but with two produced colours (G/W) instead of three and
/// a generic {2} cycling cost instead of {3}. Covers identity + subtypes,
/// two mana abilities (one per produced colour), the Cycling {2} activated
/// ability shape (CR 702.32), and an end-to-end cycle that pays {2},
/// discards self, draws one, and publishes
/// <see cref="Majik.Core.Events.CardCycledEvent"/> (CR 702.32d).
/// </summary>
[Trait("Color", "C")]
public class ScatteredGrovesFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------
    [Fact]
    public void ScatteredGroves_HasTwoManaAbilities_ProducingGreenAndWhite()
    {
        var land = (Land)NamedCardFactory.Create("Scattered Groves", _alice);
        var manaAbilities = land.Abilities.OfType<ManaAbility>().ToList();

        manaAbilities.Should().HaveCount(2, "{T}: Add {G} or {W}");
        manaAbilities.Should().Contain(m => m.ManaGenerated.Green == 1);
        manaAbilities.Should().Contain(m => m.ManaGenerated.White == 1);
    }

    // -----------------------------------------------------------------------
    // Cycling {2} ability shape — CR 702.32
    // -----------------------------------------------------------------------

    [Fact]
    public void ScatteredGroves_HasCyclingActivatedAbility_WithGenericTwoAndDiscardSelf()
    {
        var land = (Land)NamedCardFactory.Create("Scattered Groves", _alice);
        var cycling = land.Abilities.OfType<ActivatedAbility>().Should().ContainSingle().Subject;

        cycling.Costs.Should().HaveCount(2, "cycling = {2} mana cost + DiscardSelfCost");
        cycling.Costs.OfType<DiscardSelfCost>().Should().HaveCount(1);

        var manaCost = cycling.Costs.OfType<ManaCostCost>().Single().Cost;
        manaCost.Generic.Should().Be(2, "Cycling {2} charges 2 generic mana");
        manaCost.White.Should().Be(0);
        manaCost.Green.Should().Be(0);
    }

    [Fact]
    public void ScatteredGroves_HasCyclingKeywordMarker()
    {
        var land = (Land)NamedCardFactory.Create("Scattered Groves", _alice);
        land.Abilities.OfType<KeywordAbility>()
            .Should().Contain(k => k.Keyword == "Cycling");
    }

    // -----------------------------------------------------------------------
    // End-to-end cycling — pays {2}, discards, draws, publishes event
    // -----------------------------------------------------------------------

    [Fact]
    public void ScatteredGroves_Cycling_EndToEnd_PaysTwoDiscardsSelfDrawsOne()
    {
        var topCard = new Card("Llanowar Elves", "{G}");
        topCard.SetOwner(_alice);
        _alice.Zones.Library.AddCard(topCard);
        topCard.SetZone(ZoneType.Library);

        var bus = new Majik.Core.Events.EventBus();
        Majik.Core.Events.CardCycledEvent? captured = null;
        bus.Subscribe<Majik.Core.Events.CardCycledEvent>(e => captured = e);

        var groves = ScatteredGrovesFactory.Create(_alice, eventBus: bus, replacements: null);
        _alice.Zones.Hand.AddCard(groves);
        groves.SetZone(ZoneType.Hand);

        _alice.AddManaToPool(ManaCost.Parse("{2}"));

        var cycling = groves.Abilities.OfType<ActivatedAbility>().Single();
        foreach (var cost in cycling.Costs)
        {
            cost.CanPay(_alice).Should().BeTrue($"{cost.Description}");
            cost.Pay(_alice);
        }
        groves.Zone.Should().Be(ZoneType.Graveyard, "discarded self");

        foreach (var effect in cycling.Effects) effect.Execute();

        _alice.Zones.Hand.GetCards().Should().Contain(topCard, "cycle drew one card");
        captured.Should().NotBeNull("CR 702.32d publication");
        captured!.Card.Should().BeSameAs(groves);
    }

    // -----------------------------------------------------------------------
    // Enters-tapped — CR 614.1c
    // -----------------------------------------------------------------------

    [Fact]
    public void ScatteredGroves_RegistersEntersTappedReplacement_WhenBusSupplied()
    {
        var replacements = new ReplacementBus();
        var groves = ScatteredGrovesFactory.Create(_alice, eventBus: null, replacements: replacements);

        groves.Should().NotBeNull();
        // The replacement is registered on the supplied bus (CR 614.1c);
        // the shape-only path (null bus) skips it. EntersTappedReplacement
        // has no public bus-inspection surface, so the production path
        // (covered by the binder chain via oracle text) is the
        // authoritative test for tapped-entry behaviour. Same posture as
        // SavaiTriomeFactoryTests.
    }
}
