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
/// Unit tests for <see cref="TranquilCoveFactory"/> (Khans of Tarkir
/// "life-gain dual land", a.k.a. the Refuge cycle).
///
/// W/U "gain land". Oracle text (verified against Scryfall):
///   "This land enters tapped.
///    When this land enters, you gain 1 life.
///    {T}: Add {W} or {U}."
///
/// Same oracle shape as the scry-land Temple cycle
/// (<see cref="TempleOfEnlightenmentFactory"/>) — only the ETB keyword
/// action differs: a flat "you gain 1 life" (CR 119.3) instead of scry 1.
/// Loaded from the embedded JSON definition via
/// <see cref="Majik.Core.CardData.Definitions.CardDefinitionFactory"/>.
///
/// Covers:
/// - Card identity (name, Land type, owner/controller).
/// - Two single-colour mana abilities — {W} and {U} (CR 605.1a).
/// - One battlefield-active ETB triggered ability that gains 1 life.
/// - ETB effect: controller's life total rises by exactly 1 (CR 119.3).
///
/// Unconditional enters-tapped (CR 614.1c) is applied on the production
/// load path by <see cref="Majik.Core.CardData.EntersTappedBinder"/>, not by
/// this named-card factory — same posture as the Temple cycle.
/// </summary>
public class TranquilCoveTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void TranquilCove_IsLand_WithCorrectName()
    {
        var land = (Land)NamedCardFactory.Create("Tranquil Cove", _alice);

        land.Name.Should().Be("Tranquil Cove");
        land.HasType(CardType.Land).Should().BeTrue();
        land.HasType(CardType.Creature).Should().BeFalse();
        land.HasSupertype(CardSupertype.Basic).Should().BeFalse("Tranquil Cove is nonbasic");
        land.Owner.Should().BeSameAs(_alice);
        land.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_TranquilCove()
    {
        var card = NamedCardFactory.Create("Tranquil Cove", _alice);

        card.Should().BeOfType<Land>();
        card.Name.Should().Be("Tranquil Cove");
        card.Abilities.OfType<ManaAbility>().Should().HaveCount(2);
        card.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1);
    }

    [Fact]
    public void TranquilCove_HasManaAbility_ForWhite()
    {
        var land = (Land)NamedCardFactory.Create("Tranquil Cove", _alice);

        land.Abilities.OfType<ManaAbility>()
            .Should().ContainSingle(m => m.ManaGenerated.White == 1 && m.ManaGenerated.Blue == 0);
    }

    [Fact]
    public void TranquilCove_HasManaAbility_ForBlue()
    {
        var land = (Land)NamedCardFactory.Create("Tranquil Cove", _alice);

        land.Abilities.OfType<ManaAbility>()
            .Should().ContainSingle(m => m.ManaGenerated.Blue == 1 && m.ManaGenerated.White == 0);
    }

    [Fact]
    public void TranquilCove_EtbTrigger_IsBattlefieldActive()
    {
        var land = (Land)NamedCardFactory.Create("Tranquil Cove", _alice);
        var trigger = land.Abilities.OfType<TriggeredAbility>().Single();

        trigger.ActiveZones.Should().Contain(ZoneType.Battlefield);
    }

    [Fact]
    public void TranquilCove_EtbEffect_GainsExactlyOneLife()
    {
        // CR 119.3 — "you gain 1 life" raises the controller's life total by 1.
        var alice = new Player("Alice", 20);
        var land = (Land)NamedCardFactory.Create("Tranquil Cove", alice);
        var etb = land.Abilities.OfType<TriggeredAbility>().Single();

        foreach (var effect in etb.Effects) effect.Execute();

        alice.LifeTotal.Should().Be(21, "Tranquil Cove's ETB gains its controller 1 life");
    }
}
