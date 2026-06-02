using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for Thorn of Amethyst (Future Sight, Artifact {2}).
///
/// Oracle:
///   "Noncreature spells cost {1} more to cast."
///
/// Coverage:
///   * Identity: Artifact {2} with the noncreature-spell cost-increase rider.
///   * Dispatch through <see cref="NamedCardFactory"/>.
///   * Opponent's noncreature spell costs {1} more while Thorn is on the
///     battlefield.
///   * Creature spells are NOT affected — predicate restricts to
///     !HasType(CardType.Creature).
///   * Thorn leaves the battlefield → cost increase is inert.
///   * Symmetric — controller's own noncreature spells also cost {1} more.
///   * Coloured pips untouched (CR 117.7c).
/// </summary>
public class ThornOfAmethystTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void Create_HasCorrectCardShape()
    {
        var thorn = ThornOfAmethystFactory.Create(_alice);

        thorn.Name.Should().Be("Thorn of Amethyst");
        thorn.HasType(CardType.Artifact).Should().BeTrue();
        thorn.HasType(CardType.Creature).Should().BeFalse();
        thorn.ManaCost.Should().Be("{2}");
        thorn.ManaCostValue.Generic.Should().Be(2);
        thorn.Owner.Should().BeSameAs(_alice);
        thorn.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void Create_HasSpellCostIncreaseAbility()
    {
        var thorn = ThornOfAmethystFactory.Create(_alice);

        thorn.Abilities.OfType<SpellCostIncreaseAbility>()
            .Should().HaveCount(1, "the noncreature-spell cost-increase rider must be attached");
    }

    [Fact]
    public void NamedCardFactory_Dispatch_ReturnsThornShape()
    {
        var card = NamedCardFactory.Create("Thorn of Amethyst", _alice);

        card.Should().BeOfType<Artifact>();
        card.Name.Should().Be("Thorn of Amethyst");
        card.Abilities.OfType<SpellCostIncreaseAbility>().Should().HaveCount(1);
    }

    // -----------------------------------------------------------------------
    // Noncreature spells cost {1} more (CR 117.7 / CR 601.2f)
    // -----------------------------------------------------------------------

    private static (Player alice, Player bob, Artifact thorn) SetupWithThornOnBattlefield()
    {
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);

        var thorn = ThornOfAmethystFactory.Create(alice);
        alice.Zones.Battlefield.AddCard(thorn);
        thorn.SetZone(ZoneType.Battlefield);

        return (alice, bob, thorn);
    }

    [Fact]
    public void OpponentNoncreatureSpell_CostsOneMoreGeneric_WhileThornIsOut()
    {
        var (alice, bob, _) = SetupWithThornOnBattlefield();

        var counterspell = new Instant("Counterspell", "{U}{U}");
        counterspell.SetOwner(bob);
        counterspell.SetController(bob);

        // Without allPlayers (baseline — no Thorn scan).
        var baseline = CostReduction.GetEffectiveCost(counterspell, bob);
        baseline.Generic.Should().Be(0, "baseline: no allPlayers, Thorn rider not scanned");
        baseline.Blue.Should().Be(2);

        // With allPlayers supplied, Thorn is found on Alice's battlefield.
        var effective = CostReduction.GetEffectiveCost(
            counterspell, bob, new[] { alice, bob });
        effective.Generic.Should().Be(1, "Thorn adds {1} generic to noncreature spells");
        effective.Blue.Should().Be(2, "coloured pips are untouched (CR 117.7c)");
        effective.TotalValue.Should().Be(3);
    }

    [Fact]
    public void CreatureSpells_AreNotAffected_ByThornRider()
    {
        var (alice, bob, _) = SetupWithThornOnBattlefield();

        var goblin = new Creature("Goblin Guide", "{R}", 2, 2);
        goblin.SetOwner(bob);
        goblin.SetController(bob);

        var effective = CostReduction.GetEffectiveCost(
            goblin, bob, new[] { alice, bob });

        effective.Generic.Should().Be(0, "creature spells are not affected by Thorn's rider");
        effective.Red.Should().Be(1, "coloured pip unchanged");
        effective.TotalValue.Should().Be(1, "no cost increase for creature spells");
    }

    [Fact]
    public void ThornLeavesBattlefield_CostIncreaseBecomesInert()
    {
        var (alice, bob, thorn) = SetupWithThornOnBattlefield();

        // Move Thorn off the battlefield.
        alice.Zones.Battlefield.RemoveCard(thorn);
        alice.Zones.Graveyard.AddCard(thorn);
        thorn.SetZone(ZoneType.Graveyard);

        var counterspell = new Instant("Counterspell", "{U}{U}");
        counterspell.SetOwner(bob);
        counterspell.SetController(bob);

        var effective = CostReduction.GetEffectiveCost(
            counterspell, bob, new[] { alice, bob });

        effective.Generic.Should().Be(0,
            "Thorn is no longer on the battlefield — rider must be inert");
        effective.Blue.Should().Be(2);
        effective.TotalValue.Should().Be(2, "printed cost stands when Thorn is gone");
    }

    [Fact]
    public void ControllerOwnNoncreatureSpell_AlsoCostsOneMore_Symmetric()
    {
        var (alice, bob, _) = SetupWithThornOnBattlefield();

        var wrath = new Sorcery("Wrath of God", "{2}{W}{W}");
        wrath.SetOwner(alice);
        wrath.SetController(alice);

        var effective = CostReduction.GetEffectiveCost(
            wrath, alice, new[] { alice, bob });

        effective.Generic.Should().Be(3,
            "Thorn is symmetric — controller's own noncreature spells cost {1} more too");
        effective.White.Should().Be(2, "coloured pips untouched");
        effective.TotalValue.Should().Be(5);
    }

    [Fact]
    public void ArtifactSpell_CostsOneMore_NoncreatureType()
    {
        var (alice, bob, _) = SetupWithThornOnBattlefield();

        var baubleSpell = new Artifact("Mishra's Bauble", "{0}");
        baubleSpell.SetOwner(bob);
        baubleSpell.SetController(bob);

        var effective = CostReduction.GetEffectiveCost(
            baubleSpell, bob, new[] { alice, bob });

        effective.Generic.Should().Be(1,
            "artifact spells are noncreature spells — Thorn's rider applies");
    }
}
