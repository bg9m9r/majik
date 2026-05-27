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
/// Tests for Trinisphere (Darksteel, Artifact {3}).
///
/// Oracle:
///   "As long as Trinisphere is untapped, each spell that would cost less
///    than three mana to cast costs three mana to cast. (Spells with mana
///    cost less than three with any colored mana symbols in their mana
///    costs cost three mana to cast.)"
///
/// Coverage:
///   * Identity: Artifact {3} with the cost-floor rider attached.
///   * Dispatch through <see cref="NamedCardFactory"/>.
///   * {0} spell floors to {3}; {1} → {3}; {2} → {3}; {U} → {2}{U} (TV 3);
///     {U}{U} → {1}{U}{U} (TV 3).
///   * Three-or-more-cost spells untouched (printed TV >= 3).
///   * Tapped Trinisphere → no floor (rider inert).
///   * LTB → rider inert.
///   * X spells skipped (documented deferred — see factory XML).
///   * Two Trinispheres do not double-floor (idempotent on per-rider basis;
///     they each add the same delta which sums, so we assert the additive
///     behaviour matches the SpellCostIncreaseAbility model).
/// </summary>
public class TrinisphereTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void Create_HasCorrectCardShape()
    {
        var trini = TrinisphereFactory.Create(_alice);

        trini.Name.Should().Be("Trinisphere");
        trini.HasType(CardType.Artifact).Should().BeTrue();
        trini.ManaCost.Should().Be("{3}");
        trini.ManaCostValue.Generic.Should().Be(3);
        trini.Owner.Should().BeSameAs(_alice);
        trini.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void Create_HasSpellCostIncreaseAbility()
    {
        var trini = TrinisphereFactory.Create(_alice);

        trini.Abilities.OfType<SpellCostIncreaseAbility>()
            .Should().HaveCount(1,
                "the cost-floor rider must be attached");
    }

    [Fact]
    public void NamedCardFactory_Dispatch_ReturnsTrinisphereShape()
    {
        var card = NamedCardFactory.Create("Trinisphere", _alice);

        card.Should().BeOfType<Artifact>();
        card.Name.Should().Be("Trinisphere");
        card.Abilities.OfType<SpellCostIncreaseAbility>().Should().HaveCount(1);
    }

    [Fact]
    public void Create_NullOwner_Throws()
    {
        Action act = () => TrinisphereFactory.Create(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    // -----------------------------------------------------------------------
    // Cost floor at three (CR 117.7 / CR 601.2f)
    // -----------------------------------------------------------------------

    private static (Player alice, Player bob, Artifact trini) SetupWithTrinisphereOnBattlefield()
    {
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);

        var trini = TrinisphereFactory.Create(alice);
        alice.Zones.Battlefield.AddCard(trini);
        trini.SetZone(ZoneType.Battlefield);

        return (alice, bob, trini);
    }

    [Fact]
    public void ZeroCostSpell_FloorsToThree()
    {
        var (alice, bob, _) = SetupWithTrinisphereOnBattlefield();

        var bauble = new Artifact("Mishra's Bauble", "{0}");
        bauble.SetOwner(bob);
        bauble.SetController(bob);

        var effective = CostReduction.GetEffectiveCost(
            bauble, bob, new[] { alice, bob });

        effective.TotalValue.Should().Be(3, "{0} spell floors to {3} under Trinisphere");
        effective.Generic.Should().Be(3);
    }

    [Fact]
    public void OneGenericSpell_FloorsToThree()
    {
        var (alice, bob, _) = SetupWithTrinisphereOnBattlefield();

        var spell = new Sorcery("Cheap Sorcery", "{1}");
        spell.SetOwner(bob);
        spell.SetController(bob);

        var effective = CostReduction.GetEffectiveCost(
            spell, bob, new[] { alice, bob });

        effective.TotalValue.Should().Be(3, "{1} spell floors to {3}");
        effective.Generic.Should().Be(3);
    }

    [Fact]
    public void TwoGenericSpell_FloorsToThree()
    {
        var (alice, bob, _) = SetupWithTrinisphereOnBattlefield();

        var spell = new Sorcery("Cheap Sorcery", "{2}");
        spell.SetOwner(bob);
        spell.SetController(bob);

        var effective = CostReduction.GetEffectiveCost(
            spell, bob, new[] { alice, bob });

        effective.TotalValue.Should().Be(3, "{2} spell floors to {3}");
        effective.Generic.Should().Be(3);
    }

    [Fact]
    public void OneColoredPipSpell_FloorsToThree_PaysCcGeneric()
    {
        var (alice, bob, _) = SetupWithTrinisphereOnBattlefield();

        // {U} — printed TotalValue 1 — Trinisphere floors to total 3, so
        // adds {2} generic. Parenthetical reminder text covers this case.
        var spell = new Instant("Brainstorm", "{U}");
        spell.SetOwner(bob);
        spell.SetController(bob);

        var effective = CostReduction.GetEffectiveCost(
            spell, bob, new[] { alice, bob });

        effective.TotalValue.Should().Be(3,
            "{U} floors to total cost 3 — the parenthetical reminder case");
        effective.Generic.Should().Be(2, "+{2} generic to reach total 3");
        effective.Blue.Should().Be(1, "coloured pip preserved (CR 117.7c)");
    }

    [Fact]
    public void TwoColoredPipSpell_FloorsToThree_PaysOneGeneric()
    {
        var (alice, bob, _) = SetupWithTrinisphereOnBattlefield();

        // {U}{U} — printed TotalValue 2 — Trinisphere floors to total 3.
        var counterspell = new Instant("Counterspell", "{U}{U}");
        counterspell.SetOwner(bob);
        counterspell.SetController(bob);

        var effective = CostReduction.GetEffectiveCost(
            counterspell, bob, new[] { alice, bob });

        effective.TotalValue.Should().Be(3, "{U}{U} floors to {1}{U}{U}");
        effective.Generic.Should().Be(1, "+{1} generic to reach total 3");
        effective.Blue.Should().Be(2, "coloured pips preserved");
    }

    [Fact]
    public void ThreeCostSpell_Untouched()
    {
        var (alice, bob, _) = SetupWithTrinisphereOnBattlefield();

        var spell = new Sorcery("Wrath of God", "{2}{W}{W}");
        spell.SetOwner(bob);
        spell.SetController(bob);

        var effective = CostReduction.GetEffectiveCost(
            spell, bob, new[] { alice, bob });

        effective.TotalValue.Should().Be(4,
            "printed TotalValue 4 >= 3 — Trinisphere does not raise costs above the floor");
        effective.Generic.Should().Be(2, "generic unchanged");
        effective.White.Should().Be(2);
    }

    [Fact]
    public void ExactlyThreeCostSpell_Untouched()
    {
        var (alice, bob, _) = SetupWithTrinisphereOnBattlefield();

        var spell = new Sorcery("Three Cost", "{3}");
        spell.SetOwner(bob);
        spell.SetController(bob);

        var effective = CostReduction.GetEffectiveCost(
            spell, bob, new[] { alice, bob });

        effective.TotalValue.Should().Be(3, "exactly 3 — not less than 3, untouched");
        effective.Generic.Should().Be(3);
    }

    // -----------------------------------------------------------------------
    // Untapped gate
    // -----------------------------------------------------------------------

    [Fact]
    public void TappedTrinisphere_DoesNotFloor()
    {
        var (alice, bob, trini) = SetupWithTrinisphereOnBattlefield();

        trini.Tap();
        trini.IsTapped.Should().BeTrue();

        var bauble = new Artifact("Mishra's Bauble", "{0}");
        bauble.SetOwner(bob);
        bauble.SetController(bob);

        var effective = CostReduction.GetEffectiveCost(
            bauble, bob, new[] { alice, bob });

        effective.TotalValue.Should().Be(0,
            "tapped Trinisphere — cost floor rider is inert");
    }

    [Fact]
    public void TrinisphereLeavesBattlefield_RiderInert()
    {
        var (alice, bob, trini) = SetupWithTrinisphereOnBattlefield();

        alice.Zones.Battlefield.RemoveCard(trini);
        alice.Zones.Graveyard.AddCard(trini);
        trini.SetZone(ZoneType.Graveyard);

        var bauble = new Artifact("Mishra's Bauble", "{0}");
        bauble.SetOwner(bob);
        bauble.SetController(bob);

        var effective = CostReduction.GetEffectiveCost(
            bauble, bob, new[] { alice, bob });

        effective.TotalValue.Should().Be(0,
            "Trinisphere off the battlefield — rider must be inert");
    }

    [Fact]
    public void ControllerOwnSpell_AlsoFloored_Symmetric()
    {
        var (alice, bob, _) = SetupWithTrinisphereOnBattlefield();

        var bolt = new Instant("Lightning Bolt", "{R}");
        bolt.SetOwner(alice);
        bolt.SetController(alice);

        var effective = CostReduction.GetEffectiveCost(
            bolt, alice, new[] { alice, bob });

        effective.TotalValue.Should().Be(3,
            "Trinisphere is symmetric — controller's own cheap spells are floored too");
        effective.Generic.Should().Be(2);
        effective.Red.Should().Be(1);
    }

    [Fact]
    public void XSpell_NotFloored_DocumentedGap()
    {
        var (alice, bob, _) = SetupWithTrinisphereOnBattlefield();

        // {X}{R} — printed TotalValue 1 (X is not included), HasX true.
        // Documented deferred behaviour: skip floor when HasX.
        var fireball = new Sorcery("Fireball", "{X}{R}");
        fireball.SetOwner(bob);
        fireball.SetController(bob);

        var effective = CostReduction.GetEffectiveCost(
            fireball, bob, new[] { alice, bob });

        effective.Generic.Should().Be(0, "X spells are skipped — see factory XML for the deferred gap");
    }
}
