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
/// Unit tests for <see cref="JetMedallionFactory"/>.
///
/// Jet Medallion (Tempest) — Artifact, {2}. Oracle text:
///   "Black spells you cast cost {1} less to cast."
///
/// Covers:
/// - Identity (name, mana cost, Artifact type, owner/controller).
/// - NamedCardFactory dispatch.
/// - Cost-reduction rider (CR 117.7):
///     * Black instant — reduced by {1} generic, coloured pip untouched.
///     * Black creature — reduced by {1} (any black spell, not just I/S).
///     * Red spell — NOT reduced (predicate rejects non-black).
///     * {B} drain floors at zero — coloured pip untouched (CR 117.7c).
///     * Off-battlefield Medallion — no reduction (rider inert).
///     * Two Medallions stack ({2} reduction).
///     * Opponent's Medallion doesn't discount your spells.
/// </summary>
[Trait("Color", "C")]
public class JetMedallionFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    private static void PutOnBattlefield(Player owner, Card card)
    {
        owner.Zones.Battlefield.AddCard(card);
        card.SetZone(ZoneType.Battlefield);
    }

    [Fact]
    public void JetMedallion_Identity()
    {
        var c = JetMedallionFactory.Create(_alice);

        c.Name.Should().Be("Jet Medallion");
        c.ManaCost.Should().Be("{2}");
        c.HasType(CardType.Artifact).Should().BeTrue();
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);

        c.Abilities.OfType<SpellCostReductionAbility>()
            .Should().HaveCount(1, "the black-spell cost-reduction rider is attached");
    }
    [Fact]
    public void BlackInstant_GenericReducedByOne()
    {
        var medallion = JetMedallionFactory.Create(_alice);
        PutOnBattlefield(_alice, medallion);

        // Dark Ritual-ish instant shape — {1}{B} instant.
        var spell = new Instant("Disfigure-ish", "{1}{B}");
        spell.SetOwner(_alice);
        spell.SetController(_alice);

        var effective = CostReduction.GetEffectiveCost(spell, _alice);

        effective.Generic.Should().Be(0, "{1} generic reduced by 1 → {0}");
        effective.Black.Should().Be(1, "coloured pip untouched");
    }

    [Fact]
    public void BlackCreature_GenericReducedByOne()
    {
        // Unlike Goblin Anarchomancer, Jet Medallion reduces ANY black spell,
        // including creatures.
        var medallion = JetMedallionFactory.Create(_alice);
        PutOnBattlefield(_alice, medallion);

        var creature = new Creature("Gravecrawler-ish", "{2}{B}", power: 2, toughness: 2);
        creature.SetOwner(_alice);
        creature.SetController(_alice);

        var effective = CostReduction.GetEffectiveCost(creature, _alice);

        effective.Generic.Should().Be(1, "{2} generic reduced by 1 → {1}");
        effective.Black.Should().Be(1, "coloured pip untouched");
    }

    [Fact]
    public void RedSpell_NoReduction()
    {
        var medallion = JetMedallionFactory.Create(_alice);
        PutOnBattlefield(_alice, medallion);

        // Lightning Bolt shape — {R}{R}{R}-ish. Red is not black → predicate rejects.
        var burn = new Instant("Burn-ish", "{1}{R}{R}");
        burn.SetOwner(_alice);
        burn.SetController(_alice);

        var effective = CostReduction.GetEffectiveCost(burn, _alice);

        effective.Generic.Should().Be(1, "red spell — Medallion's predicate rejects");
        effective.Red.Should().Be(2);
    }

    [Fact]
    public void BlackPip_FloorsAtZero_ColouredPipUntouched()
    {
        // Single black pip, no generic. Cost floors at 0 and the coloured
        // pip is untouched (CR 117.7c).
        var medallion = JetMedallionFactory.Create(_alice);
        PutOnBattlefield(_alice, medallion);

        var spell = new Instant("Fatal Push-ish", "{B}");
        spell.SetOwner(_alice);
        spell.SetController(_alice);

        var effective = CostReduction.GetEffectiveCost(spell, _alice);

        effective.Generic.Should().Be(0);
        effective.Black.Should().Be(1);
        effective.TotalValue.Should().Be(1);
    }

    [Fact]
    public void OffBattlefield_NoReduction()
    {
        var medallion = JetMedallionFactory.Create(_alice);
        _alice.Zones.Hand.AddCard(medallion);
        medallion.SetZone(ZoneType.Hand);

        var spell = new Instant("Disfigure-ish", "{1}{B}");
        spell.SetOwner(_alice);
        spell.SetController(_alice);

        var effective = CostReduction.GetEffectiveCost(spell, _alice);

        effective.Generic.Should().Be(1, "Medallion isn't on the battlefield");
        effective.Black.Should().Be(1);
    }

    [Fact]
    public void TwoMedallions_ReductionStacks()
    {
        var m1 = JetMedallionFactory.Create(_alice);
        var m2 = JetMedallionFactory.Create(_alice);
        PutOnBattlefield(_alice, m1);
        PutOnBattlefield(_alice, m2);

        // {2}{B} creature → two {1} reducers → generic floors toward 0.
        var creature = new Creature("Gravecrawler-ish", "{2}{B}", power: 2, toughness: 2);
        creature.SetOwner(_alice);
        creature.SetController(_alice);

        var effective = CostReduction.GetEffectiveCost(creature, _alice);

        effective.Generic.Should().Be(0, "two Medallions reduce {2} generic → {0}");
        effective.Black.Should().Be(1);
    }

    [Fact]
    public void OpponentControlsMedallion_DoesNotDiscountYourSpells()
    {
        // "Black spells YOU cast" — scoped to the reducer's controller.
        var bobMedallion = JetMedallionFactory.Create(_bob);
        PutOnBattlefield(_bob, bobMedallion);

        var aliceSpell = new Instant("Disfigure-ish", "{1}{B}");
        aliceSpell.SetOwner(_alice);
        aliceSpell.SetController(_alice);

        var effective = CostReduction.GetEffectiveCost(aliceSpell, _alice);

        effective.Generic.Should().Be(1, "Bob's Medallion doesn't reduce Alice's spells");
        effective.Black.Should().Be(1);
    }
}
