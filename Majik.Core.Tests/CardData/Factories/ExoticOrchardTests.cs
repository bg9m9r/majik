using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for <see cref="ExoticOrchardFactory"/> (Conflux).
///
/// Land. Oracle text (verified against Scryfall 2026-06-02):
///   "{T}: Add one mana of any color that a land an opponent controls could
///    produce."
///
/// The land version of Fellwar Stone: five colour-specific
/// <see cref="ManaAbility"/> slots (WUBRG; {C} excluded — colorless is not a
/// colour, CR 105.1), each gated by a <c>canActivateCheck</c> live only while
/// some land an OPPONENT controls could produce that colour (CR 605.1a,
/// recomputed live). Opponents are reached via an injected
/// <c>allPlayersResolver</c>.
///
/// Covers:
/// - Identity (nonbasic Land, name, owner/controller).
/// - <see cref="NamedCardFactory"/> dispatch + five mana abilities.
/// - No resolver / no opponent lands: no slot is active.
/// - Opponent Forest: only {G} active.
/// - Opponent Forest + Island: {G} and {U} active.
/// - Controller's OWN Forest does not enable a slot (opponents only).
/// - Tapping the live ability produces the matching mana + taps the land.
/// - Tapped Exotic Orchard can't activate.
/// </summary>
[Trait("Color", "C")]
public class ExoticOrchardTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    private Func<IReadOnlyList<Player>> AllPlayers() =>
        () => new List<Player> { _alice, _bob };

    private static ManaAbility ColorAbility(Land orchard, string colorSymbol) =>
        orchard.Abilities.OfType<ManaAbility>()
            .Single(a => a.ManaGenerated.ToString() == ManaCost.Parse(colorSymbol).ToString());

    private Land OrchardOnBattlefield(Func<IReadOnlyList<Player>>? resolver = null)
    {
        var orchard = ExoticOrchardFactory.Create(_alice, resolver);
        _alice.Zones.Battlefield.AddCard(orchard);
        orchard.SetZone(ZoneType.Battlefield);
        return orchard;
    }

    private void PutOnBattlefield(Player owner, Land land)
    {
        owner.Zones.Battlefield.AddCard(land);
        land.SetZone(ZoneType.Battlefield);
    }

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void ExoticOrchard_Identity()
    {
        var land = ExoticOrchardFactory.Create(_alice);

        land.Name.Should().Be("Exotic Orchard");
        land.HasType(CardType.Land).Should().BeTrue();
        land.HasSupertype(CardSupertype.Basic).Should().BeFalse("Exotic Orchard is a nonbasic Land");
        land.Owner.Should().BeSameAs(_alice);
        land.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void ExoticOrchard_DispatchesViaNamedFactory()
    {
        var land = (Land)NamedCardFactory.Create("Exotic Orchard", _alice);

        land.Name.Should().Be("Exotic Orchard");
        land.HasType(CardType.Land).Should().BeTrue();
    }

    [Fact]
    public void ExoticOrchard_HasFiveColorManaAbilities_OnePerWUBRG()
    {
        var land = ExoticOrchardFactory.Create(_alice);
        var manas = land.Abilities.OfType<ManaAbility>().ToList();

        manas.Should().HaveCount(5, "one mana ability per colour (WUBRG); {C} excluded");
        manas.Should().ContainSingle(m => m.ManaGenerated.White == 1);
        manas.Should().ContainSingle(m => m.ManaGenerated.Blue == 1);
        manas.Should().ContainSingle(m => m.ManaGenerated.Black == 1);
        manas.Should().ContainSingle(m => m.ManaGenerated.Red == 1);
        manas.Should().ContainSingle(m => m.ManaGenerated.Green == 1);
    }

    // -----------------------------------------------------------------------
    // No resolver / no opponent lands → no slot is active
    // -----------------------------------------------------------------------

    [Fact]
    public void ExoticOrchard_NoResolver_NoSlotActive()
    {
        // Parameterless overload: no opponent visibility → nothing to reflect.
        var orchard = OrchardOnBattlefield();

        foreach (var color in new[] { "W", "U", "B", "R", "G" })
        {
            ColorAbility(orchard, color).CanActivate().Should().BeFalse(
                $"with no resolver there are no visible opponents to produce {color}");
        }
    }

    [Fact]
    public void ExoticOrchard_OpponentHasNoLands_NoSlotActive()
    {
        var orchard = OrchardOnBattlefield(AllPlayers());

        foreach (var color in new[] { "W", "U", "B", "R", "G" })
        {
            ColorAbility(orchard, color).CanActivate().Should().BeFalse(
                $"opponent controls no land that could produce {color}");
        }
    }

    // -----------------------------------------------------------------------
    // Opponent Forest → only {G} producible
    // -----------------------------------------------------------------------

    [Fact]
    public void ExoticOrchard_OpponentForest_OnlyGreenActive()
    {
        var orchard = OrchardOnBattlefield(AllPlayers());
        PutOnBattlefield(_bob, (Land)NamedCardFactory.Create("Forest", _bob));

        ColorAbility(orchard, "G").CanActivate().Should().BeTrue(
            "an opponent's Forest could produce {G}");

        foreach (var color in new[] { "W", "U", "B", "R" })
        {
            ColorAbility(orchard, color).CanActivate().Should().BeFalse(
                $"no land an opponent controls could produce {color}");
        }
    }

    // -----------------------------------------------------------------------
    // Opponent Forest + Island → {G} and {U} producible
    // -----------------------------------------------------------------------

    [Fact]
    public void ExoticOrchard_OpponentForestAndIsland_GreenAndBlueActive()
    {
        var orchard = OrchardOnBattlefield(AllPlayers());
        PutOnBattlefield(_bob, (Land)NamedCardFactory.Create("Forest", _bob));
        PutOnBattlefield(_bob, (Land)NamedCardFactory.Create("Island", _bob));

        ColorAbility(orchard, "G").CanActivate().Should().BeTrue();
        ColorAbility(orchard, "U").CanActivate().Should().BeTrue();

        foreach (var color in new[] { "W", "B", "R" })
        {
            ColorAbility(orchard, color).CanActivate().Should().BeFalse(
                $"no land an opponent controls could produce {color}");
        }
    }

    // -----------------------------------------------------------------------
    // Controller's OWN lands do NOT enable a slot (opponents only)
    // -----------------------------------------------------------------------

    [Fact]
    public void ExoticOrchard_OwnForest_DoesNotEnableGreen()
    {
        var orchard = OrchardOnBattlefield(AllPlayers());
        PutOnBattlefield(_alice, (Land)NamedCardFactory.Create("Forest", _alice));

        ColorAbility(orchard, "G").CanActivate().Should().BeFalse(
            "Exotic Orchard reflects opponents' lands, not your own");
    }

    // -----------------------------------------------------------------------
    // Tapping the live ability produces the matching mana
    // -----------------------------------------------------------------------

    [Fact]
    public void ExoticOrchard_OpponentForest_TapsForGreen()
    {
        var orchard = OrchardOnBattlefield(AllPlayers());
        PutOnBattlefield(_bob, (Land)NamedCardFactory.Create("Forest", _bob));

        var green = ColorAbility(orchard, "G");
        var produced = green.Activate();

        produced.ToString().Should().Be(ManaCost.Parse("G").ToString(),
            "the opponent's Forest makes {G} producible, so Exotic Orchard taps for {G}");
        orchard.IsTapped.Should().BeTrue("{T} is the activation cost");
    }

    // -----------------------------------------------------------------------
    // Tapped Exotic Orchard can't activate even with a valid source
    // -----------------------------------------------------------------------

    [Fact]
    public void ExoticOrchard_Tapped_CannotActivate()
    {
        var orchard = OrchardOnBattlefield(AllPlayers());
        PutOnBattlefield(_bob, (Land)NamedCardFactory.Create("Forest", _bob));
        orchard.Tap();

        ColorAbility(orchard, "G").CanActivate().Should().BeFalse(
            "{T} is part of the cost; a tapped land can't pay it");
    }
}
