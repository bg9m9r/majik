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

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Tests for Vryn Wingmare (Magic Origins, {2}{W}).
///
/// Oracle (verified Scryfall 2026-06-02):
///   "Flying
///    Noncreature spells cost {1} more to cast."
///
/// Vryn Wingmare is a functional reprint of Thalia, Guardian of Thraben —
/// the identical "noncreature spells cost {1} more" static, with Flying
/// instead of First strike (and a 2/1 White body, not Legendary).
///
/// Coverage mirrors <see cref="ThaliaGuardianOfThrabenTests"/>:
///   * Identity: 2/1 Pegasus {2}{W} with Flying + the noncreature-spell
///     cost-increase rider attached.
///   * Dispatch through <see cref="NamedCardFactory"/>.
///   * Opponent's noncreature spell costs {1} more while Wingmare is out.
///   * Creature spells are NOT affected.
///   * Wingmare leaves the battlefield → cost increase becomes inert.
///   * Symmetric — controller's own noncreature spells cost {1} more.
///   * Artifact spell (noncreature) is taxed.
/// </summary>
public class VrynWingmareTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void Create_HasCorrectCardShape()
    {
        var wingmare = VrynWingmareFactory.Create(_alice);

        wingmare.Name.Should().Be("Vryn Wingmare");
        wingmare.HasType(CardType.Creature).Should().BeTrue();
        wingmare.HasSubtype(CardSubtype.Pegasus).Should().BeTrue();
        wingmare.ManaCost.Should().Be("{2}{W}");
        wingmare.ManaCostValue.Generic.Should().Be(2);
        wingmare.ManaCostValue.White.Should().Be(1);
        wingmare.Power.Should().Be(2);
        wingmare.Toughness.Should().Be(1);
        wingmare.Owner.Should().BeSameAs(_alice);
        wingmare.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void Create_HasFlyingKeyword()
    {
        var wingmare = VrynWingmareFactory.Create(_alice);

        wingmare.Abilities.OfType<KeywordAbility>()
            .Should().ContainSingle(k =>
                k.Keyword.Equals("Flying", StringComparison.OrdinalIgnoreCase),
                "CR 702.9 — Flying keyword marker must be attached");
    }

    [Fact]
    public void Create_HasSpellCostIncreaseAbility()
    {
        var wingmare = VrynWingmareFactory.Create(_alice);

        wingmare.Abilities.OfType<SpellCostIncreaseAbility>()
            .Should().HaveCount(1, "the noncreature-spell cost-increase rider must be attached");
    }

    [Fact]
    public void NamedCardFactory_Dispatch_ReturnsWingmareShape()
    {
        var card = NamedCardFactory.Create("Vryn Wingmare", _alice);

        card.Should().BeOfType<Creature>();
        card.Name.Should().Be("Vryn Wingmare");
        card.HasSubtype(CardSubtype.Pegasus).Should().BeTrue();
        card.Abilities.OfType<SpellCostIncreaseAbility>().Should().HaveCount(1);
    }

    // -----------------------------------------------------------------------
    // Noncreature spells cost {1} more (CR 117.7 / CR 601.2f)
    // -----------------------------------------------------------------------

    private static (Player alice, Player bob, Creature wingmare) SetupWithWingmareOnBattlefield()
    {
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);

        var wingmare = VrynWingmareFactory.Create(alice);
        alice.Zones.Battlefield.AddCard(wingmare);
        wingmare.SetZone(ZoneType.Battlefield);

        return (alice, bob, wingmare);
    }

    [Fact]
    public void OpponentNoncreatureSpell_CostsOneMoreGeneric_WhileWingmareIsOut()
    {
        var (alice, bob, _) = SetupWithWingmareOnBattlefield();

        var counterspell = new Instant("Counterspell", "{U}{U}");
        counterspell.SetOwner(bob);
        counterspell.SetController(bob);

        // Without allPlayers (baseline — no Wingmare scan).
        var baseline = CostReduction.GetEffectiveCost(counterspell, bob);
        baseline.Generic.Should().Be(0, "baseline: no allPlayers, Wingmare rider not scanned");
        baseline.Blue.Should().Be(2);

        // With allPlayers supplied, Wingmare is found on Alice's battlefield.
        var effective = CostReduction.GetEffectiveCost(
            counterspell, bob, new[] { alice, bob });
        effective.Generic.Should().Be(1, "Wingmare adds {1} generic to noncreature spells");
        effective.Blue.Should().Be(2, "coloured pips are untouched (CR 117.7c)");
        effective.TotalValue.Should().Be(3);
    }

    [Fact]
    public void CreatureSpells_AreNotAffected_ByWingmareRider()
    {
        var (alice, bob, _) = SetupWithWingmareOnBattlefield();

        var goblin = new Creature("Goblin Guide", "{R}", 2, 2);
        goblin.SetOwner(bob);
        goblin.SetController(bob);

        var effective = CostReduction.GetEffectiveCost(
            goblin, bob, new[] { alice, bob });

        effective.Generic.Should().Be(0, "creature spells are not affected by Wingmare's rider");
        effective.Red.Should().Be(1, "coloured pip unchanged");
        effective.TotalValue.Should().Be(1, "no cost increase for creature spells");
    }

    [Fact]
    public void WingmareLeavesBattlefield_CostIncreaseBecomesInert()
    {
        var (alice, bob, wingmare) = SetupWithWingmareOnBattlefield();

        alice.Zones.Battlefield.RemoveCard(wingmare);
        alice.Zones.Graveyard.AddCard(wingmare);
        wingmare.SetZone(ZoneType.Graveyard);

        var counterspell = new Instant("Counterspell", "{U}{U}");
        counterspell.SetOwner(bob);
        counterspell.SetController(bob);

        var effective = CostReduction.GetEffectiveCost(
            counterspell, bob, new[] { alice, bob });

        effective.Generic.Should().Be(0,
            "Wingmare is no longer on the battlefield — rider must be inert");
        effective.Blue.Should().Be(2);
        effective.TotalValue.Should().Be(2, "printed cost stands when Wingmare is gone");
    }

    [Fact]
    public void ControllerOwnNoncreatureSpell_AlsoCostsOneMore_Symmetric()
    {
        var (alice, bob, _) = SetupWithWingmareOnBattlefield();

        var wrath = new Sorcery("Wrath of God", "{2}{W}{W}");
        wrath.SetOwner(alice);
        wrath.SetController(alice);

        var effective = CostReduction.GetEffectiveCost(
            wrath, alice, new[] { alice, bob });

        effective.Generic.Should().Be(3,
            "Wingmare is symmetric — controller's own noncreature spells cost {1} more too");
        effective.White.Should().Be(2, "coloured pips untouched");
        effective.TotalValue.Should().Be(5);
    }

    [Fact]
    public void ArtifactSpell_CostsOneMore_NoncreatureType()
    {
        var (alice, bob, _) = SetupWithWingmareOnBattlefield();

        var baubleSpell = new Artifact("Mishra's Bauble", "{0}");
        baubleSpell.SetOwner(bob);
        baubleSpell.SetController(bob);

        var effective = CostReduction.GetEffectiveCost(
            baubleSpell, bob, new[] { alice, bob });

        effective.Generic.Should().Be(1,
            "artifact spells are noncreature spells — Wingmare's rider applies");
    }
}
