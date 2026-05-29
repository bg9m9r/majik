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
/// Unit tests for <see cref="FlameSweepFactory"/>.
///
/// Card: Flame Sweep — Instant {2}{R} (M11 / reprints).
///   "Flame Sweep deals 2 damage to each creature except for creatures
///    you control with flying."
///
/// Covers:
///   - Identity (name, type, mana cost, owner/controller).
///   - NamedCardFactory dispatch.
///   - Resolve dishes 2 damage to every creature on every supplied
///     player's battlefield EXCEPT the caster's own flying creatures.
///   - Opponent flyers are still hit (the exception is "you control").
///   - Non-flying creatures the caster controls are still hit.
///   - 1-toughness creatures are marked lethal after the sweep.
///   - 3-toughness creatures survive (with 2 damage marked).
/// </summary>
public class FlameSweepTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void FlameSweep_Identity()
    {
        var c = FlameSweepFactory.Create(_alice);

        c.Name.Should().Be("Flame Sweep");
        c.ManaCost.Should().Be("{2}{R}");
        c.HasType(CardType.Instant).Should().BeTrue();
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_FlameSweep()
    {
        var card = NamedCardFactory.Create("Flame Sweep", _alice);

        card.Should().BeOfType<Instant>();
        card.Name.Should().Be("Flame Sweep");
        card.HasType(CardType.Instant).Should().BeTrue();
        card.ManaCost.Should().Be("{2}{R}");
        card.Owner.Should().BeSameAs(_alice);
    }

    // -----------------------------------------------------------------------
    // Resolve — sweep with the "you control with flying" exception
    // -----------------------------------------------------------------------

    [Fact]
    public void Resolve_DealsTwoDamage_ToEveryCreature_ExceptCasterFlyers()
    {
        // _alice is the caster. Her flyer is exempt; everything else burns.
        var aliceGround = NewCreatureOnBattlefield(_alice, "Grizzly Bears", "{1}{G}", 2, 2);
        var aliceFlyer = NewCreatureOnBattlefield(_alice, "Wind Drake", "{2}{U}", 2, 2, flying: true);
        var bobGround = NewCreatureOnBattlefield(_bob, "Runeclaw Bear", "{1}{G}", 2, 2);
        var bobFlyer = NewCreatureOnBattlefield(_bob, "Snapping Drake", "{3}{U}", 3, 2, flying: true);

        var effects = FlameSweepFactory.BuildResolveEffect(new[] { _alice, _bob }, _alice);
        foreach (var e in effects) e.Execute();

        aliceGround.Damage.Should().Be(2, "the caster's non-flying creatures still take the sweep");
        aliceFlyer.Damage.Should().Be(0, "the caster's flying creatures are exempt");
        bobGround.Damage.Should().Be(2, "opponent creatures are damaged");
        bobFlyer.Damage.Should().Be(2, "opponent flyers are NOT exempt — only \"you control\" flyers are");
    }

    [Fact]
    public void Resolve_KillsTwoToughnessCreatures_AndLeavesBiggerOnesAlive()
    {
        var bears = NewCreatureOnBattlefield(_alice, "Grizzly Bears", "{1}{G}", 2, 2);
        var giant = NewCreatureOnBattlefield(_alice, "Hill Giant", "{3}{R}", 3, 3);
        var saproling = NewCreatureOnBattlefield(_bob, "Saproling", "{0}", 1, 1);

        var effects = FlameSweepFactory.BuildResolveEffect(new[] { _alice, _bob }, _alice);
        foreach (var e in effects) e.Execute();

        bears.IsDead().Should().BeTrue("2 damage on a 2/2 is lethal");
        saproling.IsDead().Should().BeTrue("2 damage on a 1/1 is lethal");
        giant.IsDead().Should().BeFalse("2 damage on a 3/3 is survivable");
        giant.Damage.Should().Be(2);
    }

    [Fact]
    public void Resolve_CasterFlyer_Survives_Untouched()
    {
        var flyer = NewCreatureOnBattlefield(_alice, "Storm Crow", "{1}{U}", 1, 2, flying: true);

        var effects = FlameSweepFactory.BuildResolveEffect(new[] { _alice }, _alice);
        foreach (var e in effects) e.Execute();

        flyer.Damage.Should().Be(0);
        flyer.IsDead().Should().BeFalse("the caster's flyer is exempt and takes no damage");
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

        var effects = FlameSweepFactory.BuildResolveEffect(new[] { _alice, _bob }, _alice);
        foreach (var e in effects) e.Execute();

        bear.Damage.Should().Be(2, "creatures take the sweep");
        artifact.Zone.Should().Be(ZoneType.Battlefield);
        land.Zone.Should().Be(ZoneType.Battlefield);
    }

    [Fact]
    public void Resolve_NoCreaturesAnywhere_IsCleanNoOp()
    {
        var effects = FlameSweepFactory.BuildResolveEffect(new[] { _alice, _bob }, _alice);
        var act = () => { foreach (var e in effects) e.Execute(); };

        act.Should().NotThrow();
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
