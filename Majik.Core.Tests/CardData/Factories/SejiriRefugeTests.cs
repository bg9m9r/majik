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
/// Unit tests for <see cref="SejiriRefugeFactory"/> (Zendikar).
///
/// W/U "Refuge" life-gain tapland. Oracle text:
///   "This land enters tapped.
///    When this land enters, you gain 1 life.
///    {T}: Add {W} or {U}."
///
/// Same oracle shape as the Theros scry-land
/// (<see cref="TempleOfTriumphFactory"/>) and the Murders at Karlov Manor
/// surveil-land cycle (<see cref="CommercialDistrictFactory"/>), only the
/// ETB keyword action is "gain 1 life" (CR 119.3) instead of scry/surveil.
/// Loaded from the embedded JSON definition via
/// <see cref="Majik.Core.CardData.Definitions.CardDefinitionFactory"/>.
///
/// Covers:
/// - Card identity (name, Land type, owner/controller).
/// - Two single-colour mana abilities — {W} and {U} (CR 605.1a).
/// - One battlefield-active ETB triggered ability that gains 1 life.
/// - ETB effect gains exactly 1 life for the controller.
///
/// Unconditional enters-tapped (CR 614.1c) is applied on the production
/// load path by <see cref="Majik.Core.CardData.EntersTappedBinder"/>, not by
/// this named-card factory — same posture as the scry-land / surveil-land cycle.
/// </summary>
public class SejiriRefugeTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void SejiriRefuge_IsLand_WithCorrectName()
    {
        var land = (Land)NamedCardFactory.Create("Sejiri Refuge", _alice);

        land.Name.Should().Be("Sejiri Refuge");
        land.HasType(CardType.Land).Should().BeTrue();
        land.HasType(CardType.Creature).Should().BeFalse();
        land.Owner.Should().BeSameAs(_alice);
        land.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_SejiriRefuge()
    {
        var card = NamedCardFactory.Create("Sejiri Refuge", _alice);

        card.Should().BeOfType<Land>();
        card.Name.Should().Be("Sejiri Refuge");
        card.Abilities.OfType<ManaAbility>().Should().HaveCount(2);
        card.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1);
    }

    [Fact]
    public void SejiriRefuge_HasManaAbility_ForWhite()
    {
        var land = (Land)NamedCardFactory.Create("Sejiri Refuge", _alice);

        land.Abilities.OfType<ManaAbility>()
            .Should().ContainSingle(m => m.ManaGenerated.White == 1 && m.ManaGenerated.Blue == 0);
    }

    [Fact]
    public void SejiriRefuge_HasManaAbility_ForBlue()
    {
        var land = (Land)NamedCardFactory.Create("Sejiri Refuge", _alice);

        land.Abilities.OfType<ManaAbility>()
            .Should().ContainSingle(m => m.ManaGenerated.Blue == 1 && m.ManaGenerated.White == 0);
    }

    [Fact]
    public void SejiriRefuge_EtbTrigger_IsBattlefieldActive()
    {
        var land = (Land)NamedCardFactory.Create("Sejiri Refuge", _alice);
        var trigger = land.Abilities.OfType<TriggeredAbility>().Single();

        trigger.ActiveZones.Should().Contain(ZoneType.Battlefield);
    }

    [Fact]
    public void SejiriRefuge_EtbEffect_GainsOneLife()
    {
        var alice = new Player("Alice", 20);
        var land = (Land)NamedCardFactory.Create("Sejiri Refuge", alice);
        var etb = land.Abilities.OfType<TriggeredAbility>().Single();

        foreach (var effect in etb.Effects) effect.Execute();

        // CR 119.3 — controller's life total increases by 1.
        alice.LifeTotal.Should().Be(21);
    }
}
