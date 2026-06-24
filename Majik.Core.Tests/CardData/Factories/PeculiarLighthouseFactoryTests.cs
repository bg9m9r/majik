using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for <see cref="PeculiarLighthouseFactory"/> (Duskmourn: House of
/// Horror).
///
/// Oracle (verified against Scryfall):
///   "This land enters tapped unless a player has 13 or less life.
///    {T}: Add {U} or {R}."
///
/// Covers ONLY the card's unique behaviour:
/// - Identity: nonbasic, non-legendary Land (no printed subtype).
/// - Two mana abilities producing {U} and {R}.
/// - ETB-tapped predicate (CR 614.1c): the "a player has 13 or less life"
///   check across controller / opponent / no-low-life cases. "a player" =
///   ANY player, so an opponent at low life flips the land untapped too.
///
/// Dispatch + well-formedness are asserted for every implemented card by
/// CardFactoryContractTests — not re-tested here.
/// </summary>
[Trait("Color", "M")]
public class PeculiarLighthouseFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void PeculiarLighthouse_Identity_IsNonbasicNonLegendaryLand()
    {
        var land = PeculiarLighthouseFactory.Create(_alice);

        land.Should().BeOfType<Land>();
        land.HasType(CardType.Land).Should().BeTrue();
        land.Name.Should().Be("Peculiar Lighthouse");
        land.HasSupertype(CardSupertype.Basic).Should().BeFalse(
            "Peculiar Lighthouse is a nonbasic land");
        land.HasSupertype(CardSupertype.Legendary).Should().BeFalse();
        land.Owner.Should().BeSameAs(_alice);
        land.Controller.Should().BeSameAs(_alice);
    }

    // -----------------------------------------------------------------------
    // Mana abilities: {T}: Add {U} or {R}
    // -----------------------------------------------------------------------

    [Fact]
    public void PeculiarLighthouse_HasTwoManaAbilities_ProducingBlueAndRed()
    {
        var land = PeculiarLighthouseFactory.Create(_alice);

        var manaAbilities = land.Abilities.OfType<ManaAbility>().ToList();
        manaAbilities.Should().HaveCount(2,
            "two single-colour mana abilities: {T}: Add {U} or {R}");

        manaAbilities.Should().ContainSingle(a => a.ManaGenerated.Blue == 1,
            "one ability produces exactly 1 blue pip");
        manaAbilities.Should().ContainSingle(a => a.ManaGenerated.Red == 1,
            "one ability produces exactly 1 red pip");

        foreach (var a in manaAbilities)
        {
            a.ManaGenerated.Generic.Should().Be(0);
            a.ManaGenerated.Green.Should().Be(0);
            a.ManaGenerated.White.Should().Be(0);
            a.ManaGenerated.Black.Should().Be(0);
        }
    }

    // -----------------------------------------------------------------------
    // ETB-tapped predicate (CR 614.1c): "a player has 13 or less life"
    // -----------------------------------------------------------------------

    private static ZoneMoveIntent EtbIntent(Land land, Player controller) =>
        new(
            Card: land,
            FromZone: ZoneType.Hand,
            ToZone: ZoneType.Battlefield,
            Controller: controller);

    [Fact]
    public void PeculiarLighthouse_EntersTapped_WhenAllPlayersAbove13()
    {
        var bus = new ReplacementBus();
        var bob = new Player("Bob", 20);
        // Both players at 20 (> 13), no player qualifies.
        var land = PeculiarLighthouseFactory.Create(
            _alice, bus, allPlayersProvider: () => new[] { _alice, bob });

        var after = bus.Apply(EtbIntent(land, _alice));

        after.Should().NotBeNull();
        after!.EntersTapped.Should().BeTrue(
            "no player has 13 or less life, so the land enters tapped");
    }

    [Fact]
    public void PeculiarLighthouse_EntersUntapped_WhenControllerAt13OrLess()
    {
        var bus = new ReplacementBus();
        _alice.LifeTotal = 13; // exactly the threshold qualifies (13 or less)
        var bob = new Player("Bob", 20);

        var land = PeculiarLighthouseFactory.Create(
            _alice, bus, allPlayersProvider: () => new[] { _alice, bob });

        var after = bus.Apply(EtbIntent(land, _alice));

        after.Should().NotBeNull();
        after!.EntersTapped.Should().BeFalse(
            "the controller has exactly 13 life, so the land enters untapped");
    }

    [Fact]
    public void PeculiarLighthouse_EntersUntapped_WhenOpponentAtLowLife()
    {
        var bus = new ReplacementBus();
        var bob = new Player("Bob", 20) { LifeTotal = 5 };
        // Controller is high; "a player" (the opponent) is low → untapped.
        var land = PeculiarLighthouseFactory.Create(
            _alice, bus, allPlayersProvider: () => new[] { _alice, bob });

        var after = bus.Apply(EtbIntent(land, _alice));

        after.Should().NotBeNull();
        after!.EntersTapped.Should().BeFalse(
            "'a player' means ANY player — the opponent at 5 life qualifies");
    }

    [Fact]
    public void PeculiarLighthouse_RosterlessPath_FallsBackToControllerLife()
    {
        // No allPlayersProvider: predicate inspects only the controller, who
        // is always "a player". Controller below threshold → untapped.
        var bus = new ReplacementBus();
        _alice.LifeTotal = 10;

        var land = PeculiarLighthouseFactory.Create(_alice, bus);

        var after = bus.Apply(EtbIntent(land, _alice));

        after.Should().NotBeNull();
        after!.EntersTapped.Should().BeFalse(
            "roster-less path still flips untapped when the controller is low");
    }
}
