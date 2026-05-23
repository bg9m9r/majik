using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Unit tests for <see cref="GoblinElectromancerFactory"/>.
///
/// Covers:
/// - Identity (name, mana cost, Goblin + Wizard subtypes, 2/2, owner/controller).
/// - NamedCardFactory dispatch returns a Creature shell with the
///   SpellCostReductionAbility rider attached.
/// - Spell-cost reduction rider (CR 117.7):
///     * Instant cast — generic cost reduced by 1.
///     * Sorcery cast — generic cost reduced by 1.
///     * Creature cast — no reduction (rider predicate excludes non-
///       instant/sorcery spells).
///     * Off-battlefield Electromancer — no reduction (rider inert when
///       the source isn't on the controller's battlefield).
///     * Two Electromancers stack — reduction is additive.
///     * Coloured pips untouched + floor-at-zero ({R} stays {R}).
/// </summary>
public class GoblinElectromancerTests
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
    public void GoblinElectromancer_Identity()
    {
        var c = GoblinElectromancerFactory.Create(_alice);

        c.Name.Should().Be("Goblin Electromancer");
        c.ManaCost.Should().Be("{U}{R}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Goblin).Should().BeTrue("Goblin is a printed subtype");
        c.HasSubtype(CardSubtype.Wizard).Should().BeTrue("Wizard is a printed subtype");
        c.Power.Should().Be(2);
        c.Toughness.Should().Be(2);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);

        c.Abilities.OfType<SpellCostReductionAbility>()
            .Should().HaveCount(1, "the instant/sorcery cost-reduction rider is attached");
    }

    [Fact]
    public void GoblinElectromancer_DispatchesViaNamedCardFactory()
    {
        var c = NamedCardFactory.Create("Goblin Electromancer", _alice);

        c.Should().BeOfType<Creature>();
        c.Name.Should().Be("Goblin Electromancer");
        c.HasSubtype(CardSubtype.Goblin).Should().BeTrue();
        c.HasSubtype(CardSubtype.Wizard).Should().BeTrue();
        c.Abilities.OfType<SpellCostReductionAbility>().Should().HaveCount(1);
    }

    // -------------------------------------------------------------------------
    // Cost-reduction rider (CR 117.7)
    // -------------------------------------------------------------------------

    [Fact]
    public void InstantCast_GenericReducedByOne()
    {
        // Lightning Helix-shaped — {R}{W} with 0 generic — but use a
        // generic-bearing instant so the reduction is observable in the
        // generic bucket. Counterspell — {U}{U} — has 0 generic, so use
        // Cancel-shape: {1}{U}{U}.
        var electromancer = GoblinElectromancerFactory.Create(_alice);
        PutOnBattlefield(_alice, electromancer);

        var cancel = new Instant("Cancel", "{1}{U}{U}");
        cancel.SetOwner(_alice);
        cancel.SetController(_alice);

        var effective = CostReduction.GetEffectiveCost(cancel, _alice);

        effective.Generic.Should().Be(0, "{1} generic reduced by 1 → {0}");
        effective.Blue.Should().Be(2, "coloured pips untouched (CR 117.7c)");
        effective.TotalValue.Should().Be(2);
    }

    [Fact]
    public void SorceryCast_GenericReducedByOne()
    {
        var electromancer = GoblinElectromancerFactory.Create(_alice);
        PutOnBattlefield(_alice, electromancer);

        // Mind Rot-shape sorcery — {2}{B}.
        var sorcery = new Sorcery("Mind Rot", "{2}{B}");
        sorcery.SetOwner(_alice);
        sorcery.SetController(_alice);

        var effective = CostReduction.GetEffectiveCost(sorcery, _alice);

        effective.Generic.Should().Be(1, "{2} generic reduced by 1 → {1}");
        effective.Black.Should().Be(1, "coloured pips untouched (CR 117.7c)");
        effective.TotalValue.Should().Be(2);
    }

    [Fact]
    public void CreatureCast_NoReduction()
    {
        var electromancer = GoblinElectromancerFactory.Create(_alice);
        PutOnBattlefield(_alice, electromancer);

        // A vanilla creature — {2}{G}. The rider's predicate matches only
        // instants and sorceries; creature spells must pass through with
        // full printed cost.
        var creature = new Creature("Test Beast", "{2}{G}", power: 3, toughness: 3);
        creature.SetOwner(_alice);
        creature.SetController(_alice);

        var effective = CostReduction.GetEffectiveCost(creature, _alice);

        effective.Generic.Should().Be(2, "creature spell — no Electromancer discount");
        effective.Green.Should().Be(1);
        effective.TotalValue.Should().Be(3);
    }

    [Fact]
    public void OffBattlefield_NoReduction()
    {
        // Electromancer in hand, not on battlefield → rider inert. Mirrors
        // the Damping Sphere off-battlefield test.
        var electromancer = GoblinElectromancerFactory.Create(_alice);
        _alice.Zones.Hand.AddCard(electromancer);
        electromancer.SetZone(ZoneType.Hand);

        var sorcery = new Sorcery("Mind Rot", "{2}{B}");
        sorcery.SetOwner(_alice);
        sorcery.SetController(_alice);

        var effective = CostReduction.GetEffectiveCost(sorcery, _alice);

        effective.Generic.Should().Be(2, "Electromancer isn't on the battlefield — no discount");
        effective.Black.Should().Be(1);
    }

    [Fact]
    public void TwoElectromancers_ReductionStacks()
    {
        // Two Electromancers each contribute {1} → {2} total reduction.
        var e1 = GoblinElectromancerFactory.Create(_alice);
        var e2 = GoblinElectromancerFactory.Create(_alice);
        PutOnBattlefield(_alice, e1);
        PutOnBattlefield(_alice, e2);

        var sorcery = new Sorcery("Mind Rot", "{2}{B}");
        sorcery.SetOwner(_alice);
        sorcery.SetController(_alice);

        var effective = CostReduction.GetEffectiveCost(sorcery, _alice);

        effective.Generic.Should().Be(0, "two Electromancers reduce {2} generic → {0}");
        effective.Black.Should().Be(1, "coloured pip still required");
        effective.TotalValue.Should().Be(1);
    }

    [Fact]
    public void RedInstant_FloorsAtZero_ColouredPipUntouched()
    {
        // Lightning Bolt — {R}, 0 generic. Reducer can't drive coloured pip
        // below its printed minimum (CR 117.7c) and the generic bucket
        // floors at 0.
        var electromancer = GoblinElectromancerFactory.Create(_alice);
        PutOnBattlefield(_alice, electromancer);

        var bolt = new Instant("Lightning Bolt", "{R}");
        bolt.SetOwner(_alice);
        bolt.SetController(_alice);

        var effective = CostReduction.GetEffectiveCost(bolt, _alice);

        effective.Generic.Should().Be(0, "no generic to reduce — floor-at-zero (CR 117.7c)");
        effective.Red.Should().Be(1, "coloured pip untouched");
        effective.TotalValue.Should().Be(1);
    }

    [Fact]
    public void OpponentControlsElectromancer_DoesNotDiscountYourSpells()
    {
        // Bob controls a Goblin Electromancer; Alice casts an instant. The
        // rider is scoped to the controller's battlefield ("spells YOU
        // cast"), so Alice gets no discount.
        var bobMancer = GoblinElectromancerFactory.Create(_bob);
        PutOnBattlefield(_bob, bobMancer);

        var aliceCancel = new Instant("Cancel", "{1}{U}{U}");
        aliceCancel.SetOwner(_alice);
        aliceCancel.SetController(_alice);

        var effective = CostReduction.GetEffectiveCost(aliceCancel, _alice);

        effective.Generic.Should().Be(1,
            "Bob's Electromancer doesn't reduce Alice's spells — 'spells you cast' is " +
            "scoped to the controller of the reducer permanent");
        effective.Blue.Should().Be(2);
    }
}
