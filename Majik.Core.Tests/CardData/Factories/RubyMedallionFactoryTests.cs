using FluentAssertions;
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
/// Unit tests for <see cref="RubyMedallionFactory"/>.
///
/// Ruby Medallion (Tempest) — Artifact, {2}. Oracle text:
///   "Red spells you cast cost {1} less to cast."
///
/// Covers:
/// - Identity (name, mana cost, Artifact type, owner/controller).
/// - NamedCardFactory dispatch.
/// - Cost-reduction rider (CR 117.7):
///     * Red instant — reduced by {1} generic, coloured pip untouched.
///     * Red creature — reduced by {1} (any red spell, not just I/S — the
///       distinguishing difference from Goblin Anarchomancer).
///     * Blue spell — NOT reduced (predicate rejects non-red).
///     * {R} bolt floors at zero — coloured pip untouched (CR 117.7c).
///     * Off-battlefield Medallion — no reduction (rider inert).
///     * Two Medallions stack ({2} reduction).
///     * Opponent's Medallion doesn't discount your spells.
/// </summary>
public class RubyMedallionFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    private static void PutOnBattlefield(Player owner, Card card)
    {
        owner.Zones.Battlefield.AddCard(card);
        card.SetZone(ZoneType.Battlefield);
    }

    [Fact]
    public void RubyMedallion_Identity()
    {
        var c = RubyMedallionFactory.Create(_alice);

        c.Name.Should().Be("Ruby Medallion");
        c.ManaCost.Should().Be("{2}");
        c.HasType(CardType.Artifact).Should().BeTrue();
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);

        c.Abilities.OfType<SpellCostReductionAbility>()
            .Should().HaveCount(1, "the red-spell cost-reduction rider is attached");
    }

    [Fact]
    public void RubyMedallion_DispatchesViaNamedCardFactory()
    {
        var c = NamedCardFactory.Create("Ruby Medallion", _alice);

        c.Should().BeOfType<Artifact>();
        c.Name.Should().Be("Ruby Medallion");
        c.Abilities.OfType<SpellCostReductionAbility>().Should().HaveCount(1);
    }

    [Fact]
    public void RedInstant_GenericReducedByOne()
    {
        var medallion = RubyMedallionFactory.Create(_alice);
        PutOnBattlefield(_alice, medallion);

        // Burst Lightning shape — {1}{R} instant.
        var burn = new Instant("Burst Lightning", "{1}{R}");
        burn.SetOwner(_alice);
        burn.SetController(_alice);

        var effective = CostReduction.GetEffectiveCost(burn, _alice);

        effective.Generic.Should().Be(0, "{1} generic reduced by 1 → {0}");
        effective.Red.Should().Be(1, "coloured pip untouched");
    }

    [Fact]
    public void RedCreature_GenericReducedByOne()
    {
        // Unlike Goblin Anarchomancer, Ruby Medallion reduces ANY red spell,
        // including creatures.
        var medallion = RubyMedallionFactory.Create(_alice);
        PutOnBattlefield(_alice, medallion);

        var creature = new Creature("Goblin Rabblemaster", "{2}{R}", power: 2, toughness: 2);
        creature.SetOwner(_alice);
        creature.SetController(_alice);

        var effective = CostReduction.GetEffectiveCost(creature, _alice);

        effective.Generic.Should().Be(1, "{2} generic reduced by 1 → {1}");
        effective.Red.Should().Be(1, "coloured pip untouched");
    }

    [Fact]
    public void BlueSpell_NoReduction()
    {
        var medallion = RubyMedallionFactory.Create(_alice);
        PutOnBattlefield(_alice, medallion);

        // Cancel — {1}{U}{U}. Blue is not red → predicate rejects.
        var cancel = new Instant("Cancel", "{1}{U}{U}");
        cancel.SetOwner(_alice);
        cancel.SetController(_alice);

        var effective = CostReduction.GetEffectiveCost(cancel, _alice);

        effective.Generic.Should().Be(1, "blue spell — Medallion's predicate rejects");
        effective.Blue.Should().Be(2);
    }

    [Fact]
    public void RedBolt_FloorsAtZero_ColouredPipUntouched()
    {
        // Lightning Bolt — {R}, no generic. Cost floors at 0 and the
        // coloured pip is untouched (CR 117.7c).
        var medallion = RubyMedallionFactory.Create(_alice);
        PutOnBattlefield(_alice, medallion);

        var bolt = new Instant("Lightning Bolt", "{R}");
        bolt.SetOwner(_alice);
        bolt.SetController(_alice);

        var effective = CostReduction.GetEffectiveCost(bolt, _alice);

        effective.Generic.Should().Be(0);
        effective.Red.Should().Be(1);
        effective.TotalValue.Should().Be(1);
    }

    [Fact]
    public void OffBattlefield_NoReduction()
    {
        var medallion = RubyMedallionFactory.Create(_alice);
        _alice.Zones.Hand.AddCard(medallion);
        medallion.SetZone(ZoneType.Hand);

        var burn = new Instant("Burst Lightning", "{1}{R}");
        burn.SetOwner(_alice);
        burn.SetController(_alice);

        var effective = CostReduction.GetEffectiveCost(burn, _alice);

        effective.Generic.Should().Be(1, "Medallion isn't on the battlefield");
        effective.Red.Should().Be(1);
    }

    [Fact]
    public void TwoMedallions_ReductionStacks()
    {
        var m1 = RubyMedallionFactory.Create(_alice);
        var m2 = RubyMedallionFactory.Create(_alice);
        PutOnBattlefield(_alice, m1);
        PutOnBattlefield(_alice, m2);

        // {2}{R} creature → two {1} reducers → generic floors toward 0.
        var creature = new Creature("Goblin Rabblemaster", "{2}{R}", power: 2, toughness: 2);
        creature.SetOwner(_alice);
        creature.SetController(_alice);

        var effective = CostReduction.GetEffectiveCost(creature, _alice);

        effective.Generic.Should().Be(0, "two Medallions reduce {2} generic → {0}");
        effective.Red.Should().Be(1);
    }

    [Fact]
    public void OpponentControlsMedallion_DoesNotDiscountYourSpells()
    {
        // "Red spells YOU cast" — scoped to the reducer's controller.
        var bobMedallion = RubyMedallionFactory.Create(_bob);
        PutOnBattlefield(_bob, bobMedallion);

        var aliceBolt = new Instant("Burst Lightning", "{1}{R}");
        aliceBolt.SetOwner(_alice);
        aliceBolt.SetController(_alice);

        var effective = CostReduction.GetEffectiveCost(aliceBolt, _alice);

        effective.Generic.Should().Be(1, "Bob's Medallion doesn't reduce Alice's spells");
        effective.Red.Should().Be(1);
    }
}
