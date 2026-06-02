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
/// Unit tests for <see cref="JetmirsGardenFactory"/> — the Streets of New
/// Capenna "Triome" tri-land Jetmir's Garden.
///
/// Oracle text (verified against the embedded seed):
///   "({T}: Add {R}, {G}, or {W}.)
///    This land enters tapped.
///    Cycling {3} ({3}, Discard this card: Draw a card.)"
///
/// Covers:
/// - Identity: Land with the three printed subtypes (Mountain / Forest /
///   Plains).
/// - Three mana abilities producing {R}, {G}, {W} (CR 605.1 — mana
///   abilities, no stack).
/// - Cycling ability shape (ManaCostCost {3} + DiscardSelfCost via the
///   shared <see cref="Majik.Core.Keywords.CyclingFactory"/> primitive) +
///   the "Cycling" keyword marker (CR 702.32a).
/// - Cycling cost charges 3 generic mana (CR 702.32).
/// - End-to-end cycle: pays {3}, discards self, draws one card, publishes
///   <see cref="Majik.Core.Events.CardCycledEvent"/> (CR 702.32d).
/// - Dispatcher routing through <see cref="NamedCardFactory"/>.
///
/// Enters-tapped (CR 614.1c) is applied on the production load path by
/// <see cref="Majik.Core.CardData.EntersTappedBinder"/> off the oracle text
/// ("This land enters tapped."), not by this factory — same posture as
/// <see cref="HedgeMazeFactory"/> and <see cref="OnslaughtCyclingLandFactory"/>'s
/// shape-only path.
/// </summary>
[Trait("Color", "C")]
public class JetmirsGardenFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    private const string CardName = "Jetmir's Garden";

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------
    [Fact]
    public void JetmirsGarden_HasThreeManaAbilities_ProducingRGW()
    {
        var land = (Land)NamedCardFactory.Create(CardName, _alice);
        var mana = land.Abilities.OfType<ManaAbility>().ToList();

        mana.Should().HaveCount(3, "{T}: Add {R}, {G}, or {W} — one mana ability per colour");
        mana.Should().ContainSingle(m => m.ManaGenerated.Red == 1
            && m.ManaGenerated.Green == 0 && m.ManaGenerated.White == 0);
        mana.Should().ContainSingle(m => m.ManaGenerated.Green == 1
            && m.ManaGenerated.Red == 0 && m.ManaGenerated.White == 0);
        mana.Should().ContainSingle(m => m.ManaGenerated.White == 1
            && m.ManaGenerated.Red == 0 && m.ManaGenerated.Green == 0);
    }

    // -----------------------------------------------------------------------
    // Cycling ability shape — CR 702.32
    // -----------------------------------------------------------------------

    [Fact]
    public void JetmirsGarden_HasCyclingActivatedAbility_WithGenericThreeAndDiscardSelf()
    {
        var land = (Land)NamedCardFactory.Create(CardName, _alice);
        var cycling = land.Abilities.OfType<ActivatedAbility>().Should().ContainSingle().Subject;

        cycling.Costs.Should().HaveCount(2, "cycling = mana cost + DiscardSelfCost");
        cycling.Costs.OfType<DiscardSelfCost>().Should().HaveCount(1);

        var manaCost = cycling.Costs.OfType<ManaCostCost>().Single().Cost;
        manaCost.Generic.Should().Be(3, "Cycling {3} charges 3 generic mana");
        manaCost.White.Should().Be(0);
        manaCost.Red.Should().Be(0);
        manaCost.Green.Should().Be(0);
    }

    [Fact]
    public void JetmirsGarden_HasCyclingKeywordMarker()
    {
        var land = (Land)NamedCardFactory.Create(CardName, _alice);
        land.Abilities.OfType<KeywordAbility>()
            .Should().Contain(k => k.Keyword == "Cycling");
    }

    // -----------------------------------------------------------------------
    // End-to-end cycling — pays {3}, discards, draws, publishes event
    // -----------------------------------------------------------------------

    [Fact]
    public void JetmirsGarden_Cycling_EndToEnd_PaysThreeGenericDiscardsSelfDrawsOne()
    {
        // Seed library so the draw resolves.
        var topCard = new Card("Llanowar Elves", "{G}");
        topCard.SetOwner(_alice);
        _alice.Zones.Library.AddCard(topCard);
        topCard.SetZone(ZoneType.Library);

        var bus = new Majik.Core.Events.EventBus();
        Majik.Core.Events.CardCycledEvent? captured = null;
        bus.Subscribe<Majik.Core.Events.CardCycledEvent>(e => captured = e);

        var garden = JetmirsGardenFactory.Create(_alice, eventBus: bus, replacements: null);
        _alice.Zones.Hand.AddCard(garden);
        garden.SetZone(ZoneType.Hand);

        // 3 generic mana — pay with three green from a basic-ish pool.
        _alice.AddManaToPool(ManaCost.Parse("3"));

        var cycling = garden.Abilities.OfType<ActivatedAbility>().Single();
        foreach (var cost in cycling.Costs)
        {
            cost.CanPay(_alice).Should().BeTrue($"{cost.Description}");
            cost.Pay(_alice);
        }
        garden.Zone.Should().Be(ZoneType.Graveyard, "discarded self");

        foreach (var effect in cycling.Effects) effect.Execute();

        _alice.Zones.Hand.GetCards().Should().Contain(topCard, "cycle drew one card");
        captured.Should().NotBeNull("CR 702.32d publication");
        captured!.Card.Should().BeSameAs(garden);
    }
}
