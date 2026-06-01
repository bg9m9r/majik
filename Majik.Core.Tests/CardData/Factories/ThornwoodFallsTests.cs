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
/// Unit tests for <see cref="ThornwoodFallsFactory"/> (Khans of Tarkir /
/// "Refuge" gain-life tapland cycle).
///
/// G/U gain-life land. Oracle text:
///   "This land enters tapped.
///    When this land enters, you gain 1 life.
///    {T}: Add {G} or {U}."
///
/// Same oracle shape as <see cref="RuggedHighlandsFactory"/> (refuge tapland
/// with a dual-colour mana ability and a self-ETB gain-life trigger); only the
/// colour pair differs — {G}/{U} instead of {R}/{G}. Loaded from the embedded
/// JSON definition via
/// <see cref="Majik.Core.CardData.Definitions.CardDefinitionFactory"/>.
///
/// Covers:
/// - Card identity (name, Land type, owner/controller).
/// - Two single-colour mana abilities — {G} and {U} (CR 605.1a).
/// - One battlefield-active ETB triggered ability that gains 1 life.
/// - The ETB effect adds exactly 1 life to the controller (CR 119).
///
/// Unconditional enters-tapped (CR 614.1c) is applied on the production load
/// path by <see cref="Majik.Core.CardData.EntersTappedBinder"/>, not by this
/// named-card factory — same posture as the rest of the refuge cycle.
/// </summary>
public class ThornwoodFallsTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void ThornwoodFalls_IsLand_WithCorrectName()
    {
        var land = (Land)NamedCardFactory.Create("Thornwood Falls", _alice);

        land.Name.Should().Be("Thornwood Falls");
        land.HasType(CardType.Land).Should().BeTrue();
        land.HasType(CardType.Creature).Should().BeFalse();
        land.HasSupertype(CardSupertype.Basic).Should().BeFalse("refuge lands are nonbasic");
        land.Owner.Should().BeSameAs(_alice);
        land.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_ThornwoodFalls()
    {
        var card = NamedCardFactory.Create("Thornwood Falls", _alice);

        card.Should().BeOfType<Land>();
        card.Name.Should().Be("Thornwood Falls");
        card.Abilities.OfType<ManaAbility>().Should().HaveCount(2);
        card.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1);
    }

    [Fact]
    public void ThornwoodFalls_HasManaAbility_ForGreen()
    {
        var land = (Land)NamedCardFactory.Create("Thornwood Falls", _alice);

        land.Abilities.OfType<ManaAbility>()
            .Should().ContainSingle(m => m.ManaGenerated.Green == 1 && m.ManaGenerated.Blue == 0);
    }

    [Fact]
    public void ThornwoodFalls_HasManaAbility_ForBlue()
    {
        var land = (Land)NamedCardFactory.Create("Thornwood Falls", _alice);

        land.Abilities.OfType<ManaAbility>()
            .Should().ContainSingle(m => m.ManaGenerated.Blue == 1 && m.ManaGenerated.Green == 0);
    }

    [Fact]
    public void ThornwoodFalls_EtbTrigger_IsBattlefieldActive()
    {
        var land = (Land)NamedCardFactory.Create("Thornwood Falls", _alice);
        var trigger = land.Abilities.OfType<TriggeredAbility>().Single();

        trigger.ActiveZones.Should().Contain(ZoneType.Battlefield);
    }

    [Fact]
    public void ThornwoodFalls_EtbEffect_GainsOneLife()
    {
        var alice = new Player("Alice", 20);
        var land = (Land)NamedCardFactory.Create("Thornwood Falls", alice);
        var etb = land.Abilities.OfType<TriggeredAbility>().Single();

        foreach (var effect in etb.Effects) effect.Execute();

        alice.LifeTotal.Should().Be(21, "the ETB trigger gains the controller 1 life (CR 119)");
    }
}
