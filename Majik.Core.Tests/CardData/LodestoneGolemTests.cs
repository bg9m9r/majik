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
/// Tests for Lodestone Golem (Worldwake, Artifact Creature — Golem {4} 5/3).
///
/// Oracle:
///   "Nonartifact spells cost {1} more to cast."
///
/// Coverage:
///   * Identity: Artifact Creature — Golem {4} 5/3 named "Lodestone Golem"
///     with the nonartifact-spell cost-increase rider attached.
///   * Dispatch through <see cref="NamedCardFactory"/>.
///   * A nonartifact spell costs {1} more while Lodestone Golem is on the
///     battlefield (symmetric — applies to all players' nonartifact spells).
///   * Artifact spells are NOT taxed (CR 117.7 — predicate excludes artifacts).
///   * Coloured pips are untouched (CR 117.7c).
///   * Lodestone Golem leaves the battlefield → cost increase is inert.
///   * Two copies stack additively (each adds {1}).
///
/// Mirrors <see cref="ThaliaGuardianOfThrabenFactory"/>'s noncreature-spell
/// rider; the only difference is the predicate excludes Artifacts instead of
/// Creatures.
/// </summary>
public class LodestoneGolemTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void Create_HasCorrectCardShape()
    {
        var golem = LodestoneGolemFactory.Create(_alice);

        golem.Name.Should().Be("Lodestone Golem");
        golem.HasType(CardType.Artifact).Should().BeTrue();
        golem.HasType(CardType.Creature).Should().BeTrue();
        golem.HasSubtype(CardSubtype.Golem).Should().BeTrue();
        golem.ManaCost.Should().Be("{4}");
        golem.ManaCostValue.Generic.Should().Be(4);
        golem.Power.Should().Be(5);
        golem.Toughness.Should().Be(3);
        golem.Owner.Should().BeSameAs(_alice);
        golem.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void Create_HasSpellCostIncreaseAbility()
    {
        var golem = LodestoneGolemFactory.Create(_alice);

        golem.Abilities.OfType<SpellCostIncreaseAbility>()
            .Should().HaveCount(1,
                "the nonartifact-spell cost-increase rider must be attached");
    }

    [Fact]
    public void NamedCardFactory_Dispatch_ReturnsLodestoneGolemShape()
    {
        var card = NamedCardFactory.Create("Lodestone Golem", _alice);

        card.Should().BeOfType<Creature>();
        card.Name.Should().Be("Lodestone Golem");
        card.HasType(CardType.Artifact).Should().BeTrue();
        card.Abilities.OfType<SpellCostIncreaseAbility>().Should().HaveCount(1);
    }

    [Fact]
    public void Create_NullOwner_Throws()
    {
        Action act = () => LodestoneGolemFactory.Create(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    // -----------------------------------------------------------------------
    // Nonartifact spells cost {1} more (CR 117.7 / CR 601.2f)
    // -----------------------------------------------------------------------

    private static (Player alice, Player bob, Creature golem) SetupWithGolemOnBattlefield()
    {
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);

        var golem = LodestoneGolemFactory.Create(alice);
        alice.Zones.Battlefield.AddCard(golem);
        golem.SetZone(ZoneType.Battlefield);

        return (alice, bob, golem);
    }

    [Fact]
    public void NonartifactSpell_CostsOneMoreGeneric_WhileGolemIsOut()
    {
        var (alice, bob, _) = SetupWithGolemOnBattlefield();

        var counterspell = new Instant("Counterspell", "{U}{U}");
        counterspell.SetOwner(bob);
        counterspell.SetController(bob);

        var baseline = CostReduction.GetEffectiveCost(counterspell, bob);
        baseline.Generic.Should().Be(0, "baseline: no allPlayers, rider not scanned");

        var effective = CostReduction.GetEffectiveCost(
            counterspell, bob, new[] { alice, bob });
        effective.Generic.Should().Be(1, "Lodestone Golem adds {1} generic to nonartifact spells");
        effective.Blue.Should().Be(2, "coloured pips are untouched (CR 117.7c)");
        effective.TotalValue.Should().Be(3);
    }

    [Fact]
    public void CreatureSpell_AlsoCostsOneMore_NonartifactRider()
    {
        var (alice, bob, _) = SetupWithGolemOnBattlefield();

        var goblin = new Creature("Goblin Guide", "{R}", 2, 2);
        goblin.SetOwner(bob);
        goblin.SetController(bob);

        var effective = CostReduction.GetEffectiveCost(
            goblin, bob, new[] { alice, bob });

        effective.Generic.Should().Be(1,
            "a nonartifact creature spell is taxed by Lodestone Golem");
        effective.Red.Should().Be(1, "coloured pip unchanged");
        effective.TotalValue.Should().Be(2);
    }

    [Fact]
    public void ArtifactSpell_IsNotTaxed()
    {
        var (alice, bob, _) = SetupWithGolemOnBattlefield();

        // An artifact spell — must NOT be taxed (predicate excludes Artifacts).
        var bauble = new Artifact("Mishra's Bauble", "{0}");
        bauble.SetOwner(bob);
        bauble.SetController(bob);

        var effective = CostReduction.GetEffectiveCost(
            bauble, bob, new[] { alice, bob });

        effective.Generic.Should().Be(0,
            "artifact spells are not affected — Lodestone Golem taxes only nonartifact spells");
        effective.TotalValue.Should().Be(0);
    }

    [Fact]
    public void ControllerOwnSpell_AlsoCostsOneMore_Symmetric()
    {
        var (alice, bob, _) = SetupWithGolemOnBattlefield();

        var wrath = new Sorcery("Wrath of God", "{2}{W}{W}");
        wrath.SetOwner(alice);
        wrath.SetController(alice);

        var effective = CostReduction.GetEffectiveCost(
            wrath, alice, new[] { alice, bob });

        effective.Generic.Should().Be(3,
            "Lodestone Golem is symmetric — controller's own nonartifact spells cost {1} more too");
        effective.White.Should().Be(2, "coloured pips untouched");
        effective.TotalValue.Should().Be(5);
    }

    [Fact]
    public void GolemLeavesBattlefield_CostIncreaseBecomesInert()
    {
        var (alice, bob, golem) = SetupWithGolemOnBattlefield();

        alice.Zones.Battlefield.RemoveCard(golem);
        alice.Zones.Graveyard.AddCard(golem);
        golem.SetZone(ZoneType.Graveyard);

        var counterspell = new Instant("Counterspell", "{U}{U}");
        counterspell.SetOwner(bob);
        counterspell.SetController(bob);

        var effective = CostReduction.GetEffectiveCost(
            counterspell, bob, new[] { alice, bob });

        effective.Generic.Should().Be(0,
            "Lodestone Golem is no longer on the battlefield — rider must be inert");
        effective.TotalValue.Should().Be(2);
    }

    [Fact]
    public void TwoCopies_StackAdditively_EachAddsOne()
    {
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);

        var golem1 = LodestoneGolemFactory.Create(alice);
        alice.Zones.Battlefield.AddCard(golem1);
        golem1.SetZone(ZoneType.Battlefield);

        var golem2 = LodestoneGolemFactory.Create(bob);
        bob.Zones.Battlefield.AddCard(golem2);
        golem2.SetZone(ZoneType.Battlefield);

        var counterspell = new Instant("Counterspell", "{U}{U}");
        counterspell.SetOwner(bob);
        counterspell.SetController(bob);

        var effective = CostReduction.GetEffectiveCost(
            counterspell, bob, new[] { alice, bob });

        effective.Generic.Should().Be(2, "two Lodestone Golems each add {1}");
        effective.TotalValue.Should().Be(4);
    }
}
