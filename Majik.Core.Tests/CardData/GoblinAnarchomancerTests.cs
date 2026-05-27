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
/// Unit tests for <see cref="GoblinAnarchomancerFactory"/>.
///
/// Covers:
/// - Identity (name, mana cost, Goblin + Shaman subtypes, 1/3,
///   owner/controller).
/// - NamedCardFactory dispatch.
/// - Cost-reduction rider (CR 117.7):
///     * Red instant — reduced by 1 generic.
///     * Green sorcery — reduced by 1 generic.
///     * Blue instant — NOT reduced (predicate rejects non-red/green).
///     * Red creature spell — NOT reduced (predicate rejects non-
///       instant/sorcery).
///     * Off-battlefield Anarchomancer — no reduction (rider inert).
///     * Two Anarchomancers stack ({2} reduction).
///     * {R} bolt floors at zero — coloured pip untouched.
///     * Opponent's Anarchomancer doesn't discount your spells.
/// </summary>
public class GoblinAnarchomancerTests
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
    public void GoblinAnarchomancer_Identity()
    {
        var c = GoblinAnarchomancerFactory.Create(_alice);

        c.Name.Should().Be("Goblin Anarchomancer");
        c.ManaCost.Should().Be("{R}{G}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Goblin).Should().BeTrue();
        c.HasSubtype(CardSubtype.Shaman).Should().BeTrue();
        c.Power.Should().Be(1);
        c.Toughness.Should().Be(3);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);

        c.Abilities.OfType<SpellCostReductionAbility>()
            .Should().HaveCount(1, "the red/green instant/sorcery rider is attached");
    }

    [Fact]
    public void GoblinAnarchomancer_DispatchesViaNamedCardFactory()
    {
        var c = NamedCardFactory.Create("Goblin Anarchomancer", _alice);

        c.Should().BeOfType<Creature>();
        c.Name.Should().Be("Goblin Anarchomancer");
        c.HasSubtype(CardSubtype.Goblin).Should().BeTrue();
        c.HasSubtype(CardSubtype.Shaman).Should().BeTrue();
        c.Abilities.OfType<SpellCostReductionAbility>().Should().HaveCount(1);
    }

    // -------------------------------------------------------------------------
    // Reduction predicate — colour + type matrix
    // -------------------------------------------------------------------------

    [Fact]
    public void RedInstant_GenericReducedByOne()
    {
        var mancer = GoblinAnarchomancerFactory.Create(_alice);
        PutOnBattlefield(_alice, mancer);

        // Burst Lightning shape — {1}{R} instant.
        var burn = new Instant("Burst Lightning", "{1}{R}");
        burn.SetOwner(_alice);
        burn.SetController(_alice);

        var effective = CostReduction.GetEffectiveCost(burn, _alice);

        effective.Generic.Should().Be(0, "{1} generic reduced by 1 → {0}");
        effective.Red.Should().Be(1, "coloured pip untouched");
    }

    [Fact]
    public void GreenSorcery_GenericReducedByOne()
    {
        var mancer = GoblinAnarchomancerFactory.Create(_alice);
        PutOnBattlefield(_alice, mancer);

        // Cultivate shape — {2}{G} sorcery.
        var sorc = new Sorcery("Cultivate", "{2}{G}");
        sorc.SetOwner(_alice);
        sorc.SetController(_alice);

        var effective = CostReduction.GetEffectiveCost(sorc, _alice);

        effective.Generic.Should().Be(1, "{2} generic reduced by 1 → {1}");
        effective.Green.Should().Be(1, "coloured pip untouched");
    }

    [Fact]
    public void BlueInstant_NoReduction()
    {
        var mancer = GoblinAnarchomancerFactory.Create(_alice);
        PutOnBattlefield(_alice, mancer);

        // Cancel — {1}{U}{U}. Blue is neither red nor green → predicate
        // rejects → no reduction.
        var cancel = new Instant("Cancel", "{1}{U}{U}");
        cancel.SetOwner(_alice);
        cancel.SetController(_alice);

        var effective = CostReduction.GetEffectiveCost(cancel, _alice);

        effective.Generic.Should().Be(1, "blue spell — Anarchomancer's predicate rejects");
        effective.Blue.Should().Be(2);
    }

    [Fact]
    public void RedCreature_NoReduction()
    {
        var mancer = GoblinAnarchomancerFactory.Create(_alice);
        PutOnBattlefield(_alice, mancer);

        // A red creature — predicate requires instant or sorcery.
        var creature = new Creature("Goblin Guide", "{R}", power: 2, toughness: 2);
        creature.SetOwner(_alice);
        creature.SetController(_alice);

        var effective = CostReduction.GetEffectiveCost(creature, _alice);

        effective.Generic.Should().Be(0, "no generic on a {R} card");
        effective.Red.Should().Be(1, "creature spell — Anarchomancer doesn't reduce");
    }

    [Fact]
    public void OffBattlefield_NoReduction()
    {
        // Anarchomancer in hand, not on battlefield → rider inert.
        var mancer = GoblinAnarchomancerFactory.Create(_alice);
        _alice.Zones.Hand.AddCard(mancer);
        mancer.SetZone(ZoneType.Hand);

        var burn = new Instant("Burst Lightning", "{1}{R}");
        burn.SetOwner(_alice);
        burn.SetController(_alice);

        var effective = CostReduction.GetEffectiveCost(burn, _alice);

        effective.Generic.Should().Be(1, "Anarchomancer isn't on the battlefield");
        effective.Red.Should().Be(1);
    }

    [Fact]
    public void TwoAnarchomancers_ReductionStacks()
    {
        // Two reducers each contribute {1}. CR 117.7d only counts the SAME
        // reducer once per spell — but two copies are two distinct
        // permanents so each contributes.
        var m1 = GoblinAnarchomancerFactory.Create(_alice);
        var m2 = GoblinAnarchomancerFactory.Create(_alice);
        PutOnBattlefield(_alice, m1);
        PutOnBattlefield(_alice, m2);

        var sorc = new Sorcery("Cultivate", "{2}{G}");
        sorc.SetOwner(_alice);
        sorc.SetController(_alice);

        var effective = CostReduction.GetEffectiveCost(sorc, _alice);

        effective.Generic.Should().Be(0, "two Anarchomancers reduce {2} generic → {0}");
        effective.Green.Should().Be(1);
    }

    [Fact]
    public void RedBolt_FloorsAtZero_ColouredPipUntouched()
    {
        // Lightning Bolt — {R}, no generic. Cost floors at 0 and the
        // coloured pip is untouched (CR 117.7c).
        var mancer = GoblinAnarchomancerFactory.Create(_alice);
        PutOnBattlefield(_alice, mancer);

        var bolt = new Instant("Lightning Bolt", "{R}");
        bolt.SetOwner(_alice);
        bolt.SetController(_alice);

        var effective = CostReduction.GetEffectiveCost(bolt, _alice);

        effective.Generic.Should().Be(0);
        effective.Red.Should().Be(1);
        effective.TotalValue.Should().Be(1);
    }

    [Fact]
    public void OpponentControlsAnarchomancer_DoesNotDiscountYourSpells()
    {
        // Bob controls Anarchomancer; Alice casts a red instant. "Spells
        // you cast" is scoped to the reducer's controller, so Alice gets
        // no discount.
        var bobMancer = GoblinAnarchomancerFactory.Create(_bob);
        PutOnBattlefield(_bob, bobMancer);

        var aliceBolt = new Instant("Burst Lightning", "{1}{R}");
        aliceBolt.SetOwner(_alice);
        aliceBolt.SetController(_alice);

        var effective = CostReduction.GetEffectiveCost(aliceBolt, _alice);

        effective.Generic.Should().Be(1, "Bob's Anarchomancer doesn't reduce Alice's spells");
        effective.Red.Should().Be(1);
    }
}
