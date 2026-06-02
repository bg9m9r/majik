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
/// Unit tests for <see cref="BloodfellCavesFactory"/> (Khans of Tarkir).
///
/// B/R "life gain land". Oracle text:
///   "This land enters tapped.
///    When this land enters, you gain 1 life.
///    {T}: Add {B} or {R}."
///
/// Same oracle shape as the Theros "Temple" scry-land cycle
/// (<see cref="TempleOfSilenceFactory"/>) — ETB-tapped + an ETB self-trigger
/// + two single-colour mana abilities — except the ETB keyword action is
/// "you gain 1 life" (CR 119.3). Loaded from the embedded JSON definition
/// via <see cref="Majik.Core.CardData.Definitions.CardDefinitionFactory"/>.
///
/// Covers:
/// - Card identity (name, Land type, owner/controller).
/// - Two single-colour mana abilities — {B} and {R} (CR 605.1a).
/// - One battlefield-active ETB triggered ability that gains 1 life.
/// - ETB effect raises the controller's life total by exactly 1.
///
/// Unconditional enters-tapped (CR 614.1c) is applied on the production
/// load path by <see cref="Majik.Core.CardData.EntersTappedBinder"/>, not by
/// this named-card factory — same posture as the cycle.
/// </summary>
[Trait("Color", "C")]
public class BloodfellCavesTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void BloodfellCaves_IsLand_WithCorrectName()
    {
        var land = (Land)NamedCardFactory.Create("Bloodfell Caves", _alice);

        land.Name.Should().Be("Bloodfell Caves");
        land.HasType(CardType.Land).Should().BeTrue();
        land.HasType(CardType.Creature).Should().BeFalse();
        land.HasSupertype(CardSupertype.Basic).Should().BeFalse("Bloodfell Caves is nonbasic");
        land.Owner.Should().BeSameAs(_alice);
        land.Controller.Should().BeSameAs(_alice);
    }
    [Fact]
    public void BloodfellCaves_HasManaAbility_ForBlack()
    {
        var land = (Land)NamedCardFactory.Create("Bloodfell Caves", _alice);

        land.Abilities.OfType<ManaAbility>()
            .Should().ContainSingle(m => m.ManaGenerated.Black == 1 && m.ManaGenerated.Red == 0);
    }

    [Fact]
    public void BloodfellCaves_HasManaAbility_ForRed()
    {
        var land = (Land)NamedCardFactory.Create("Bloodfell Caves", _alice);

        land.Abilities.OfType<ManaAbility>()
            .Should().ContainSingle(m => m.ManaGenerated.Red == 1 && m.ManaGenerated.Black == 0);
    }

    [Fact]
    public void BloodfellCaves_EtbTrigger_IsBattlefieldActive()
    {
        var land = (Land)NamedCardFactory.Create("Bloodfell Caves", _alice);
        var trigger = land.Abilities.OfType<TriggeredAbility>().Single();

        trigger.ActiveZones.Should().Contain(ZoneType.Battlefield);
    }

    [Fact]
    public void BloodfellCaves_EtbEffect_GainsOneLife_ForController()
    {
        var alice = new Player("Alice", 20);
        var land = (Land)NamedCardFactory.Create("Bloodfell Caves", alice);

        var etb = land.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var effect in etb.Effects) effect.Execute();

        alice.LifeTotal.Should().Be(21, "the ETB trigger gains the controller 1 life (CR 119.3)");
    }
}
