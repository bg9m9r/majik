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
/// Tests for <see cref="StrangledCemeteryFactory"/> (Duskmourn: House of
/// Horror — B/G dual land).
///
/// Oracle:
///   "This land enters tapped unless a player has 13 or less life.
///    {T}: Add {B} or {G}."
///
/// Covers ONLY the card's unique behaviour:
/// - Mana abilities: exactly two, one producing {B}, one producing {G}.
/// - Conditional ETB-tapped predicate (CR 614.1c) keyed on the GLOBAL
///   "a player has 13 or less life" game-state fact (any player, including
///   the controller and opponents), read off
///   <see cref="GamePlayersRegistry.AllPlayers"/>:
///     * everyone above 13 ⇒ enters tapped;
///     * controller at/below 13 ⇒ enters untapped;
///     * an opponent at/below 13 (controller high) ⇒ enters untapped;
///     * exactly 13 is the boundary (enters untapped);
///     * shape-only path (no ReplacementBus) ⇒ no replacement registered.
///
/// (Identity-only / NamedCardFactory-dispatch / type asserts are covered for
/// every implemented card by CardFactoryContractTests — not repeated here.)
/// </summary>
[Trait("Color", "C")]
public class StrangledCemeteryFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    private static ZoneMoveIntent EnterIntent(ICard land, Player controller) =>
        new(
            Card: land,
            FromZone: ZoneType.Hand,
            ToZone: ZoneType.Battlefield,
            Controller: controller);

    // -----------------------------------------------------------------------
    // Mana abilities: {T}: Add {B} or {G}
    // -----------------------------------------------------------------------

    [Fact]
    public void StrangledCemetery_HasTwoManaAbilities_ProducingBlackAndGreen()
    {
        var land = StrangledCemeteryFactory.Create(_alice);

        var manaAbilities = land.Abilities.OfType<ManaAbility>().ToList();
        manaAbilities.Should().HaveCount(2,
            "two single-colour mana abilities model \"Add {B} or {G}\"");

        manaAbilities.Should().ContainSingle(a =>
            a.ManaGenerated.Black == 1 && a.ManaGenerated.Green == 0,
            "one ability produces a single black pip");
        manaAbilities.Should().ContainSingle(a =>
            a.ManaGenerated.Green == 1 && a.ManaGenerated.Black == 0,
            "one ability produces a single green pip");
        manaAbilities.Should().OnlyContain(a => a.ManaGenerated.Generic == 0);
    }

    // -----------------------------------------------------------------------
    // Conditional ETB-tapped predicate (CR 614.1c) — "a player has 13 or less"
    // -----------------------------------------------------------------------

    [Fact]
    public void StrangledCemetery_EntersTapped_WhenAllPlayersAbove13Life()
    {
        using var _ = GamePlayersRegistry.PushScope();
        var bob = new Player("Bob", 20);
        GamePlayersRegistry.Set(new[] { _alice, bob }); // 20 / 20

        var bus = new ReplacementBus();
        var land = StrangledCemeteryFactory.Create(_alice, bus);

        var after = bus.Apply(EnterIntent(land, _alice));

        after.Should().NotBeNull();
        after!.EntersTapped.Should().BeTrue(
            "no player is at 13 or less life, so the land enters tapped");
    }

    [Fact]
    public void StrangledCemetery_EntersUntapped_WhenControllerHas13OrLess()
    {
        using var _ = GamePlayersRegistry.PushScope();
        _alice.LifeTotal = 10;
        var bob = new Player("Bob", 20);
        GamePlayersRegistry.Set(new[] { _alice, bob });

        var bus = new ReplacementBus();
        var land = StrangledCemeteryFactory.Create(_alice, bus);

        var after = bus.Apply(EnterIntent(land, _alice));

        after.Should().NotBeNull();
        after!.EntersTapped.Should().BeFalse(
            "the controller is at 10 life (<= 13), so the land enters untapped");
    }

    [Fact]
    public void StrangledCemetery_EntersUntapped_WhenOpponentHas13OrLess()
    {
        using var _ = GamePlayersRegistry.PushScope();
        var bob = new Player("Bob", 20) { LifeTotal = 5 };
        GamePlayersRegistry.Set(new[] { _alice, bob }); // Alice 20, Bob 5

        var bus = new ReplacementBus();
        var land = StrangledCemeteryFactory.Create(_alice, bus);

        var after = bus.Apply(EnterIntent(land, _alice));

        after.Should().NotBeNull();
        after!.EntersTapped.Should().BeFalse(
            "\"a player\" includes opponents — Bob at 5 life satisfies the gate");
    }

    [Fact]
    public void StrangledCemetery_EntersUntapped_AtExactly13Life()
    {
        using var _ = GamePlayersRegistry.PushScope();
        _alice.LifeTotal = 13;
        GamePlayersRegistry.Set(new[] { _alice });

        var bus = new ReplacementBus();
        var land = StrangledCemeteryFactory.Create(_alice, bus);

        var after = bus.Apply(EnterIntent(land, _alice));

        after.Should().NotBeNull();
        after!.EntersTapped.Should().BeFalse(
            "13 is the boundary — \"13 or less\" is inclusive, so it enters untapped");
    }

    [Fact]
    public void StrangledCemetery_ShapeOnlyPath_RegistersNoReplacement()
    {
        // Single-arg dispatcher path: no ReplacementBus, so no ETB replacement
        // is registered and an unrelated bus is a no-op on the move intent.
        var land = StrangledCemeteryFactory.Create(_alice);

        var bus = new ReplacementBus();
        var after = bus.Apply(EnterIntent(land, _alice));

        after.Should().NotBeNull();
        after!.EntersTapped.Should().BeFalse(
            "no replacement was registered on the shape-only path");
    }
}
