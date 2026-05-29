using System.Linq;
using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Counters;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Tests for <see cref="GemstoneMineFactory"/> (Weatherlight / reprints).
/// Land:
///   "This land enters with three mining counters on it.
///    {T}, Remove a mining counter from this land: Add one mana of any
///    color. If there are no mining counters on this land, sacrifice it."
///
/// Covers:
/// - Identity (Land, no supertype, name, owner/controller).
/// - <see cref="NamedCardFactory"/> dispatch.
/// - ETB trigger places three mining counters (CR 614.1d-style shape).
/// - "Add one mana of any color" modelled as five WUBRG mana abilities,
///   each gated on untapped + on-battlefield + at-least-one mining counter.
/// - Activating removes one mining counter.
/// - When the last mining counter is removed, the land sacrifices itself
///   (CR 701.16) — the "if there are no mining counters, sacrifice it"
///   rider.
/// - With counters remaining the land survives the activation.
/// </summary>
public class GemstoneMineTests
{
    private readonly Player _alice = new("Alice", 20);

    private static ManaAbility ColorAbility(Land land, string colorSymbol) =>
        land.Abilities.OfType<ManaAbility>()
            .Single(a => a.ManaGenerated.ToString() == ManaCost.Parse(colorSymbol).ToString());

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void GemstoneMine_Identity()
    {
        var land = GemstoneMineFactory.Create(_alice);

        land.Name.Should().Be("Gemstone Mine");
        land.HasType(CardType.Land).Should().BeTrue();
        land.HasSupertype(CardSupertype.Basic).Should().BeFalse(
            "Gemstone Mine is a nonbasic land");
        land.Owner.Should().BeSameAs(_alice);
        land.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void GemstoneMine_DispatchesViaNamedCardFactory()
    {
        var card = NamedCardFactory.Create("Gemstone Mine", _alice);

        card.Should().BeOfType<Land>();
        card.Name.Should().Be("Gemstone Mine");
        card.HasType(CardType.Land).Should().BeTrue();

        card.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1,
            "the ETB mining-counter trigger is attached for shape");
        card.Abilities.OfType<ManaAbility>().Should().HaveCount(5,
            "'Add one mana of any color' is five WUBRG mana abilities");
    }

    // -----------------------------------------------------------------------
    // ETB mining-counter trigger
    // -----------------------------------------------------------------------

    [Fact]
    public void GemstoneMine_EtbTrigger_AddsThreeMiningCounters()
    {
        var land = GemstoneMineFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(land);
        land.SetZone(ZoneType.Battlefield);

        land.Counters.Count(CounterType.Mining).Should().Be(0,
            "no counters until the ETB trigger fires");

        var trigger = land.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var e in trigger.Effects) e.Execute();

        land.Counters.Count(CounterType.Mining).Should().Be(3,
            "ETB trigger adds exactly three mining counters (CR 614.1d-style)");
    }

    // -----------------------------------------------------------------------
    // Mana abilities + activation gating
    // -----------------------------------------------------------------------

    [Fact]
    public void GemstoneMine_ManaAbility_BlockedWhenNoMiningCounters()
    {
        var land = GemstoneMineFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(land);
        land.SetZone(ZoneType.Battlefield);
        // No ETB fired → zero mining counters → cannot pay the remove cost.

        var white = ColorAbility(land, "W");
        white.CanActivate().Should().BeFalse(
            "the remove-a-mining-counter cost cannot be paid with zero counters (CR 119.4)");
    }

    [Fact]
    public void GemstoneMine_ManaAbility_RemovesOneMiningCounter_LandSurvivesWithCountersLeft()
    {
        var land = GemstoneMineFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(land);
        land.SetZone(ZoneType.Battlefield);
        land.Counters.Add(CounterType.Mining, 3);

        var blue = ColorAbility(land, "U");
        blue.CanActivate().Should().BeTrue("untapped, on battlefield, 3 mining counters");

        // Activate: tap {T} + remove one mining counter (the additional cost).
        var produced = blue.Activate();
        produced.ToString().Should().Be(ManaCost.Parse("U").ToString(),
            "the ability adds one blue mana");

        land.Counters.Count(CounterType.Mining).Should().Be(2,
            "one mining counter removed as the activation cost");
        land.IsTapped.Should().BeTrue("{T} is part of the activation cost");
        land.Zone.Should().Be(ZoneType.Battlefield,
            "two mining counters remain → no self-sacrifice");
    }

    [Fact]
    public void GemstoneMine_ManaAbility_SacrificesSelf_WhenLastMiningCounterRemoved()
    {
        var land = GemstoneMineFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(land);
        land.SetZone(ZoneType.Battlefield);
        land.Counters.Add(CounterType.Mining, 1);

        var green = ColorAbility(land, "G");
        green.CanActivate().Should().BeTrue("one mining counter left — still activatable");

        green.Activate();

        land.Counters.Count(CounterType.Mining).Should().Be(0,
            "the last mining counter is removed as the activation cost");
        land.Zone.Should().Be(ZoneType.Graveyard,
            "no mining counters remain → 'sacrifice it' (CR 701.16)");
        _alice.Zones.Graveyard.GetCards().Should().Contain(land);
        _alice.Zones.Battlefield.GetCards().Should().NotContain(land);
    }
}
