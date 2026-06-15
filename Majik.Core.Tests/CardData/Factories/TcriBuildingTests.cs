using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="TcriBuildingFactory"/> (U/R gain-life tapland).
///
/// Oracle text (verified against Scryfall):
///   "This land enters tapped.
///    When this land enters, you gain 1 life.
///    {T}: Add {U} or {R}."
///
/// Same oracle shape as the Zendikar "Refuge" cycle (e.g.
/// <see cref="AkoumRefugeFactory"/>) — only the produced colours differ.
/// Loaded from the embedded JSON definition via
/// <see cref="Majik.Core.CardData.Definitions.CardDefinitionFactory"/>.
///
/// Covers the card's unique behaviour:
/// - Two single-colour mana abilities — {U} and {R} (CR 605.1a).
/// - One battlefield-active ETB triggered ability that gains 1 life (CR 119.3).
///
/// Unconditional enters-tapped (CR 614.1c) is applied on the production load
/// path by <see cref="Majik.Core.CardData.EntersTappedBinder"/>, not by this
/// named-card factory — same posture as the Refuge cycle.
/// </summary>
[Trait("Color", "U")]
public class TcriBuildingTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void TcriBuilding_HasManaAbility_ForBlue()
    {
        var land = (Land)NamedCardFactory.Create("TCRI Building", _alice);

        land.Abilities.OfType<ManaAbility>()
            .Should().ContainSingle(m => m.ManaGenerated.Blue == 1 && m.ManaGenerated.Red == 0);
    }

    [Fact]
    public void TcriBuilding_HasManaAbility_ForRed()
    {
        var land = (Land)NamedCardFactory.Create("TCRI Building", _alice);

        land.Abilities.OfType<ManaAbility>()
            .Should().ContainSingle(m => m.ManaGenerated.Red == 1 && m.ManaGenerated.Blue == 0);
    }

    [Fact]
    public void TcriBuilding_EtbTrigger_IsBattlefieldActive()
    {
        var land = (Land)NamedCardFactory.Create("TCRI Building", _alice);
        var trigger = land.Abilities.OfType<TriggeredAbility>().Single();

        trigger.ActiveZones.Should().Contain(ZoneType.Battlefield);
    }

    [Fact]
    public void TcriBuilding_EtbEffect_GainsExactlyOneLife()
    {
        // CR 119.3 — "you gain 1 life" raises the controller's life total by 1.
        var alice = new Player("Alice", 20);
        var land = (Land)NamedCardFactory.Create("TCRI Building", alice);
        var etb = land.Abilities.OfType<TriggeredAbility>().Single();

        foreach (var effect in etb.Effects) effect.Execute();

        alice.LifeTotal.Should().Be(21, "TCRI Building's ETB gains its controller 1 life");
    }
}
