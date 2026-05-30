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
/// Unit tests for <see cref="FieryCannonadeFactory"/>.
///
/// Card: Fiery Cannonade — Instant {1}{R} (Magic Origins and reprints).
///   "Fiery Cannonade deals 2 damage to each non-Pirate creature."
///
/// Fiery Cannonade is the instant-speed, Pirate-sparing analogue of
/// <see cref="PyroclasmFactory"/> (the sorcery "2 damage to each creature"
/// sweeper). The only behavioural delta is the subtype exclusion: creatures
/// with the Pirate creature type (CR 205.3m) are not in the affected set.
///
/// Covers:
///   - Identity (name, Instant type, mana cost, owner/controller).
///   - NamedCardFactory dispatch yields an Instant.
///   - Resolve dishes 2 damage to every NON-Pirate creature on every supplied
///     player's battlefield, including opponents'.
///   - Pirates take zero damage from the sweep.
///   - 1/1 non-Pirate creatures are flagged lethal; 3/3 survive with 2 marked.
///   - Resolve doesn't mistake non-creature permanents for creatures.
/// </summary>
public class FieryCannonadeTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void FieryCannonade_Identity()
    {
        var c = FieryCannonadeFactory.Create(_alice);

        c.Name.Should().Be("Fiery Cannonade");
        c.ManaCost.Should().Be("{1}{R}");
        c.HasType(CardType.Instant).Should().BeTrue();
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_FieryCannonade()
    {
        var card = NamedCardFactory.Create("Fiery Cannonade", _alice);

        card.Should().BeOfType<Instant>();
        card.Name.Should().Be("Fiery Cannonade");
        card.HasType(CardType.Instant).Should().BeTrue();
        card.ManaCost.Should().Be("{1}{R}");
        card.Owner.Should().BeSameAs(_alice);
    }

    // -----------------------------------------------------------------------
    // Resolve — sweep
    // -----------------------------------------------------------------------

    [Fact]
    public void Resolve_DealsTwoDamage_ToEveryNonPirateCreature_AcrossBothPlayers()
    {
        var aliceBears = NewCreatureOnBattlefield(_alice, "Grizzly Bears", "{1}{G}", 2, 2);
        var aliceGiant = NewCreatureOnBattlefield(_alice, "Hill Giant", "{3}{R}", 3, 3);
        var bobBear = NewCreatureOnBattlefield(_bob, "Runeclaw Bear", "{1}{G}", 2, 2);
        var bobTitan = NewCreatureOnBattlefield(_bob, "Craw Wurm", "{4}{G}{G}", 6, 4);

        var effects = FieryCannonadeFactory.BuildResolveEffect(new[] { _alice, _bob });
        foreach (var e in effects) e.Execute();

        aliceBears.Damage.Should().Be(2, "Fiery Cannonade deals 2 to each non-Pirate creature");
        aliceGiant.Damage.Should().Be(2);
        bobBear.Damage.Should().Be(2, "opponent creatures are also damaged");
        bobTitan.Damage.Should().Be(2);
    }

    [Fact]
    public void Resolve_SparesPirates()
    {
        // CR 205.3m — the Pirate creature type. Non-Pirate creatures take 2;
        // Pirates take none.
        var bear = NewCreatureOnBattlefield(_alice, "Grizzly Bears", "{1}{G}", 2, 2);
        var ragavan = NewCreatureOnBattlefield(
            _bob, "Ragavan, Nimble Pilferer", "{R}", 2, 1,
            CardSubtype.Monkey, CardSubtype.Pirate);
        var freebooter = NewCreatureOnBattlefield(
            _alice, "Kitesail Freebooter", "{1}{B}", 1, 2,
            CardSubtype.Human, CardSubtype.Pirate);

        var effects = FieryCannonadeFactory.BuildResolveEffect(new[] { _alice, _bob });
        foreach (var e in effects) e.Execute();

        bear.Damage.Should().Be(2, "a non-Pirate creature takes the sweep");
        ragavan.Damage.Should().Be(0, "Pirates are excluded from the affected set");
        freebooter.Damage.Should().Be(0, "Pirates are excluded regardless of controller");
        ragavan.IsDead().Should().BeFalse("undamaged 2/1 Pirate survives");
    }

    [Fact]
    public void Resolve_KillsTwoToughnessNonPirates_AndLeavesBiggerOnesAlive()
    {
        var bears = NewCreatureOnBattlefield(_alice, "Grizzly Bears", "{1}{G}", 2, 2);
        var giant = NewCreatureOnBattlefield(_alice, "Hill Giant", "{3}{R}", 3, 3);
        var saproling = NewCreatureOnBattlefield(_bob, "Saproling", "{0}", 1, 1);

        var effects = FieryCannonadeFactory.BuildResolveEffect(new[] { _alice, _bob });
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

        var effects = FieryCannonadeFactory.BuildResolveEffect(new[] { _alice, _bob });
        foreach (var e in effects) e.Execute();

        bear.Damage.Should().Be(2, "creatures take the sweep");
        artifact.Zone.Should().Be(ZoneType.Battlefield);
        land.Zone.Should().Be(ZoneType.Battlefield);
    }

    [Fact]
    public void Resolve_NoCreaturesAnywhere_IsCleanNoOp()
    {
        var effects = FieryCannonadeFactory.BuildResolveEffect(new[] { _alice, _bob });
        var act = () => { foreach (var e in effects) e.Execute(); };

        act.Should().NotThrow();
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static Creature NewCreatureOnBattlefield(
        Player owner, string name, string manaCost, int power, int toughness,
        params CardSubtype[] subtypes)
    {
        var c = new Creature(name, manaCost, power, toughness,
            subtypes: subtypes.Length > 0 ? subtypes : null);
        c.SetOwner(owner);
        c.SetController(owner);
        owner.Zones.Battlefield.AddCard(c);
        c.SetZone(ZoneType.Battlefield);
        return c;
    }
}
