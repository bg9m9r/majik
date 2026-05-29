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
/// Tests for <see cref="FellwarStoneFactory"/> (Fallen Empires, {2}).
///
/// Artifact. Oracle text (verified against Scryfall 2026-05-29):
///   "{T}: Add one mana of any color that a land an opponent controls could
///    produce."
///
/// The opponent-facing twin of Reflecting Pool / Star Compass: five
/// colour-specific <see cref="ManaAbility"/> slots (WUBRG; {C} excluded —
/// colorless is not a colour, CR 105.1), each gated by a
/// <c>canActivateCheck</c> live only while some land an OPPONENT controls
/// could produce that colour (CR 605.1a, recomputed live). Opponents are
/// reached via an injected <c>allPlayersResolver</c>.
///
/// Covers:
/// - Identity (Artifact, {2}, owner/controller).
/// - <see cref="NamedCardFactory"/> dispatch + five mana abilities.
/// - No resolver / no opponent lands: no slot is active.
/// - Opponent Forest: only {G} active.
/// - Opponent Forest + Island: {G} and {U} active.
/// - Controller's OWN Forest does not enable a slot (opponents only).
/// - Tapping the live ability produces the matching mana + taps the stone.
/// - Tapped stone can't activate.
/// </summary>
public class FellwarStoneTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    private Func<IReadOnlyList<Player>> AllPlayers() =>
        () => new List<Player> { _alice, _bob };

    private static ManaAbility ColorAbility(Artifact stone, string colorSymbol) =>
        stone.Abilities.OfType<ManaAbility>()
            .Single(a => a.ManaGenerated.ToString() == ManaCost.Parse(colorSymbol).ToString());

    private Artifact StoneOnBattlefield(Func<IReadOnlyList<Player>>? resolver = null)
    {
        var stone = FellwarStoneFactory.Create(_alice, resolver);
        _alice.Zones.Battlefield.AddCard(stone);
        stone.SetZone(ZoneType.Battlefield);
        return stone;
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
    public void FellwarStone_Identity()
    {
        var stone = FellwarStoneFactory.Create(_alice);

        stone.Name.Should().Be("Fellwar Stone");
        stone.HasType(CardType.Artifact).Should().BeTrue();
        stone.ManaCost.Should().Be("{2}");
        stone.Owner.Should().BeSameAs(_alice);
        stone.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void FellwarStone_HasFiveColorManaAbilities_OnePerWUBRG()
    {
        var stone = FellwarStoneFactory.Create(_alice);
        var manas = stone.Abilities.OfType<ManaAbility>().ToList();

        manas.Should().HaveCount(5, "one mana ability per colour (WUBRG); {C} excluded");
        manas.Should().ContainSingle(m => m.ManaGenerated.White == 1);
        manas.Should().ContainSingle(m => m.ManaGenerated.Blue == 1);
        manas.Should().ContainSingle(m => m.ManaGenerated.Black == 1);
        manas.Should().ContainSingle(m => m.ManaGenerated.Red == 1);
        manas.Should().ContainSingle(m => m.ManaGenerated.Green == 1);
    }

    [Fact]
    public void FellwarStone_DispatchesViaNamedCardFactory()
    {
        var card = NamedCardFactory.Create("Fellwar Stone", _alice);

        card.Should().BeOfType<Artifact>();
        card.Name.Should().Be("Fellwar Stone");
        card.HasType(CardType.Artifact).Should().BeTrue();
        card.ManaCost.Should().Be("{2}");
        card.Abilities.OfType<ManaAbility>().Should().HaveCount(5,
            "dispatcher path attaches the five colour slots");
    }

    // -----------------------------------------------------------------------
    // No resolver / no opponent lands → no slot is active
    // -----------------------------------------------------------------------

    [Fact]
    public void FellwarStone_NoResolver_NoSlotActive()
    {
        // Parameterless overload: no opponent visibility → nothing to reflect.
        var stone = StoneOnBattlefield();

        foreach (var color in new[] { "W", "U", "B", "R", "G" })
        {
            ColorAbility(stone, color).CanActivate().Should().BeFalse(
                $"with no resolver there are no visible opponents to produce {color}");
        }
    }

    [Fact]
    public void FellwarStone_OpponentHasNoLands_NoSlotActive()
    {
        var stone = StoneOnBattlefield(AllPlayers());

        foreach (var color in new[] { "W", "U", "B", "R", "G" })
        {
            ColorAbility(stone, color).CanActivate().Should().BeFalse(
                $"opponent controls no land that could produce {color}");
        }
    }

    // -----------------------------------------------------------------------
    // Opponent Forest → only {G} producible
    // -----------------------------------------------------------------------

    [Fact]
    public void FellwarStone_OpponentForest_OnlyGreenActive()
    {
        var stone = StoneOnBattlefield(AllPlayers());
        PutOnBattlefield(_bob, ForestFactory.Create(_bob));

        ColorAbility(stone, "G").CanActivate().Should().BeTrue(
            "an opponent's Forest could produce {G}");

        foreach (var color in new[] { "W", "U", "B", "R" })
        {
            ColorAbility(stone, color).CanActivate().Should().BeFalse(
                $"no land an opponent controls could produce {color}");
        }
    }

    // -----------------------------------------------------------------------
    // Opponent Forest + Island → {G} and {U} producible
    // -----------------------------------------------------------------------

    [Fact]
    public void FellwarStone_OpponentForestAndIsland_GreenAndBlueActive()
    {
        var stone = StoneOnBattlefield(AllPlayers());
        PutOnBattlefield(_bob, ForestFactory.Create(_bob));
        PutOnBattlefield(_bob, IslandFactory.Create(_bob));

        ColorAbility(stone, "G").CanActivate().Should().BeTrue();
        ColorAbility(stone, "U").CanActivate().Should().BeTrue();

        foreach (var color in new[] { "W", "B", "R" })
        {
            ColorAbility(stone, color).CanActivate().Should().BeFalse(
                $"no land an opponent controls could produce {color}");
        }
    }

    // -----------------------------------------------------------------------
    // Controller's OWN lands do NOT enable a slot (opponents only)
    // -----------------------------------------------------------------------

    [Fact]
    public void FellwarStone_OwnForest_DoesNotEnableGreen()
    {
        var stone = StoneOnBattlefield(AllPlayers());
        PutOnBattlefield(_alice, ForestFactory.Create(_alice));

        ColorAbility(stone, "G").CanActivate().Should().BeFalse(
            "Fellwar Stone reflects opponents' lands, not your own");
    }

    // -----------------------------------------------------------------------
    // Tapping the live ability produces the matching mana
    // -----------------------------------------------------------------------

    [Fact]
    public void FellwarStone_OpponentForest_TapsForGreen()
    {
        var stone = StoneOnBattlefield(AllPlayers());
        PutOnBattlefield(_bob, ForestFactory.Create(_bob));

        var green = ColorAbility(stone, "G");
        var produced = green.Activate();

        produced.ToString().Should().Be(ManaCost.Parse("G").ToString(),
            "the opponent's Forest makes {G} producible, so Fellwar Stone taps for {G}");
        stone.IsTapped.Should().BeTrue("{T} is the activation cost");
    }

    // -----------------------------------------------------------------------
    // Tapped Fellwar Stone can't activate even with a valid source
    // -----------------------------------------------------------------------

    [Fact]
    public void FellwarStone_Tapped_CannotActivate()
    {
        var stone = StoneOnBattlefield(AllPlayers());
        PutOnBattlefield(_bob, ForestFactory.Create(_bob));
        stone.Tap();

        ColorAbility(stone, "G").CanActivate().Should().BeFalse(
            "{T} is part of the cost; a tapped artifact can't pay it");
    }
}
