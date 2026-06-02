using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for <see cref="SliverLegionFactory"/> (Future Sight,
/// {W}{U}{B}{R}{G}). Legendary Creature — Sliver 7/7. Oracle text
/// (verified against Scryfall):
///   "All Sliver creatures get +1/+1 for each other Sliver on the
///    battlefield."
///
/// Covers:
/// - Identity (Legendary, Sliver, five-colour cost, 7/7, owner/controller).
/// - <see cref="NamedCardFactory"/> dispatch.
/// - The dynamic anthem buffs every Sliver creature on the battlefield by
///   +N/+N where N = OTHER Slivers (total − 1) — all players, not
///   controller-scoped.
/// - With Sliver Legion alone on the battlefield (only Sliver = itself),
///   it gets +0/+0 (no OTHER Slivers).
/// - A non-Sliver creature is NOT buffed.
/// - An opponent's Sliver IS buffed ("All Sliver creatures").
/// </summary>
[Trait("Color", "WUBRG")]
public class SliverLegionFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    private Func<IReadOnlyList<Player>> BothPlayers()
        => () => new[] { _alice, _bob };

    private static Creature MakeSliver(Player owner, string name)
    {
        var c = new Creature(name, "{U}", 1, 1, subtypes: new[] { CardSubtype.Sliver });
        c.SetOwner(owner);
        c.SetController(owner);
        owner.Zones.Battlefield.AddCard(c);
        c.SetZone(ZoneType.Battlefield);
        return c;
    }

    private static Creature MakeNonSliver(Player owner)
    {
        var c = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        c.SetOwner(owner);
        c.SetController(owner);
        owner.Zones.Battlefield.AddCard(c);
        c.SetZone(ZoneType.Battlefield);
        return c;
    }

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void SliverLegion_Identity_LegendaryFiveColorSliver_7_7()
    {
        var card = SliverLegionFactory.Create(_alice);

        card.Name.Should().Be("Sliver Legion");
        card.ManaCost.Should().Be("{W}{U}{B}{R}{G}");
        card.HasType(CardType.Creature).Should().BeTrue();
        card.HasSupertype(CardSupertype.Legendary).Should().BeTrue();
        card.HasSubtype(CardSubtype.Sliver).Should().BeTrue();
        card.BasePower.Should().Be(7);
        card.BaseToughness.Should().Be(7);
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void SliverLegion_Dispatches_ThroughNamedFactory()
    {
        var created = NamedCardFactory.Create("Sliver Legion", _alice);

        created.Should().NotBeNull();
        created.Name.Should().Be("Sliver Legion");
        created.Should().BeAssignableTo<Creature>();
        ((Creature)created).HasSubtype(CardSubtype.Sliver).Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // CountSliversOnBattlefield helper (CR 109.5 — all battlefields)
    // -----------------------------------------------------------------------

    [Fact]
    public void CountSliversOnBattlefield_CountsAcrossAllPlayers()
    {
        MakeSliver(_alice, "Galerider Sliver");
        MakeSliver(_bob, "Sidewinder Sliver");
        MakeNonSliver(_alice);

        var legion = SliverLegionFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(legion);
        legion.SetZone(ZoneType.Battlefield);

        SliverLegionFactory.CountSliversOnBattlefield(legion, BothPlayers())
            .Should().Be(3, "two named Slivers + Sliver Legion itself; the bear doesn't count");
    }

    // -----------------------------------------------------------------------
    // Dynamic anthem — "All Sliver creatures get +1/+1 for each other Sliver"
    // -----------------------------------------------------------------------

    [Fact]
    public void SliverLegion_Buffs_ControlledSliver_ByOtherSliverCount()
    {
        var continuous = new ContinuousEffectsService();

        // Board: Sliver Legion + two other Slivers Alice controls.
        var galerider = MakeSliver(_alice, "Galerider Sliver");
        galerider.ActiveEffects = continuous;
        var muscle = MakeSliver(_alice, "Muscle Sliver");
        muscle.ActiveEffects = continuous;

        var legion = SliverLegionFactory.Create(_alice, continuous, BothPlayers());
        _alice.Zones.Battlefield.AddCard(legion);
        legion.SetZone(ZoneType.Battlefield);
        legion.ActiveEffects = continuous;

        // Total Slivers = 3 (Legion + Galerider + Muscle). Each gets +1/+1
        // for each OTHER Sliver = total − 1 = 2.
        var chars = continuous.Compute(galerider);
        chars.Power.Should().Be(1 + 2, "1/1 base + (3 Slivers − 1 other) = +2/+2");
        chars.Toughness.Should().Be(1 + 2);
    }

    [Fact]
    public void SliverLegion_BuffsItself_ByOtherSliverCount()
    {
        var continuous = new ContinuousEffectsService();

        var galerider = MakeSliver(_alice, "Galerider Sliver");
        galerider.ActiveEffects = continuous;

        var legion = SliverLegionFactory.Create(_alice, continuous, BothPlayers());
        _alice.Zones.Battlefield.AddCard(legion);
        legion.SetZone(ZoneType.Battlefield);
        legion.ActiveEffects = continuous;

        // Total Slivers = 2 (Legion + Galerider). Legion is itself a Sliver,
        // so it is buffed by the OTHER Sliver (count = 1).
        var chars = continuous.Compute(legion);
        chars.Power.Should().Be(7 + 1, "7/7 base + (2 Slivers − 1 other) = +1/+1");
        chars.Toughness.Should().Be(7 + 1);
    }

    [Fact]
    public void SliverLegion_Alone_GetsNoBuff()
    {
        var continuous = new ContinuousEffectsService();

        var legion = SliverLegionFactory.Create(_alice, continuous, BothPlayers());
        _alice.Zones.Battlefield.AddCard(legion);
        legion.SetZone(ZoneType.Battlefield);
        legion.ActiveEffects = continuous;

        // Only Sliver = Legion itself → no OTHER Slivers → +0/+0.
        var chars = continuous.Compute(legion);
        chars.Power.Should().Be(7, "no other Slivers on the battlefield");
        chars.Toughness.Should().Be(7);
    }

    [Fact]
    public void SliverLegion_DoesNotBuff_NonSliver()
    {
        var continuous = new ContinuousEffectsService();

        var bears = MakeNonSliver(_alice);
        bears.ActiveEffects = continuous;
        MakeSliver(_alice, "Galerider Sliver");

        var legion = SliverLegionFactory.Create(_alice, continuous, BothPlayers());
        _alice.Zones.Battlefield.AddCard(legion);
        legion.SetZone(ZoneType.Battlefield);
        legion.ActiveEffects = continuous;

        var chars = continuous.Compute(bears);
        chars.Power.Should().Be(2, "Grizzly Bears is not a Sliver — the anthem doesn't apply");
        chars.Toughness.Should().Be(2);
    }

    [Fact]
    public void SliverLegion_Buffs_OpponentSliver_AllPlayersScope()
    {
        var continuous = new ContinuousEffectsService();

        var bobSliver = MakeSliver(_bob, "Sidewinder Sliver");
        bobSliver.ActiveEffects = continuous;

        var legion = SliverLegionFactory.Create(_alice, continuous, BothPlayers());
        _alice.Zones.Battlefield.AddCard(legion);
        legion.SetZone(ZoneType.Battlefield);
        legion.ActiveEffects = continuous;

        // "All Sliver creatures" — Bob's Sliver is buffed too. Total
        // Slivers = 2 (Legion + Bob's) → +1/+1.
        var chars = continuous.Compute(bobSliver);
        chars.Power.Should().Be(1 + 1, "All Sliver creatures (any player) get the anthem");
        chars.Toughness.Should().Be(1 + 1);
    }
}
