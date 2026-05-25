using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Unit tests for <see cref="NimbusMazeFactory"/>.
///
/// Land. Oracle text:
///   "{T}: Add {C}.
///    {T}: Add {W}. Activate only if you control a Plains.
///    {T}: Add {U}. Activate only if you control an Island."
/// </summary>
public class NimbusMazeTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Card identity
    // -----------------------------------------------------------------------

    [Fact]
    public void NimbusMaze_IsLand_WithNoBasicSupertype()
    {
        var land = NimbusMazeFactory.Create(_alice);

        land.Name.Should().Be("Nimbus Maze");
        land.HasType(CardType.Land).Should().BeTrue();
        land.HasSupertype(CardSupertype.Basic).Should().BeFalse();
        land.HasSubtype(CardSubtype.Plains).Should().BeFalse(
            "Nimbus Maze is not a Plains itself — doesn't satisfy its own gate");
        land.HasSubtype(CardSubtype.Island).Should().BeFalse(
            "Nimbus Maze is not an Island itself — doesn't satisfy its own gate");
    }

    [Fact]
    public void NimbusMaze_OwnerAndControllerSet()
    {
        var land = NimbusMazeFactory.Create(_alice);

        land.Owner.Should().BeSameAs(_alice);
        land.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_NimbusMaze()
    {
        var land = NamedCardFactory.Create("Nimbus Maze", _alice);

        land.Should().BeOfType<Land>();
        land.Name.Should().Be("Nimbus Maze");
    }

    // -----------------------------------------------------------------------
    // Mana abilities — three: {C}, {W}, {U}
    // -----------------------------------------------------------------------

    [Fact]
    public void NimbusMaze_HasExactlyThreeManaAbilities()
    {
        var land = NimbusMazeFactory.Create(_alice);

        land.Abilities.OfType<ManaAbility>().Should().HaveCount(3,
            "one each for {C}, {W}, {U}");
    }

    [Fact]
    public void NimbusMaze_ColorlessManaAbility_IsAlwaysActivatable_WhenUntapped()
    {
        var land = NimbusMazeFactory.Create(_alice);
        land.SetZone(ZoneType.Battlefield);

        // {C} parses as Generic=1, all coloured pips 0.
        var colorless = land.Abilities.OfType<ManaAbility>()
            .Single(m => m.ManaGenerated.Generic == 1
                && m.ManaGenerated.White == 0
                && m.ManaGenerated.Blue == 0
                && m.ManaGenerated.Black == 0
                && m.ManaGenerated.Red == 0
                && m.ManaGenerated.Green == 0);

        colorless.CanActivate().Should().BeTrue(
            "{C} ability has no gating restriction");
    }

    [Fact]
    public void NimbusMaze_WhiteManaAbility_CanActivate_OnlyWithControlledPlains()
    {
        var land = NimbusMazeFactory.Create(_alice);
        land.SetZone(ZoneType.Battlefield);

        var white = land.Abilities.OfType<ManaAbility>()
            .Single(m => m.ManaGenerated.White == 1);

        white.CanActivate().Should().BeFalse(
            "no Plains under controller → gated off");

        // Add a Plains under Alice's control.
        var plains = NamedCardFactory.Create("Plains", _alice);
        plains.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(plains);

        white.CanActivate().Should().BeTrue(
            "Alice now controls a Plains → gate opens");
    }

    [Fact]
    public void NimbusMaze_BlueManaAbility_CanActivate_OnlyWithControlledIsland()
    {
        var land = NimbusMazeFactory.Create(_alice);
        land.SetZone(ZoneType.Battlefield);

        var blue = land.Abilities.OfType<ManaAbility>()
            .Single(m => m.ManaGenerated.Blue == 1);

        blue.CanActivate().Should().BeFalse(
            "no Island under controller → gated off");

        var island = NamedCardFactory.Create("Island", _alice);
        island.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(island);

        blue.CanActivate().Should().BeTrue(
            "Alice now controls an Island → gate opens");
    }

    [Fact]
    public void NimbusMaze_WhiteAbility_GatedOff_WhenTapped_EvenWithPlains()
    {
        var land = NimbusMazeFactory.Create(_alice);
        land.SetZone(ZoneType.Battlefield);

        var plains = NamedCardFactory.Create("Plains", _alice);
        plains.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(plains);

        var white = land.Abilities.OfType<ManaAbility>()
            .Single(m => m.ManaGenerated.White == 1);

        white.CanActivate().Should().BeTrue();
        land.Tap();
        white.CanActivate().Should().BeFalse(
            "tapped → printed {T} gate fails");
    }

    [Fact]
    public void NimbusMaze_ControllerControlsSubtype_PureHelper()
    {
        var land = NimbusMazeFactory.Create(_alice);
        land.SetZone(ZoneType.Battlefield);

        NimbusMazeFactory.ControllerControlsSubtype(land, CardSubtype.Plains).Should().BeFalse();
        NimbusMazeFactory.ControllerControlsSubtype(land, CardSubtype.Island).Should().BeFalse();

        var plains = NamedCardFactory.Create("Plains", _alice);
        plains.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(plains);

        NimbusMazeFactory.ControllerControlsSubtype(land, CardSubtype.Plains).Should().BeTrue();
        NimbusMazeFactory.ControllerControlsSubtype(land, CardSubtype.Island).Should().BeFalse();
    }

    [Fact]
    public void NimbusMaze_OpponentsPlains_DoNotOpenWhiteGate()
    {
        var bob = new Player("Bob", 20);
        var land = NimbusMazeFactory.Create(_alice);
        land.SetZone(ZoneType.Battlefield);

        // Bob controls a Plains; Alice does not.
        var bobPlains = NamedCardFactory.Create("Plains", bob);
        bobPlains.SetZone(ZoneType.Battlefield);
        bob.Zones.Battlefield.AddCard(bobPlains);

        var white = land.Abilities.OfType<ManaAbility>()
            .Single(m => m.ManaGenerated.White == 1);

        white.CanActivate().Should().BeFalse(
            "Bob's Plains doesn't open Alice's Nimbus Maze gate (controller, not any player)");
    }
}
