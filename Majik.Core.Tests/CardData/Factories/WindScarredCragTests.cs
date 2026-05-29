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
/// Unit tests for <see cref="WindScarredCragFactory"/> (Khans of Tarkir
/// "life-gain dual land", a.k.a. the Refuge cycle).
///
/// R/W "gain land". Oracle text (verified against Scryfall):
///   "This land enters tapped.
///    When this land enters, you gain 1 life.
///    {T}: Add {R} or {W}."
///
/// Same oracle shape as the rest of the Refuge cycle
/// (<see cref="TranquilCoveFactory"/>) — a flat "you gain 1 life" ETB keyword
/// action (CR 119.3) and two single-colour mana abilities. Loaded from the
/// embedded JSON definition via
/// <see cref="Majik.Core.CardData.Definitions.CardDefinitionFactory"/>.
///
/// Covers:
/// - Card identity (name, Land type, owner/controller).
/// - Two single-colour mana abilities — {R} and {W} (CR 605.1a).
/// - One battlefield-active ETB triggered ability that gains 1 life.
/// - ETB effect: controller's life total rises by exactly 1 (CR 119.3).
///
/// Unconditional enters-tapped (CR 614.1c) is applied on the production
/// load path by <see cref="Majik.Core.CardData.EntersTappedBinder"/>, not by
/// this named-card factory — same posture as the rest of the cycle.
/// </summary>
public class WindScarredCragTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void WindScarredCrag_IsLand_WithCorrectName()
    {
        var land = WindScarredCragFactory.Create(_alice);

        land.Name.Should().Be("Wind-Scarred Crag");
        land.HasType(CardType.Land).Should().BeTrue();
        land.HasType(CardType.Creature).Should().BeFalse();
        land.HasSupertype(CardSupertype.Basic).Should().BeFalse("Wind-Scarred Crag is nonbasic");
        land.Owner.Should().BeSameAs(_alice);
        land.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_WindScarredCrag()
    {
        var card = NamedCardFactory.Create("Wind-Scarred Crag", _alice);

        card.Should().BeOfType<Land>();
        card.Name.Should().Be("Wind-Scarred Crag");
        card.Abilities.OfType<ManaAbility>().Should().HaveCount(2);
        card.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1);
    }

    [Fact]
    public void WindScarredCrag_HasManaAbility_ForRed()
    {
        var land = WindScarredCragFactory.Create(_alice);

        land.Abilities.OfType<ManaAbility>()
            .Should().ContainSingle(m => m.ManaGenerated.Red == 1 && m.ManaGenerated.White == 0);
    }

    [Fact]
    public void WindScarredCrag_HasManaAbility_ForWhite()
    {
        var land = WindScarredCragFactory.Create(_alice);

        land.Abilities.OfType<ManaAbility>()
            .Should().ContainSingle(m => m.ManaGenerated.White == 1 && m.ManaGenerated.Red == 0);
    }

    [Fact]
    public void WindScarredCrag_EtbTrigger_IsBattlefieldActive()
    {
        var land = WindScarredCragFactory.Create(_alice);
        var trigger = land.Abilities.OfType<TriggeredAbility>().Single();

        trigger.ActiveZones.Should().Contain(ZoneType.Battlefield);
    }

    [Fact]
    public void WindScarredCrag_EtbEffect_GainsExactlyOneLife()
    {
        // CR 119.3 — "you gain 1 life" raises the controller's life total by 1.
        var alice = new Player("Alice", 20);
        var land = WindScarredCragFactory.Create(alice);
        var etb = land.Abilities.OfType<TriggeredAbility>().Single();

        foreach (var effect in etb.Effects) effect.Execute();

        alice.LifeTotal.Should().Be(21, "Wind-Scarred Crag's ETB gains its controller 1 life");
    }
}
