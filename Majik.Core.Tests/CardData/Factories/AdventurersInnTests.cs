using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="AdventurersInnFactory"/>.
///
/// "Town" land. Oracle text (verified against Scryfall):
///   "When this land enters, you gain 2 life.
///    {T}: Add {C}."
///
/// Same gain-life-on-ETB shape as the Khans gain-land cycle
/// (<see cref="BloodfellCavesFactory"/>), but it enters <b>untapped</b>,
/// produces only colorless mana ({T}: Add {C}), and gains <b>2</b> life
/// (CR 119.3) instead of 1. Loaded from the embedded JSON definition via
/// <see cref="Majik.Core.CardData.Definitions.CardDefinitionFactory"/>.
///
/// Covers the card's UNIQUE behaviour:
/// - {C}-only mana ability — {T}: Add {C} (CR 605.1a). {C} parses into the
///   Generic slot today (no dedicated Colorless property on ManaCost; mirrors
///   the Encroaching Wastes tap-for-{C} test).
/// - ETB self-trigger that gains the controller exactly 2 life.
/// - Identity: the printed <c>Town</c> subtype (CR 205.3m).
///
/// Dispatch + well-formedness are asserted for every implemented card by
/// CardFactoryContractTests; no *_DispatchesViaNamedCardFactory test here.
/// </summary>
[Trait("Color", "C")]
public class AdventurersInnTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void AdventurersInn_Identity_IsTownLand()
    {
        var land = (Land)NamedCardFactory.Create("Adventurer's Inn", _alice);

        land.Name.Should().Be("Adventurer's Inn");
        land.HasType(CardType.Land).Should().BeTrue();
        land.HasSupertype(CardSupertype.Basic).Should().BeFalse("Adventurer's Inn is nonbasic");
        land.Subtypes.Should().Contain(CardSubtype.Town);
    }

    [Fact]
    public void AdventurersInn_HasColorlessManaAbility_TapsAndProducesC()
    {
        var land = (Land)NamedCardFactory.Create("Adventurer's Inn", _alice);
        _alice.Zones.Battlefield.AddCard(land);
        land.SetZone(ZoneType.Battlefield);

        var manaAbility = land.Abilities.OfType<ManaAbility>().Single();

        manaAbility.CanActivate().Should().BeTrue();
        var produced = manaAbility.Activate();

        // {C} parses into the Generic slot today (no dedicated Colorless
        // property on ManaCost — mirrors Encroaching Wastes' tap-for-{C} test).
        produced.Generic.Should().Be(1);
        produced.White.Should().Be(0);
        produced.Black.Should().Be(0);
        land.IsTapped.Should().BeTrue();
    }

    [Fact]
    public void AdventurersInn_EtbTrigger_IsBattlefieldActive()
    {
        var land = (Land)NamedCardFactory.Create("Adventurer's Inn", _alice);
        var trigger = land.Abilities.OfType<TriggeredAbility>().Single();

        trigger.ActiveZones.Should().Contain(ZoneType.Battlefield);
    }

    [Fact]
    public void AdventurersInn_EtbEffect_GainsTwoLife_ForController()
    {
        var alice = new Player("Alice", 20);
        var land = (Land)NamedCardFactory.Create("Adventurer's Inn", alice);

        var etb = land.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var effect in etb.Effects) effect.Execute();

        alice.LifeTotal.Should().Be(22, "the ETB trigger gains the controller 2 life (CR 119.3)");
    }
}
