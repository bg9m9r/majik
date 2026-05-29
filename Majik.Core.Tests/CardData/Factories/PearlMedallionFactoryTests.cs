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
/// Unit tests for <see cref="PearlMedallionFactory"/>.
///
/// Pearl Medallion (Tempest) — Artifact, {2}. Oracle text:
///   "White spells you cast cost {1} less to cast."
///
/// Covers:
/// - Identity (name, mana cost, Artifact type, owner/controller).
/// - NamedCardFactory dispatch.
/// - Cost-reduction rider (CR 117.7):
///     * White instant — reduced by {1} generic, coloured pip untouched.
///     * White creature — reduced by {1} (any white spell, not just I/S).
///     * Red spell — NOT reduced (predicate rejects non-white).
///     * {W} spell floors at zero — coloured pip untouched (CR 117.7c).
///     * Off-battlefield Medallion — no reduction (rider inert).
///     * Two Medallions stack ({2} reduction).
///     * Opponent's Medallion doesn't discount your spells.
/// </summary>
public class PearlMedallionFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    private static void PutOnBattlefield(Player owner, Card card)
    {
        owner.Zones.Battlefield.AddCard(card);
        card.SetZone(ZoneType.Battlefield);
    }

    [Fact]
    public void PearlMedallion_Identity()
    {
        var c = PearlMedallionFactory.Create(_alice);

        c.Name.Should().Be("Pearl Medallion");
        c.ManaCost.Should().Be("{2}");
        c.HasType(CardType.Artifact).Should().BeTrue();
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);

        c.Abilities.OfType<SpellCostReductionAbility>()
            .Should().HaveCount(1, "the white-spell cost-reduction rider is attached");
    }

    [Fact]
    public void PearlMedallion_DispatchesViaNamedCardFactory()
    {
        var c = NamedCardFactory.Create("Pearl Medallion", _alice);

        c.Should().BeOfType<Artifact>();
        c.Name.Should().Be("Pearl Medallion");
        c.Abilities.OfType<SpellCostReductionAbility>().Should().HaveCount(1);
    }

    [Fact]
    public void WhiteInstant_GenericReducedByOne()
    {
        var medallion = PearlMedallionFactory.Create(_alice);
        PutOnBattlefield(_alice, medallion);

        // {1}{W} instant shape.
        var spell = new Instant("Mana Tithe", "{1}{W}");
        spell.SetOwner(_alice);
        spell.SetController(_alice);

        var effective = CostReduction.GetEffectiveCost(spell, _alice);

        effective.Generic.Should().Be(0, "{1} generic reduced by 1 → {0}");
        effective.White.Should().Be(1, "coloured pip untouched");
    }

    [Fact]
    public void WhiteCreature_GenericReducedByOne()
    {
        // Unlike instant/sorcery-only reducers, Pearl Medallion reduces ANY
        // white spell, including creatures.
        var medallion = PearlMedallionFactory.Create(_alice);
        PutOnBattlefield(_alice, medallion);

        var creature = new Creature("Wall of Omens", "{1}{W}", power: 0, toughness: 4);
        creature.SetOwner(_alice);
        creature.SetController(_alice);

        var effective = CostReduction.GetEffectiveCost(creature, _alice);

        effective.Generic.Should().Be(0, "{1} generic reduced by 1 → {0}");
        effective.White.Should().Be(1, "coloured pip untouched");
    }

    [Fact]
    public void RedSpell_NoReduction()
    {
        var medallion = PearlMedallionFactory.Create(_alice);
        PutOnBattlefield(_alice, medallion);

        // Lightning Bolt — {R}. Red is not white → predicate rejects.
        var bolt = new Instant("Burst Lightning", "{1}{R}");
        bolt.SetOwner(_alice);
        bolt.SetController(_alice);

        var effective = CostReduction.GetEffectiveCost(bolt, _alice);

        effective.Generic.Should().Be(1, "red spell — Medallion's predicate rejects");
        effective.Red.Should().Be(1);
    }

    [Fact]
    public void WhiteSpell_FloorsAtZero_ColouredPipUntouched()
    {
        // {W}, no generic. Cost floors at 0 and the coloured pip is untouched
        // (CR 117.7c).
        var medallion = PearlMedallionFactory.Create(_alice);
        PutOnBattlefield(_alice, medallion);

        var spell = new Instant("Swords to Plowshares", "{W}");
        spell.SetOwner(_alice);
        spell.SetController(_alice);

        var effective = CostReduction.GetEffectiveCost(spell, _alice);

        effective.Generic.Should().Be(0);
        effective.White.Should().Be(1);
        effective.TotalValue.Should().Be(1);
    }

    [Fact]
    public void OffBattlefield_NoReduction()
    {
        var medallion = PearlMedallionFactory.Create(_alice);
        _alice.Zones.Hand.AddCard(medallion);
        medallion.SetZone(ZoneType.Hand);

        var spell = new Instant("Mana Tithe", "{1}{W}");
        spell.SetOwner(_alice);
        spell.SetController(_alice);

        var effective = CostReduction.GetEffectiveCost(spell, _alice);

        effective.Generic.Should().Be(1, "Medallion isn't on the battlefield");
        effective.White.Should().Be(1);
    }

    [Fact]
    public void TwoMedallions_ReductionStacks()
    {
        var m1 = PearlMedallionFactory.Create(_alice);
        var m2 = PearlMedallionFactory.Create(_alice);
        PutOnBattlefield(_alice, m1);
        PutOnBattlefield(_alice, m2);

        // {2}{W} creature → two {1} reducers → generic floors toward 0.
        var creature = new Creature("Serra Angel", "{2}{W}", power: 4, toughness: 4);
        creature.SetOwner(_alice);
        creature.SetController(_alice);

        var effective = CostReduction.GetEffectiveCost(creature, _alice);

        effective.Generic.Should().Be(0, "two Medallions reduce {2} generic → {0}");
        effective.White.Should().Be(1);
    }

    [Fact]
    public void OpponentControlsMedallion_DoesNotDiscountYourSpells()
    {
        // "White spells YOU cast" — scoped to the reducer's controller.
        var bobMedallion = PearlMedallionFactory.Create(_bob);
        PutOnBattlefield(_bob, bobMedallion);

        var aliceSpell = new Instant("Mana Tithe", "{1}{W}");
        aliceSpell.SetOwner(_alice);
        aliceSpell.SetController(_alice);

        var effective = CostReduction.GetEffectiveCost(aliceSpell, _alice);

        effective.Generic.Should().Be(1, "Bob's Medallion doesn't reduce Alice's spells");
        effective.White.Should().Be(1);
    }
}
