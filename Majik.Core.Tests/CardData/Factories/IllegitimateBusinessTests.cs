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
/// Unit tests for <see cref="IllegitimateBusinessFactory"/> (Outlaws of
/// Thunder Junction B/G gain-life tapland).
///
/// Oracle text (verified against Scryfall):
///   "This land enters tapped.
///    When this land enters, you gain 1 life.
///    {T}: Add {B} or {G}."
///
/// Same oracle shape as <see cref="AkoumRefugeFactory"/> (B/R Refuge) — only
/// the produced colours differ ({B}/{G} here). Loaded from the embedded JSON
/// definition via
/// <see cref="Majik.Core.CardData.Definitions.CardDefinitionFactory"/>.
///
/// Covers the card's UNIQUE behaviour:
/// - Two single-colour mana abilities — {B} and {G} (CR 605.1a).
/// - One battlefield-active ETB triggered ability that gains 1 life (CR 119.3).
///
/// Unconditional enters-tapped (CR 614.1c) is applied on the production
/// load path by <see cref="Majik.Core.CardData.EntersTappedBinder"/>, not by
/// this named-card factory — same posture as the rest of the gain-land cycle.
/// Name / type / dispatch well-formedness is covered for every implemented
/// card by CardFactoryContractTests.
/// </summary>
[Trait("Color", "M")]
public class IllegitimateBusinessTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void IllegitimateBusiness_HasManaAbility_ForBlack()
    {
        var land = (Land)NamedCardFactory.Create("Illegitimate Business", _alice);

        land.Abilities.OfType<ManaAbility>()
            .Should().ContainSingle(m => m.ManaGenerated.Black == 1 && m.ManaGenerated.Green == 0);
    }

    [Fact]
    public void IllegitimateBusiness_HasManaAbility_ForGreen()
    {
        var land = (Land)NamedCardFactory.Create("Illegitimate Business", _alice);

        land.Abilities.OfType<ManaAbility>()
            .Should().ContainSingle(m => m.ManaGenerated.Green == 1 && m.ManaGenerated.Black == 0);
    }

    [Fact]
    public void IllegitimateBusiness_EtbTrigger_IsBattlefieldActive()
    {
        var land = (Land)NamedCardFactory.Create("Illegitimate Business", _alice);
        var trigger = land.Abilities.OfType<TriggeredAbility>().Single();

        trigger.ActiveZones.Should().Contain(ZoneType.Battlefield);
    }

    [Fact]
    public void IllegitimateBusiness_EtbEffect_GainsExactlyOneLife()
    {
        // CR 119.3 — "you gain 1 life" raises the controller's life total by 1.
        var alice = new Player("Alice", 20);
        var land = (Land)NamedCardFactory.Create("Illegitimate Business", alice);
        var etb = land.Abilities.OfType<TriggeredAbility>().Single();

        foreach (var effect in etb.Effects) effect.Execute();

        alice.LifeTotal.Should().Be(21, "Illegitimate Business's ETB gains its controller 1 life");
    }
}
