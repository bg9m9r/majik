using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Unit tests for <see cref="PyroclasmFactory"/>.
///
/// Card: Pyroclasm — Sorcery {1}{R} (Portal Second Age and reprints).
///   "Pyroclasm deals 2 damage to each creature."
///
/// Covers:
///   - Identity (name, type, mana cost, owner/controller).
///   - NamedCardFactory dispatch.
///   - Resolve dishes 2 damage to every creature on every supplied
///     player's battlefield, including opponents'.
///   - Resolve doesn't mistake non-creature permanents for creatures.
///   - 1-toughness creatures are marked lethal after the sweep.
///   - 3-toughness creatures survive the sweep (with 2 damage marked).
/// </summary>
public class PyroclasmTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void Pyroclasm_Identity()
    {
        var c = PyroclasmFactory.Create(_alice);

        c.Name.Should().Be("Pyroclasm");
        c.ManaCost.Should().Be("{1}{R}");
        c.HasType(CardType.Sorcery).Should().BeTrue();
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_Pyroclasm()
    {
        var card = NamedCardFactory.Create("Pyroclasm", _alice);

        card.Should().BeOfType<Sorcery>();
        card.Name.Should().Be("Pyroclasm");
        card.HasType(CardType.Sorcery).Should().BeTrue();
        card.ManaCost.Should().Be("{1}{R}");
        card.Owner.Should().BeSameAs(_alice);
    }

    // -----------------------------------------------------------------------
    // Resolve — sweep
    // -----------------------------------------------------------------------

    [Fact]
    public void Resolve_DealsTwoDamage_ToEveryCreature_AcrossBothPlayers()
    {
        var aliceBears = NewCreatureOnBattlefield(_alice, "Grizzly Bears", "{1}{G}", 2, 2);
        var aliceGiant = NewCreatureOnBattlefield(_alice, "Hill Giant", "{3}{R}", 3, 3);
        var bobBear = NewCreatureOnBattlefield(_bob, "Runeclaw Bear", "{1}{G}", 2, 2);
        var bobTitan = NewCreatureOnBattlefield(_bob, "Craw Wurm", "{4}{G}{G}", 6, 4);

        var effects = PyroclasmFactory.BuildResolveEffect(new[] { _alice, _bob });
        foreach (var e in effects) e.Execute();

        aliceBears.Damage.Should().Be(2, "Pyroclasm deals 2 to each creature");
        aliceGiant.Damage.Should().Be(2);
        bobBear.Damage.Should().Be(2, "opponent creatures are also damaged");
        bobTitan.Damage.Should().Be(2);
    }

    [Fact]
    public void Resolve_KillsTwoToughnessCreatures_AndLeavesBiggerOnesAlive()
    {
        // 2/2 takes lethal — Damage >= Toughness flags IsDead() for the SBA
        // pass to pick up. 3/3 survives with 2 marked damage.
        var bears = NewCreatureOnBattlefield(_alice, "Grizzly Bears", "{1}{G}", 2, 2);
        var giant = NewCreatureOnBattlefield(_alice, "Hill Giant", "{3}{R}", 3, 3);

        // 1/1 must die too.
        var saproling = NewCreatureOnBattlefield(_bob, "Saproling", "{0}", 1, 1);

        var effects = PyroclasmFactory.BuildResolveEffect(new[] { _alice, _bob });
        foreach (var e in effects) e.Execute();

        bears.IsDead().Should().BeTrue("2 damage on a 2/2 is lethal");
        saproling.IsDead().Should().BeTrue("2 damage on a 1/1 is lethal");
        giant.IsDead().Should().BeFalse("2 damage on a 3/3 is survivable");
        giant.Damage.Should().Be(2);
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

        var effects = PyroclasmFactory.BuildResolveEffect(new[] { _alice, _bob });
        foreach (var e in effects) e.Execute();

        bear.Damage.Should().Be(2, "creatures take the sweep");
        // Artifacts / lands have no Damage surface — sanity check that they
        // are still on the battlefield (the sweep didn't move them).
        artifact.Zone.Should().Be(ZoneType.Battlefield);
        land.Zone.Should().Be(ZoneType.Battlefield);
    }

    [Fact]
    public void Resolve_NoCreaturesAnywhere_IsCleanNoOp()
    {
        var effects = PyroclasmFactory.BuildResolveEffect(new[] { _alice, _bob });
        var act = () => { foreach (var e in effects) e.Execute(); };

        act.Should().NotThrow();
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static Creature NewCreatureOnBattlefield(
        Player owner, string name, string manaCost, int power, int toughness)
    {
        var c = new Creature(name, manaCost, power, toughness);
        c.SetOwner(owner);
        c.SetController(owner);
        owner.Zones.Battlefield.AddCard(c);
        c.SetZone(ZoneType.Battlefield);
        return c;
    }
}
