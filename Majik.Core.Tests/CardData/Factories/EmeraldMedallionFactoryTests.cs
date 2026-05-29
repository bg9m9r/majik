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
/// Unit tests for <see cref="EmeraldMedallionFactory"/>.
///
/// Emerald Medallion (Tempest) — Artifact, {2}. Oracle text:
///   "Green spells you cast cost {1} less to cast."
///
/// Direct green-coloured sibling of Ruby Medallion; covers the same
/// matrix swapped to green (CR 117.7):
/// - Identity (name, mana cost, Artifact type, owner/controller).
/// - NamedCardFactory dispatch.
/// - Green instant — reduced by {1} generic, coloured pip untouched.
/// - Green creature — reduced by {1} (any green spell, not just I/S).
/// - Red spell — NOT reduced (predicate rejects non-green).
/// - {G} spell floors at zero — coloured pip untouched (CR 117.7c).
/// - Off-battlefield Medallion — no reduction (rider inert).
/// - Two Medallions stack ({2} reduction).
/// - Opponent's Medallion doesn't discount your spells.
/// </summary>
public class EmeraldMedallionFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    private static void PutOnBattlefield(Player owner, Card card)
    {
        owner.Zones.Battlefield.AddCard(card);
        card.SetZone(ZoneType.Battlefield);
    }

    [Fact]
    public void EmeraldMedallion_Identity()
    {
        var c = EmeraldMedallionFactory.Create(_alice);

        c.Name.Should().Be("Emerald Medallion");
        c.ManaCost.Should().Be("{2}");
        c.HasType(CardType.Artifact).Should().BeTrue();
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);

        c.Abilities.OfType<SpellCostReductionAbility>()
            .Should().HaveCount(1, "the green-spell cost-reduction rider is attached");
    }

    [Fact]
    public void EmeraldMedallion_DispatchesViaNamedCardFactory()
    {
        var c = NamedCardFactory.Create("Emerald Medallion", _alice);

        c.Should().BeOfType<Artifact>();
        c.Name.Should().Be("Emerald Medallion");
        c.Abilities.OfType<SpellCostReductionAbility>().Should().HaveCount(1);
    }

    [Fact]
    public void GreenInstant_GenericReducedByOne()
    {
        var medallion = EmeraldMedallionFactory.Create(_alice);
        PutOnBattlefield(_alice, medallion);

        // {1}{G} instant shape.
        var spell = new Instant("Snakeskin Veil", "{1}{G}");
        spell.SetOwner(_alice);
        spell.SetController(_alice);

        var effective = CostReduction.GetEffectiveCost(spell, _alice);

        effective.Generic.Should().Be(0, "{1} generic reduced by 1 → {0}");
        effective.Green.Should().Be(1, "coloured pip untouched");
    }

    [Fact]
    public void GreenCreature_GenericReducedByOne()
    {
        // Unlike type-gated reducers, Emerald Medallion reduces ANY green
        // spell, including creatures.
        var medallion = EmeraldMedallionFactory.Create(_alice);
        PutOnBattlefield(_alice, medallion);

        var creature = new Creature("Llanowar Visionary", "{2}{G}", power: 2, toughness: 2);
        creature.SetOwner(_alice);
        creature.SetController(_alice);

        var effective = CostReduction.GetEffectiveCost(creature, _alice);

        effective.Generic.Should().Be(1, "{2} generic reduced by 1 → {1}");
        effective.Green.Should().Be(1, "coloured pip untouched");
    }

    [Fact]
    public void RedSpell_NoReduction()
    {
        var medallion = EmeraldMedallionFactory.Create(_alice);
        PutOnBattlefield(_alice, medallion);

        // Lightning Helix shape — {R}{W}. Not green → predicate rejects.
        var spell = new Instant("Incinerate", "{1}{R}");
        spell.SetOwner(_alice);
        spell.SetController(_alice);

        var effective = CostReduction.GetEffectiveCost(spell, _alice);

        effective.Generic.Should().Be(1, "red spell — Medallion's predicate rejects");
        effective.Red.Should().Be(1);
    }

    [Fact]
    public void GreenSpell_FloorsAtZero_ColouredPipUntouched()
    {
        // {G}, no generic. Cost floors at 0 and the coloured pip is
        // untouched (CR 117.7c).
        var medallion = EmeraldMedallionFactory.Create(_alice);
        PutOnBattlefield(_alice, medallion);

        var spell = new Instant("Giant Growth", "{G}");
        spell.SetOwner(_alice);
        spell.SetController(_alice);

        var effective = CostReduction.GetEffectiveCost(spell, _alice);

        effective.Generic.Should().Be(0);
        effective.Green.Should().Be(1);
        effective.TotalValue.Should().Be(1);
    }

    [Fact]
    public void OffBattlefield_NoReduction()
    {
        var medallion = EmeraldMedallionFactory.Create(_alice);
        _alice.Zones.Hand.AddCard(medallion);
        medallion.SetZone(ZoneType.Hand);

        var spell = new Instant("Snakeskin Veil", "{1}{G}");
        spell.SetOwner(_alice);
        spell.SetController(_alice);

        var effective = CostReduction.GetEffectiveCost(spell, _alice);

        effective.Generic.Should().Be(1, "Medallion isn't on the battlefield");
        effective.Green.Should().Be(1);
    }

    [Fact]
    public void TwoMedallions_ReductionStacks()
    {
        var m1 = EmeraldMedallionFactory.Create(_alice);
        var m2 = EmeraldMedallionFactory.Create(_alice);
        PutOnBattlefield(_alice, m1);
        PutOnBattlefield(_alice, m2);

        // {2}{G} creature → two {1} reducers → generic floors toward 0.
        var creature = new Creature("Llanowar Visionary", "{2}{G}", power: 2, toughness: 2);
        creature.SetOwner(_alice);
        creature.SetController(_alice);

        var effective = CostReduction.GetEffectiveCost(creature, _alice);

        effective.Generic.Should().Be(0, "two Medallions reduce {2} generic → {0}");
        effective.Green.Should().Be(1);
    }

    [Fact]
    public void OpponentControlsMedallion_DoesNotDiscountYourSpells()
    {
        // "Green spells YOU cast" — scoped to the reducer's controller.
        var bobMedallion = EmeraldMedallionFactory.Create(_bob);
        PutOnBattlefield(_bob, bobMedallion);

        var aliceSpell = new Instant("Snakeskin Veil", "{1}{G}");
        aliceSpell.SetOwner(_alice);
        aliceSpell.SetController(_alice);

        var effective = CostReduction.GetEffectiveCost(aliceSpell, _alice);

        effective.Generic.Should().Be(1, "Bob's Medallion doesn't reduce Alice's spells");
        effective.Green.Should().Be(1);
    }
}
