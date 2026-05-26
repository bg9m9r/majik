using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Tests for Sphere of Resistance (Urza's Saga, Artifact {2}).
///
/// Oracle:
///   "Spells cost {1} more to cast."
///
/// Coverage:
///   * Identity: Artifact {2} named "Sphere of Resistance" with the spell
///     cost-increase rider attached.
///   * Dispatch through <see cref="NamedCardFactory"/>.
///   * Opponent's spells cost {1} more while Sphere of Resistance is on the
///     battlefield (symmetric — applies to all spells, all players).
///   * Coloured pips are untouched (CR 117.7c).
///   * Sphere of Resistance leaves the battlefield → cost increase is inert.
///   * Two copies stack additively (each adds {1}).
/// </summary>
public class SphereOfResistanceTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void Create_HasCorrectCardShape()
    {
        var sphere = SphereOfResistanceFactory.Create(_alice);

        sphere.Name.Should().Be("Sphere of Resistance");
        sphere.HasType(CardType.Artifact).Should().BeTrue();
        sphere.ManaCost.Should().Be("{2}");
        sphere.ManaCostValue.Generic.Should().Be(2);
        sphere.Owner.Should().BeSameAs(_alice);
        sphere.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void Create_HasSpellCostIncreaseAbility()
    {
        var sphere = SphereOfResistanceFactory.Create(_alice);

        sphere.Abilities.OfType<SpellCostIncreaseAbility>()
            .Should().HaveCount(1,
                "the spell cost-increase rider must be attached");
    }

    [Fact]
    public void NamedCardFactory_Dispatch_ReturnsSphereOfResistanceShape()
    {
        var card = NamedCardFactory.Create("Sphere of Resistance", _alice);

        card.Should().BeOfType<Artifact>();
        card.Name.Should().Be("Sphere of Resistance");
        card.Abilities.OfType<SpellCostIncreaseAbility>().Should().HaveCount(1);
    }

    [Fact]
    public void Create_NullOwner_Throws()
    {
        Action act = () => SphereOfResistanceFactory.Create(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    // -----------------------------------------------------------------------
    // Spells cost {1} more (CR 117.7 / CR 601.2f)
    // -----------------------------------------------------------------------

    private static (Player alice, Player bob, Artifact sphere) SetupWithSphereOnBattlefield()
    {
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);

        var sphere = SphereOfResistanceFactory.Create(alice);
        alice.Zones.Battlefield.AddCard(sphere);
        sphere.SetZone(ZoneType.Battlefield);

        return (alice, bob, sphere);
    }

    [Fact]
    public void OpponentSpell_CostsOneMoreGeneric_WhileSphereIsOut()
    {
        var (alice, bob, _) = SetupWithSphereOnBattlefield();

        var counterspell = new Instant("Counterspell", "{U}{U}");
        counterspell.SetOwner(bob);
        counterspell.SetController(bob);

        var baseline = CostReduction.GetEffectiveCost(counterspell, bob);
        baseline.Generic.Should().Be(0, "baseline: no allPlayers, rider not scanned");

        var effective = CostReduction.GetEffectiveCost(
            counterspell, bob, new[] { alice, bob });
        effective.Generic.Should().Be(1, "Sphere of Resistance adds {1} generic");
        effective.Blue.Should().Be(2, "coloured pips are untouched (CR 117.7c)");
        effective.TotalValue.Should().Be(3);
    }

    [Fact]
    public void CreatureSpell_AlsoCostsOneMore_UnconditionalRider()
    {
        var (alice, bob, _) = SetupWithSphereOnBattlefield();

        var goblin = new Creature("Goblin Guide", "{R}", 2, 2);
        goblin.SetOwner(bob);
        goblin.SetController(bob);

        var effective = CostReduction.GetEffectiveCost(
            goblin, bob, new[] { alice, bob });

        effective.Generic.Should().Be(1,
            "Sphere of Resistance taxes every spell, including creature spells");
        effective.Red.Should().Be(1, "coloured pip unchanged");
        effective.TotalValue.Should().Be(2);
    }

    [Fact]
    public void ControllerOwnSpell_AlsoCostsOneMore_Symmetric()
    {
        var (alice, bob, _) = SetupWithSphereOnBattlefield();

        var wrath = new Sorcery("Wrath of God", "{2}{W}{W}");
        wrath.SetOwner(alice);
        wrath.SetController(alice);

        var effective = CostReduction.GetEffectiveCost(
            wrath, alice, new[] { alice, bob });

        effective.Generic.Should().Be(3,
            "Sphere of Resistance is symmetric — controller's own spells cost {1} more too");
        effective.White.Should().Be(2, "coloured pips untouched");
        effective.TotalValue.Should().Be(5);
    }

    [Fact]
    public void SphereLeavesBattlefield_CostIncreaseBecomesInert()
    {
        var (alice, bob, sphere) = SetupWithSphereOnBattlefield();

        alice.Zones.Battlefield.RemoveCard(sphere);
        alice.Zones.Graveyard.AddCard(sphere);
        sphere.SetZone(ZoneType.Graveyard);

        var counterspell = new Instant("Counterspell", "{U}{U}");
        counterspell.SetOwner(bob);
        counterspell.SetController(bob);

        var effective = CostReduction.GetEffectiveCost(
            counterspell, bob, new[] { alice, bob });

        effective.Generic.Should().Be(0,
            "Sphere of Resistance is no longer on the battlefield — rider must be inert");
        effective.TotalValue.Should().Be(2);
    }

    [Fact]
    public void TwoCopies_StackAdditively_EachAddsOne()
    {
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);

        var sphere1 = SphereOfResistanceFactory.Create(alice);
        alice.Zones.Battlefield.AddCard(sphere1);
        sphere1.SetZone(ZoneType.Battlefield);

        var sphere2 = SphereOfResistanceFactory.Create(bob);
        bob.Zones.Battlefield.AddCard(sphere2);
        sphere2.SetZone(ZoneType.Battlefield);

        var counterspell = new Instant("Counterspell", "{U}{U}");
        counterspell.SetOwner(bob);
        counterspell.SetController(bob);

        var effective = CostReduction.GetEffectiveCost(
            counterspell, bob, new[] { alice, bob });

        effective.Generic.Should().Be(2, "two Spheres each add {1}");
        effective.TotalValue.Should().Be(4);
    }
}
