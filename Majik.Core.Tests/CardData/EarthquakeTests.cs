using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Unit tests for <see cref="EarthquakeFactory"/>.
///
/// Card: Earthquake — Sorcery {X}{R} (various reprints).
///   "Earthquake deals X damage to each creature without flying and each
///    player."
///
/// Covers the card's UNIQUE behaviour (the contract test already asserts
/// dispatch + well-formedness):
///   - Identity (name, type, X-cost {X}{R}, owner/controller).
///   - Resolve dishes X damage to every NON-FLYING creature on every
///     supplied player's battlefield, regardless of controller.
///   - Flying creatures are spared — including the caster's AND the
///     opponent's (no "you control" restriction, unlike Flame Sweep).
///   - Each player takes X damage (symmetric — the caster too).
///   - X scales the damage; X = 0 is a clean no-op.
/// </summary>
[Trait("Color", "R")]
public class EarthquakeTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void Earthquake_Identity()
    {
        var c = EarthquakeFactory.Create(_alice);

        c.Name.Should().Be("Earthquake");
        c.ManaCost.Should().Be("{X}{R}");
        c.HasType(CardType.Sorcery).Should().BeTrue();
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    // -----------------------------------------------------------------------
    // Resolve — non-flying creature sweep
    // -----------------------------------------------------------------------

    [Fact]
    public void Resolve_DealsXDamage_ToEveryNonFlyingCreature_AcrossBothPlayers()
    {
        var aliceGround = NewCreatureOnBattlefield(_alice, "Grizzly Bears", "{1}{G}", 2, 2);
        var aliceFlyer = NewCreatureOnBattlefield(_alice, "Wind Drake", "{2}{U}", 2, 2, flying: true);
        var bobGround = NewCreatureOnBattlefield(_bob, "Runeclaw Bear", "{1}{G}", 2, 2);
        var bobFlyer = NewCreatureOnBattlefield(_bob, "Snapping Drake", "{3}{U}", 3, 2, flying: true);

        var effects = EarthquakeFactory.BuildResolveEffect(new[] { _alice, _bob }, x: 3);
        foreach (var e in effects) e.Execute();

        aliceGround.Damage.Should().Be(3, "non-flying creatures take X");
        bobGround.Damage.Should().Be(3, "opponent non-flying creatures are also damaged");
        aliceFlyer.Damage.Should().Be(0, "creatures with flying are spared");
        bobFlyer.Damage.Should().Be(0, "opponent flyers are spared too — no \"you control\" restriction");
    }

    [Fact]
    public void Resolve_KillsCreatures_WhenXIsLethal_AndSparesFlyers()
    {
        var bears = NewCreatureOnBattlefield(_alice, "Grizzly Bears", "{1}{G}", 2, 2);
        var giant = NewCreatureOnBattlefield(_alice, "Hill Giant", "{3}{R}", 3, 3);
        var flyer = NewCreatureOnBattlefield(_bob, "Storm Crow", "{1}{U}", 1, 2, flying: true);

        var effects = EarthquakeFactory.BuildResolveEffect(new[] { _alice, _bob }, x: 2);
        foreach (var e in effects) e.Execute();

        bears.IsDead().Should().BeTrue("2 damage on a 2/2 is lethal");
        giant.IsDead().Should().BeFalse("2 damage on a 3/3 is survivable");
        giant.Damage.Should().Be(2);
        flyer.IsDead().Should().BeFalse("the flyer is spared entirely");
        flyer.Damage.Should().Be(0);
    }

    // -----------------------------------------------------------------------
    // Resolve — each player
    // -----------------------------------------------------------------------

    [Fact]
    public void Resolve_DealsXDamage_ToEachPlayer_Symmetrically()
    {
        var effects = EarthquakeFactory.BuildResolveEffect(new[] { _alice, _bob }, x: 5);
        foreach (var e in effects) e.Execute();

        _alice.LifeTotal.Should().Be(15, "the caster takes X too — Earthquake is symmetric");
        _bob.LifeTotal.Should().Be(15, "each player takes X damage");
    }

    [Fact]
    public void Resolve_XScalesBothCreatureAndPlayerDamage()
    {
        var bear = NewCreatureOnBattlefield(_alice, "Grizzly Bears", "{1}{G}", 2, 2);

        var effects = EarthquakeFactory.BuildResolveEffect(new[] { _alice, _bob }, x: 7);
        foreach (var e in effects) e.Execute();

        bear.Damage.Should().Be(7);
        _alice.LifeTotal.Should().Be(13);
        _bob.LifeTotal.Should().Be(13);
    }

    [Fact]
    public void Resolve_IgnoresNonCreaturePermanents()
    {
        var bear = NewCreatureOnBattlefield(_alice, "Grizzly Bears", "{1}{G}", 2, 2);

        var artifact = new Artifact("Mishra's Bauble", "{0}");
        artifact.SetOwner(_alice);
        _alice.Zones.Battlefield.AddCard(artifact);
        artifact.SetZone(ZoneType.Battlefield);

        var land = new Land("Mountain");
        land.SetOwner(_alice);
        _alice.Zones.Battlefield.AddCard(land);
        land.SetZone(ZoneType.Battlefield);

        var effects = EarthquakeFactory.BuildResolveEffect(new[] { _alice, _bob }, x: 2);
        foreach (var e in effects) e.Execute();

        bear.Damage.Should().Be(2, "creatures take the sweep");
        artifact.Zone.Should().Be(ZoneType.Battlefield);
        land.Zone.Should().Be(ZoneType.Battlefield);
    }

    [Fact]
    public void Resolve_XZero_IsCleanNoOp()
    {
        var bear = NewCreatureOnBattlefield(_alice, "Grizzly Bears", "{1}{G}", 2, 2);

        var effects = EarthquakeFactory.BuildResolveEffect(new[] { _alice, _bob }, x: 0);
        var act = () => { foreach (var e in effects) e.Execute(); };

        act.Should().NotThrow();
        bear.Damage.Should().Be(0, "X = 0 deals no damage");
        _alice.LifeTotal.Should().Be(20);
        _bob.LifeTotal.Should().Be(20);
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static Creature NewCreatureOnBattlefield(
        Player owner, string name, string manaCost, int power, int toughness, bool flying = false)
    {
        var c = new Creature(name, manaCost, power, toughness);
        c.SetOwner(owner);
        c.SetController(owner);
        if (flying)
        {
            c.AddAbility(new KeywordAbility("Flying", source: c, controller: owner));
        }
        owner.Zones.Battlefield.AddCard(c);
        c.SetZone(ZoneType.Battlefield);
        return c;
    }
}
