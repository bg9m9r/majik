using System.Linq;
using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for <see cref="RazortrapGorgeFactory"/> (Tarkir: Dragonstorm).
///
/// Oracle (verified against Scryfall):
///   "This land enters tapped unless a player has 13 or less life.
///    {T}: Add {B} or {R}."
///
/// Covers the card's UNIQUE behaviour:
/// - Two mana abilities, one producing {B} and one producing {R}.
/// - ETB-tapped predicate (CR 614.1c): enters tapped while EVERY player has
///   more than 13 life; enters untapped as soon as ANY player (controller OR
///   opponent — the oracle says "a player", CR 102) is at 13 or less.
/// </summary>
[Trait("Color", "C")]
public class RazortrapGorgeFactoryTests
{
    // -----------------------------------------------------------------------
    // Mana abilities: {T}: Add {B} or {R}
    // -----------------------------------------------------------------------

    [Fact]
    public void RazortrapGorge_HasTwoManaAbilities_OneBlackOneRed()
    {
        var alice = new Player("Alice", 20);
        var land = RazortrapGorgeFactory.Create(alice);

        var manaAbilities = land.Abilities.OfType<ManaAbility>().ToList();
        manaAbilities.Should().HaveCount(2,
            "two mana abilities: {T}: Add {B} and {T}: Add {R}");

        manaAbilities.Should().ContainSingle(a => a.ManaGenerated.Black == 1
            && a.ManaGenerated.Red == 0 && a.ManaGenerated.Generic == 0,
            "one ability produces exactly one black pip");
        manaAbilities.Should().ContainSingle(a => a.ManaGenerated.Red == 1
            && a.ManaGenerated.Black == 0 && a.ManaGenerated.Generic == 0,
            "one ability produces exactly one red pip");
    }

    // -----------------------------------------------------------------------
    // ETB-tapped predicate (CR 614.1c) — "a player has 13 or less life"
    // -----------------------------------------------------------------------

    private static ZoneMoveIntent EtbIntent(Land land, Player controller) =>
        new(
            Card: land,
            FromZone: ZoneType.Hand,
            ToZone: ZoneType.Battlefield,
            Controller: controller);

    [Fact]
    public void RazortrapGorge_EntersTapped_WhenEveryPlayerAboveThirteen()
    {
        using var scope = GamePlayersRegistry.PushScope();
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);
        GamePlayersRegistry.Set(new[] { alice, bob });

        var bus = new ReplacementBus();
        var land = RazortrapGorgeFactory.Create(alice, bus);

        var after = bus.Apply(EtbIntent(land, alice));

        after.Should().NotBeNull();
        after!.EntersTapped.Should().BeTrue(
            "no player is at 13 or less life, so the land enters tapped");
    }

    [Fact]
    public void RazortrapGorge_EntersUntapped_WhenControllerHasThirteenOrLess()
    {
        using var scope = GamePlayersRegistry.PushScope();
        var alice = new Player("Alice", 13);
        var bob = new Player("Bob", 20);
        GamePlayersRegistry.Set(new[] { alice, bob });

        var bus = new ReplacementBus();
        var land = RazortrapGorgeFactory.Create(alice, bus);

        var after = bus.Apply(EtbIntent(land, alice));

        after.Should().NotBeNull();
        after!.EntersTapped.Should().BeFalse(
            "the controller is at exactly 13 life, so the land enters untapped");
    }

    [Fact]
    public void RazortrapGorge_EntersUntapped_WhenAnOpponentHasThirteenOrLess()
    {
        using var scope = GamePlayersRegistry.PushScope();
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 7);
        GamePlayersRegistry.Set(new[] { alice, bob });

        var bus = new ReplacementBus();
        var land = RazortrapGorgeFactory.Create(alice, bus);

        var after = bus.Apply(EtbIntent(land, alice));

        after.Should().NotBeNull();
        after!.EntersTapped.Should().BeFalse(
            "the oracle says 'a player' — an opponent at 7 life satisfies it");
    }
}
