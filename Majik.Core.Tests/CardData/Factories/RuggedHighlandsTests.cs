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
/// Unit tests for <see cref="RuggedHighlandsFactory"/> (Innistrad / "Refuge"
/// gain-life tapland cycle).
///
/// R/G gain-life land. Oracle text:
///   "This land enters tapped.
///    When this land enters, you gain 1 life.
///    {T}: Add {R} or {G}."
///
/// Same oracle shape as <see cref="TempleOfAbandonFactory"/> (R/G tapland with
/// a dual-colour mana ability and a self-ETB trigger); only the ETB keyword
/// action differs — gain 1 life (CR 119) instead of scry 1. Loaded from the
/// embedded JSON definition via
/// <see cref="Majik.Core.CardData.Definitions.CardDefinitionFactory"/>.
///
/// Covers:
/// - Card identity (name, Land type, owner/controller).
/// - Two single-colour mana abilities — {R} and {G} (CR 605.1a).
/// - One battlefield-active ETB triggered ability that gains 1 life.
/// - The ETB effect adds exactly 1 life to the controller.
///
/// Unconditional enters-tapped (CR 614.1c) is applied on the production load
/// path by <see cref="Majik.Core.CardData.EntersTappedBinder"/>, not by this
/// named-card factory — same posture as the temple / scry-land cycle.
/// </summary>
public class RuggedHighlandsTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void RuggedHighlands_IsLand_WithCorrectName()
    {
        var land = (Land)NamedCardFactory.Create("Rugged Highlands", _alice);

        land.Name.Should().Be("Rugged Highlands");
        land.HasType(CardType.Land).Should().BeTrue();
        land.HasType(CardType.Creature).Should().BeFalse();
        land.HasSupertype(CardSupertype.Basic).Should().BeFalse("refuge lands are nonbasic");
        land.Owner.Should().BeSameAs(_alice);
        land.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_RuggedHighlands()
    {
        var card = NamedCardFactory.Create("Rugged Highlands", _alice);

        card.Should().BeOfType<Land>();
        card.Name.Should().Be("Rugged Highlands");
        card.Abilities.OfType<ManaAbility>().Should().HaveCount(2);
        card.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1);
    }

    [Fact]
    public void RuggedHighlands_HasManaAbility_ForRed()
    {
        var land = (Land)NamedCardFactory.Create("Rugged Highlands", _alice);

        land.Abilities.OfType<ManaAbility>()
            .Should().ContainSingle(m => m.ManaGenerated.Red == 1 && m.ManaGenerated.Green == 0);
    }

    [Fact]
    public void RuggedHighlands_HasManaAbility_ForGreen()
    {
        var land = (Land)NamedCardFactory.Create("Rugged Highlands", _alice);

        land.Abilities.OfType<ManaAbility>()
            .Should().ContainSingle(m => m.ManaGenerated.Green == 1 && m.ManaGenerated.Red == 0);
    }

    [Fact]
    public void RuggedHighlands_EtbTrigger_IsBattlefieldActive()
    {
        var land = (Land)NamedCardFactory.Create("Rugged Highlands", _alice);
        var trigger = land.Abilities.OfType<TriggeredAbility>().Single();

        trigger.ActiveZones.Should().Contain(ZoneType.Battlefield);
    }

    [Fact]
    public void RuggedHighlands_EtbEffect_GainsOneLife()
    {
        var alice = new Player("Alice", 20);
        var land = (Land)NamedCardFactory.Create("Rugged Highlands", alice);
        var etb = land.Abilities.OfType<TriggeredAbility>().Single();

        foreach (var effect in etb.Effects) effect.Execute();

        alice.LifeTotal.Should().Be(21, "the ETB trigger gains the controller 1 life (CR 119)");
    }
}
