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
/// Tests for Thalia, Guardian of Thraben (Dark Ascension, {1}{W}).
///
/// Oracle:
///   "First strike.
///    Noncreature spells cost {1} more to cast."
///
/// Coverage:
///   * Identity: Legendary 2/1 Human Soldier {1}{W} with First Strike and
///     the noncreature-spell cost-increase rider attached.
///   * Dispatch through <see cref="NamedCardFactory"/>.
///   * Opponent's noncreature spell costs {1} more while Thalia is on the
///     battlefield.
///   * Creature spells are NOT affected — predicate restricts to
///     !HasType(CardType.Creature).
///   * Thalia leaves the battlefield → cost increase is inert (no rider
///     fires when she is not on the battlefield).
///   * Symmetric — controller's own noncreature spells also cost {1} more.
/// </summary>
public class ThaliaGuardianOfThrabenTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void Create_HasCorrectCardShape()
    {
        var thalia = ThaliaGuardianOfThrabenFactory.Create(_alice);

        thalia.Name.Should().Be("Thalia, Guardian of Thraben");
        thalia.HasType(CardType.Creature).Should().BeTrue();
        thalia.HasSupertype(CardSupertype.Legendary).Should().BeTrue();
        thalia.HasSubtype(CardSubtype.Human).Should().BeTrue();
        thalia.HasSubtype(CardSubtype.Soldier).Should().BeTrue();
        thalia.ManaCost.Should().Be("{1}{W}");
        thalia.ManaCostValue.Generic.Should().Be(1);
        thalia.ManaCostValue.White.Should().Be(1);
        thalia.Power.Should().Be(2);
        thalia.Toughness.Should().Be(1);
        thalia.Owner.Should().BeSameAs(_alice);
        thalia.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void Create_HasFirstStrikeKeyword()
    {
        var thalia = ThaliaGuardianOfThrabenFactory.Create(_alice);

        thalia.Abilities.OfType<KeywordAbility>()
            .Should().ContainSingle(k =>
                k.Keyword.Equals("First strike", StringComparison.OrdinalIgnoreCase),
                "CR 702.7 — First strike keyword marker must be attached");
    }

    [Fact]
    public void Create_HasSpellCostIncreaseAbility()
    {
        var thalia = ThaliaGuardianOfThrabenFactory.Create(_alice);

        thalia.Abilities.OfType<SpellCostIncreaseAbility>()
            .Should().HaveCount(1, "the noncreature-spell cost-increase rider must be attached");
    }

    [Fact]
    public void NamedCardFactory_Dispatch_ReturnsThaliaShape()
    {
        var card = NamedCardFactory.Create("Thalia, Guardian of Thraben", _alice);

        card.Should().BeOfType<Creature>();
        card.Name.Should().Be("Thalia, Guardian of Thraben");
        card.HasSupertype(CardSupertype.Legendary).Should().BeTrue();
        card.Abilities.OfType<SpellCostIncreaseAbility>().Should().HaveCount(1);
    }

    // -----------------------------------------------------------------------
    // Noncreature spells cost {1} more (CR 117.7 / CR 601.2f)
    // -----------------------------------------------------------------------

    private static (Player alice, Player bob, Creature thalia) SetupWithThaliaOnBattlefield()
    {
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);

        var thalia = ThaliaGuardianOfThrabenFactory.Create(alice);
        alice.Zones.Battlefield.AddCard(thalia);
        thalia.SetZone(ZoneType.Battlefield);

        return (alice, bob, thalia);
    }

    [Fact]
    public void OpponentNoncreatureSpell_CostsOneMoreGeneric_WhileThaliaIsOut()
    {
        var (alice, bob, _) = SetupWithThaliaOnBattlefield();

        // A {1}{U} instant — Bob wants to cast this.
        var counterspell = new Instant("Counterspell", "{U}{U}");
        counterspell.SetOwner(bob);
        counterspell.SetController(bob);

        // Without allPlayers (baseline — no Thalia scan).
        var baseline = CostReduction.GetEffectiveCost(counterspell, bob);
        baseline.Generic.Should().Be(0, "baseline: no allPlayers, Thalia rider not scanned");
        baseline.Blue.Should().Be(2);

        // With allPlayers supplied, Thalia is found on Alice's battlefield.
        var effective = CostReduction.GetEffectiveCost(
            counterspell, bob, new[] { alice, bob });
        effective.Generic.Should().Be(1, "Thalia adds {1} generic to noncreature spells");
        effective.Blue.Should().Be(2, "coloured pips are untouched (CR 117.7c)");
        effective.TotalValue.Should().Be(3);
    }

    [Fact]
    public void CreatureSpells_AreNotAffected_ByThaliaRider()
    {
        var (alice, bob, _) = SetupWithThaliaOnBattlefield();

        // A creature spell Bob wants to cast.
        var goblin = new Creature("Goblin Guide", "{R}", 2, 2);
        goblin.SetOwner(bob);
        goblin.SetController(bob);

        var effective = CostReduction.GetEffectiveCost(
            goblin, bob, new[] { alice, bob });

        effective.Generic.Should().Be(0, "creature spells are not affected by Thalia's rider");
        effective.Red.Should().Be(1, "coloured pip unchanged");
        effective.TotalValue.Should().Be(1, "no cost increase for creature spells");
    }

    [Fact]
    public void ThaliaLeavesBattlefield_CostIncreaseBecomesInert()
    {
        var (alice, bob, thalia) = SetupWithThaliaOnBattlefield();

        // Move Thalia off the battlefield.
        alice.Zones.Battlefield.RemoveCard(thalia);
        alice.Zones.Graveyard.AddCard(thalia);
        thalia.SetZone(ZoneType.Graveyard);

        var counterspell = new Instant("Counterspell", "{U}{U}");
        counterspell.SetOwner(bob);
        counterspell.SetController(bob);

        var effective = CostReduction.GetEffectiveCost(
            counterspell, bob, new[] { alice, bob });

        effective.Generic.Should().Be(0,
            "Thalia is no longer on the battlefield — rider must be inert");
        effective.Blue.Should().Be(2);
        effective.TotalValue.Should().Be(2, "printed cost stands when Thalia is gone");
    }

    [Fact]
    public void ControllerOwnNoncreatureSpell_AlsoCotsOneMore_Symmetric()
    {
        var (alice, bob, _) = SetupWithThaliaOnBattlefield();

        // Alice (Thalia's controller) casts a noncreature spell herself.
        var wrath = new Sorcery("Wrath of God", "{2}{W}{W}");
        wrath.SetOwner(alice);
        wrath.SetController(alice);

        var effective = CostReduction.GetEffectiveCost(
            wrath, alice, new[] { alice, bob });

        effective.Generic.Should().Be(3,
            "Thalia is symmetric — controller's own noncreature spells cost {1} more too");
        effective.White.Should().Be(2, "coloured pips untouched");
        effective.TotalValue.Should().Be(5);
    }

    [Fact]
    public void ArtifactSpell_CostsOneMore_NoncreatureType()
    {
        var (alice, bob, _) = SetupWithThaliaOnBattlefield();

        // An artifact spell (noncreature) that Bob casts.
        var baubleSpell = new Artifact("Mishra's Bauble", "{0}");
        baubleSpell.SetOwner(bob);
        baubleSpell.SetController(bob);

        var effective = CostReduction.GetEffectiveCost(
            baubleSpell, bob, new[] { alice, bob });

        effective.Generic.Should().Be(1,
            "artifact spells are noncreature spells — Thalia's rider applies");
    }
}
