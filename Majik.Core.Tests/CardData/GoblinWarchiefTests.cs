using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Combat;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Unit tests for <see cref="GoblinWarchiefFactory"/>.
///
/// Covers:
/// - Identity (name, mana cost, Goblin + Warrior subtypes, 2/2,
///   owner/controller).
/// - NamedCardFactory dispatch (returns the cost-reduction-only shell).
/// - Cost-reduction rider (CR 117.7):
///   * Goblin creature spell — generic reduced by 1.
///   * Non-Goblin creature spell — no reduction.
///   * Floor-at-zero for {R} Goblin (Mogg Fanatic shape).
///   * Two Warchiefs stack — additive.
///   * Opponent controls Warchief — no discount on your spells.
///   * Off-battlefield Warchief — no discount.
/// - Haste-grant static effect:
///   * Goblin creature controller's other Goblins gain Haste.
///   * Includes Warchief itself (oracle text has no "other" clause).
///   * Non-Goblin doesn't gain Haste.
///   * Opponent's Goblin doesn't gain Haste (controller-scoped).
/// </summary>
public class GoblinWarchiefTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    private static void PutOnBattlefield(Player owner, Card card)
    {
        owner.Zones.Battlefield.AddCard(card);
        card.SetZone(ZoneType.Battlefield);
    }

    // -------------------------------------------------------------------------
    // Identity + dispatch
    // -------------------------------------------------------------------------

    [Fact]
    public void GoblinWarchief_Identity()
    {
        var c = GoblinWarchiefFactory.Create(_alice);

        c.Name.Should().Be("Goblin Warchief");
        c.ManaCost.Should().Be("{1}{R}{R}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Goblin).Should().BeTrue();
        c.HasSubtype(CardSubtype.Warrior).Should().BeTrue();
        c.BasePower.Should().Be(2);
        c.BaseToughness.Should().Be(2);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);

        c.Abilities.OfType<SpellCostReductionAbility>()
            .Should().HaveCount(1, "the Goblin-cost-reduction rider is attached.");
    }

    [Fact]
    public void GoblinWarchief_DispatchesViaNamedCardFactory()
    {
        var c = NamedCardFactory.Create("Goblin Warchief", _alice);

        c.Should().BeOfType<Creature>();
        c.Name.Should().Be("Goblin Warchief");
        c.HasSubtype(CardSubtype.Goblin).Should().BeTrue();
        c.HasSubtype(CardSubtype.Warrior).Should().BeTrue();
        c.Abilities.OfType<SpellCostReductionAbility>().Should().HaveCount(1);
    }

    // -------------------------------------------------------------------------
    // Cost-reduction rider — "Goblin spells you cast cost {1} less"
    // -------------------------------------------------------------------------

    [Fact]
    public void GoblinSpell_GenericReducedByOne()
    {
        var warchief = GoblinWarchiefFactory.Create(_alice);
        PutOnBattlefield(_alice, warchief);

        // Goblin Piledriver shape — {1}{R} 1/2 Goblin Warrior.
        var goblinSpell = new Creature("Test Goblin", "{2}{R}", 2, 2,
            subtypes: new[] { CardSubtype.Goblin });
        goblinSpell.SetOwner(_alice);
        goblinSpell.SetController(_alice);

        var effective = CostReduction.GetEffectiveCost(goblinSpell, _alice);

        effective.Generic.Should().Be(1, "{2} generic reduced by 1 → {1}.");
        effective.Red.Should().Be(1, "coloured pip untouched (CR 117.7c).");
    }

    [Fact]
    public void NonGoblinCreatureSpell_NoReduction()
    {
        var warchief = GoblinWarchiefFactory.Create(_alice);
        PutOnBattlefield(_alice, warchief);

        // A bear (not a Goblin) — the rider's predicate filters out non-
        // Goblin spells.
        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2,
            subtypes: new[] { CardSubtype.Bear });
        bear.SetOwner(_alice);
        bear.SetController(_alice);

        var effective = CostReduction.GetEffectiveCost(bear, _alice);

        effective.Generic.Should().Be(1, "non-Goblin spell — no Warchief discount.");
        effective.Green.Should().Be(1);
    }

    [Fact]
    public void TwoWarchiefs_StackReduction()
    {
        var w1 = GoblinWarchiefFactory.Create(_alice);
        var w2 = GoblinWarchiefFactory.Create(_alice);
        PutOnBattlefield(_alice, w1);
        PutOnBattlefield(_alice, w2);

        var goblinSpell = new Creature("Test Goblin", "{2}{R}", 2, 2,
            subtypes: new[] { CardSubtype.Goblin });
        goblinSpell.SetOwner(_alice);
        goblinSpell.SetController(_alice);

        var effective = CostReduction.GetEffectiveCost(goblinSpell, _alice);

        effective.Generic.Should().Be(0, "two Warchiefs reduce {2} → {0}.");
        effective.Red.Should().Be(1, "coloured pip untouched.");
    }

    [Fact]
    public void RedGoblin_FloorsAtZero_ColouredPipUntouched()
    {
        // Mogg Fanatic — {R}, 0 generic. Reducer can't drive coloured pip
        // below its printed minimum (CR 117.7c) and the generic bucket
        // floors at 0.
        var warchief = GoblinWarchiefFactory.Create(_alice);
        PutOnBattlefield(_alice, warchief);

        var fanatic = new Creature("Mogg Fanatic", "{R}", 1, 1,
            subtypes: new[] { CardSubtype.Goblin });
        fanatic.SetOwner(_alice);
        fanatic.SetController(_alice);

        var effective = CostReduction.GetEffectiveCost(fanatic, _alice);

        effective.Generic.Should().Be(0, "no generic to reduce — floor-at-zero (CR 117.7c).");
        effective.Red.Should().Be(1, "coloured pip untouched.");
        effective.TotalValue.Should().Be(1);
    }

    [Fact]
    public void OpponentControlsWarchief_DoesNotDiscountYourSpells()
    {
        var bobWarchief = GoblinWarchiefFactory.Create(_bob);
        PutOnBattlefield(_bob, bobWarchief);

        var aliceGoblin = new Creature("Test Goblin", "{2}{R}", 2, 2,
            subtypes: new[] { CardSubtype.Goblin });
        aliceGoblin.SetOwner(_alice);
        aliceGoblin.SetController(_alice);

        var effective = CostReduction.GetEffectiveCost(aliceGoblin, _alice);

        effective.Generic.Should().Be(2,
            "Bob's Warchief doesn't reduce Alice's spells — 'spells you cast' is " +
            "scoped to the controller of the reducer permanent.");
    }

    [Fact]
    public void OffBattlefield_NoReduction()
    {
        var warchief = GoblinWarchiefFactory.Create(_alice);
        _alice.Zones.Hand.AddCard(warchief);
        warchief.SetZone(ZoneType.Hand);

        var goblinSpell = new Creature("Test Goblin", "{2}{R}", 2, 2,
            subtypes: new[] { CardSubtype.Goblin });
        goblinSpell.SetOwner(_alice);
        goblinSpell.SetController(_alice);

        var effective = CostReduction.GetEffectiveCost(goblinSpell, _alice);

        effective.Generic.Should().Be(2,
            "Warchief isn't on the battlefield — no discount.");
    }

    // -------------------------------------------------------------------------
    // Haste-grant static effect
    // -------------------------------------------------------------------------

    [Fact]
    public void GoblinWarchief_GrantsHasteToOtherControllerGoblins()
    {
        var svc = new ContinuousEffectsService();

        var otherGoblin = new Creature("Mogg Fanatic", "R", 1, 1,
            subtypes: new[] { CardSubtype.Goblin })
        {
            Owner = _alice,
            Controller = _alice,
            Zone = ZoneType.Battlefield,
            ActiveEffects = svc,
        };

        var warchief = GoblinWarchiefFactory.Create(_alice, svc);
        warchief.Zone = ZoneType.Battlefield;
        warchief.ActiveEffects = svc;

        CombatAbilities.HasHaste(otherGoblin).Should().BeTrue(
            "Warchief's static grants Haste to controller's Goblins.");
    }

    [Fact]
    public void GoblinWarchief_GrantsHasteToItself()
    {
        // Oracle text: "Goblins you control have haste." No "other" clause —
        // so Warchief grants Haste to itself too. (includeSelf: true.)
        var svc = new ContinuousEffectsService();

        var warchief = GoblinWarchiefFactory.Create(_alice, svc);
        warchief.Zone = ZoneType.Battlefield;
        warchief.ActiveEffects = svc;

        CombatAbilities.HasHaste(warchief).Should().BeTrue(
            "Warchief is a Goblin its controller controls — gets its own Haste.");
        warchief.GetPower().Should().Be(2, "no P/T bonus from Warchief's haste-only static.");
        warchief.GetToughness().Should().Be(2);
    }

    [Fact]
    public void GoblinWarchief_DoesNotGrantHasteToNonGoblin()
    {
        var svc = new ContinuousEffectsService();

        var bear = new Creature("Grizzly Bears", "1G", 2, 2,
            subtypes: new[] { CardSubtype.Bear })
        {
            Owner = _alice,
            Controller = _alice,
            Zone = ZoneType.Battlefield,
            ActiveEffects = svc,
        };

        var warchief = GoblinWarchiefFactory.Create(_alice, svc);
        warchief.Zone = ZoneType.Battlefield;
        warchief.ActiveEffects = svc;

        CombatAbilities.HasHaste(bear).Should().BeFalse(
            "non-Goblins don't get the Haste grant.");
    }

    [Fact]
    public void GoblinWarchief_DoesNotGrantHasteToOpponentGoblin()
    {
        var svc = new ContinuousEffectsService();

        var oppGoblin = new Creature("Mogg Fanatic", "R", 1, 1,
            subtypes: new[] { CardSubtype.Goblin })
        {
            Owner = _bob,
            Controller = _bob,
            Zone = ZoneType.Battlefield,
            ActiveEffects = svc,
        };

        var warchief = GoblinWarchiefFactory.Create(_alice, svc);
        warchief.Zone = ZoneType.Battlefield;
        warchief.ActiveEffects = svc;

        CombatAbilities.HasHaste(oppGoblin).Should().BeFalse(
            "Warchief's static is scoped to 'Goblins YOU control'.");
    }
}
