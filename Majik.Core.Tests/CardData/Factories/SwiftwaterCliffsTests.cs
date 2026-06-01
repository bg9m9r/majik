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
/// Unit tests for <see cref="SwiftwaterCliffsFactory"/> (Khans of Tarkir
/// gain-life tapland / Innistrad "Refuge" cycle).
///
/// U/R gain-life land. Oracle text:
///   "This land enters tapped.
///    When this land enters, you gain 1 life.
///    {T}: Add {U} or {R}."
///
/// Same oracle shape as <see cref="RuggedHighlandsFactory"/> (dual-colour mana
/// ability + a self-ETB gain-1-life trigger, CR 119); only the produced
/// colours differ — {U}/{R} instead of {R}/{G}. Loaded from the embedded JSON
/// definition via
/// <see cref="Majik.Core.CardData.Definitions.CardDefinitionFactory"/>.
///
/// Covers:
/// - Card identity (name, Land type, owner/controller).
/// - Two single-colour mana abilities — {U} and {R} (CR 605.1a).
/// - One battlefield-active ETB triggered ability that gains 1 life.
/// - The ETB effect adds exactly 1 life to the controller.
///
/// Unconditional enters-tapped (CR 614.1c) is applied on the production load
/// path by <see cref="Majik.Core.CardData.EntersTappedBinder"/>, not by this
/// named-card factory — same posture as the refuge-land cycle.
/// </summary>
public class SwiftwaterCliffsTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void SwiftwaterCliffs_IsLand_WithCorrectName()
    {
        var land = (Land)NamedCardFactory.Create("Swiftwater Cliffs", _alice);

        land.Name.Should().Be("Swiftwater Cliffs");
        land.HasType(CardType.Land).Should().BeTrue();
        land.HasType(CardType.Creature).Should().BeFalse();
        land.HasSupertype(CardSupertype.Basic).Should().BeFalse("refuge lands are nonbasic");
        land.Owner.Should().BeSameAs(_alice);
        land.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_SwiftwaterCliffs()
    {
        var card = NamedCardFactory.Create("Swiftwater Cliffs", _alice);

        card.Should().BeOfType<Land>();
        card.Name.Should().Be("Swiftwater Cliffs");
        card.Abilities.OfType<ManaAbility>().Should().HaveCount(2);
        card.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1);
    }

    [Fact]
    public void SwiftwaterCliffs_HasManaAbility_ForBlue()
    {
        var land = (Land)NamedCardFactory.Create("Swiftwater Cliffs", _alice);

        land.Abilities.OfType<ManaAbility>()
            .Should().ContainSingle(m => m.ManaGenerated.Blue == 1 && m.ManaGenerated.Red == 0);
    }

    [Fact]
    public void SwiftwaterCliffs_HasManaAbility_ForRed()
    {
        var land = (Land)NamedCardFactory.Create("Swiftwater Cliffs", _alice);

        land.Abilities.OfType<ManaAbility>()
            .Should().ContainSingle(m => m.ManaGenerated.Red == 1 && m.ManaGenerated.Blue == 0);
    }

    [Fact]
    public void SwiftwaterCliffs_EtbTrigger_IsBattlefieldActive()
    {
        var land = (Land)NamedCardFactory.Create("Swiftwater Cliffs", _alice);
        var trigger = land.Abilities.OfType<TriggeredAbility>().Single();

        trigger.ActiveZones.Should().Contain(ZoneType.Battlefield);
    }

    [Fact]
    public void SwiftwaterCliffs_EtbEffect_GainsOneLife()
    {
        var alice = new Player("Alice", 20);
        var land = (Land)NamedCardFactory.Create("Swiftwater Cliffs", alice);
        var etb = land.Abilities.OfType<TriggeredAbility>().Single();

        foreach (var effect in etb.Effects) effect.Execute();

        alice.LifeTotal.Should().Be(21, "the ETB trigger gains the controller 1 life (CR 119)");
    }
}
