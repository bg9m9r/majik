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
/// Tests for <see cref="ReflectingPoolFactory"/> (Tempest).
///
/// Land. Oracle text (verified against Scryfall 2026-05-29):
///   "{T}: Add one mana of any type that a land you control could produce."
///
/// "Type of mana" is the five colours plus colorless (CR 107.4c / 106.1b);
/// Reflecting Pool offers exactly the union of types its controller's OTHER
/// lands could produce. Modelled — like Cavern of Souls / Gemstone Caverns —
/// as six fixed-type <see cref="ManaAbility"/> instances (W,U,B,R,G,C), each
/// gated by a <c>canActivateCheck</c> that is live only while some land the
/// controller controls (other than Reflecting Pool itself) has a mana ability
/// producing that type (CR 605.1a).
///
/// Covers:
/// - Identity (nonbasic Land, name, owner/controller).
/// - <see cref="NamedCardFactory"/> dispatch + six mana abilities.
/// - Alone on the battlefield: no ability is active (no source for any type).
/// - With a Forest: only the {G} ability is active.
/// - With a Forest + an Island: {G} and {U} active, others not.
/// - Reflecting Pool ignores itself (no infinite self-reference).
/// - Tapping the live ability produces the matching mana and taps the land.
/// </summary>
[Trait("Color", "C")]
public class ReflectingPoolTests
{
    private readonly Player _alice = new("Alice", 20);

    private static ManaAbility ColorAbility(Land land, string colorSymbol) =>
        land.Abilities.OfType<ManaAbility>()
            .Single(a => a.ManaGenerated.ToString() == ManaCost.Parse(colorSymbol).ToString());

    private Land PoolOnBattlefield()
    {
        var pool = ReflectingPoolFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(pool);
        pool.SetZone(ZoneType.Battlefield);
        return pool;
    }

    private void PutOnBattlefield(Land land)
    {
        _alice.Zones.Battlefield.AddCard(land);
        land.SetZone(ZoneType.Battlefield);
    }

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void ReflectingPool_Identity()
    {
        var land = ReflectingPoolFactory.Create(_alice);

        land.Name.Should().Be("Reflecting Pool");
        land.HasType(CardType.Land).Should().BeTrue();
        land.HasSupertype(CardSupertype.Basic).Should().BeFalse("Reflecting Pool is a nonbasic Land");
        land.Owner.Should().BeSameAs(_alice);
        land.Controller.Should().BeSameAs(_alice);
    }
    // -----------------------------------------------------------------------
    // No other lands → no type is producible → no ability is active
    // -----------------------------------------------------------------------

    [Fact]
    public void ReflectingPool_Alone_NoAbilityActive()
    {
        var pool = PoolOnBattlefield();

        foreach (var color in new[] { "W", "U", "B", "R", "G", "C" })
        {
            ColorAbility(pool, color).CanActivate().Should().BeFalse(
                $"with no other lands, no land you control could produce {color}");
        }
    }

    // -----------------------------------------------------------------------
    // With a Forest → only {G} is producible
    // -----------------------------------------------------------------------

    [Fact]
    public void ReflectingPool_WithForest_OnlyGreenActive()
    {
        var pool = PoolOnBattlefield();
        PutOnBattlefield((Land)NamedCardFactory.Create("Forest", _alice));

        ColorAbility(pool, "G").CanActivate().Should().BeTrue(
            "a Forest you control could produce {G}");

        foreach (var color in new[] { "W", "U", "B", "R", "C" })
        {
            ColorAbility(pool, color).CanActivate().Should().BeFalse(
                $"no land you control could produce {color}");
        }
    }

    // -----------------------------------------------------------------------
    // With a Forest + an Island → {G} and {U} both producible
    // -----------------------------------------------------------------------

    [Fact]
    public void ReflectingPool_WithForestAndIsland_GreenAndBlueActive()
    {
        var pool = PoolOnBattlefield();
        PutOnBattlefield((Land)NamedCardFactory.Create("Forest", _alice));
        PutOnBattlefield((Land)NamedCardFactory.Create("Island", _alice));

        ColorAbility(pool, "G").CanActivate().Should().BeTrue();
        ColorAbility(pool, "U").CanActivate().Should().BeTrue();

        foreach (var color in new[] { "W", "B", "R", "C" })
        {
            ColorAbility(pool, color).CanActivate().Should().BeFalse(
                $"no land you control could produce {color}");
        }
    }

    // -----------------------------------------------------------------------
    // Reflecting Pool ignores itself (no circular self-reference)
    // -----------------------------------------------------------------------

    [Fact]
    public void ReflectingPool_IgnoresItself()
    {
        // Two Reflecting Pools and nothing else: neither can produce any type,
        // because each only "could produce" by reflecting the other — there is
        // no actual mana source to seed the union.
        var pool1 = PoolOnBattlefield();
        var pool2 = ReflectingPoolFactory.Create(_alice);
        PutOnBattlefield(pool2);

        foreach (var color in new[] { "W", "U", "B", "R", "G", "C" })
        {
            ColorAbility(pool1, color).CanActivate().Should().BeFalse(
                "a Reflecting Pool does not seed its own (or another Pool's) producible types");
        }
    }

    // -----------------------------------------------------------------------
    // Tapping the live ability produces the matching mana
    // -----------------------------------------------------------------------

    [Fact]
    public void ReflectingPool_WithForest_TapsForGreen()
    {
        var pool = PoolOnBattlefield();
        PutOnBattlefield((Land)NamedCardFactory.Create("Forest", _alice));

        var green = ColorAbility(pool, "G");
        var produced = green.Activate();

        produced.ToString().Should().Be(ManaCost.Parse("G").ToString(),
            "the Forest makes {G} producible, so Reflecting Pool taps for {G}");
        pool.IsTapped.Should().BeTrue("{T} is the activation cost");
    }

    // -----------------------------------------------------------------------
    // Tapped Reflecting Pool can't activate even with a valid source
    // -----------------------------------------------------------------------

    [Fact]
    public void ReflectingPool_Tapped_CannotActivate()
    {
        var pool = PoolOnBattlefield();
        PutOnBattlefield((Land)NamedCardFactory.Create("Forest", _alice));
        pool.Tap();

        ColorAbility(pool, "G").CanActivate().Should().BeFalse(
            "{T} is part of the cost; a tapped land can't pay it");
    }
}
