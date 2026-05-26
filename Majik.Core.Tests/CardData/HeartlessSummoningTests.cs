using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Unit tests for <see cref="HeartlessSummoningFactory"/>.
///
/// Covers:
/// - Identity (name, type, mana cost, owner/controller wiring).
/// - <see cref="NamedCardFactory"/> dispatch.
/// - Creature-spell cost reduction (CR 117.7): -2 generic on creature
///   spells; instants/sorceries untouched; off-battlefield inert; two
///   copies stack additively; coloured pips untouched; opponent's
///   Heartless Summoning doesn't discount your spells.
/// - Anthem (-1/-1) to controller's creatures via
///   <see cref="ControllerCreatureAnthemEffect"/>.
/// - Opponent's creatures untouched.
/// - LTB lifts the penalty.
/// </summary>
public class HeartlessSummoningTests
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
    public void HeartlessSummoning_Identity()
    {
        var card = HeartlessSummoningFactory.Create(_alice);

        card.Name.Should().Be("Heartless Summoning");
        card.ManaCost.Should().Be("{1}{B}");
        card.HasType(CardType.Enchantment).Should().BeTrue();
        card.HasType(CardType.Creature).Should().BeFalse();
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);

        card.Abilities.OfType<SpellCostReductionAbility>()
            .Should().HaveCount(1, "the creature-spell cost-reduction rider is attached.");
    }

    [Fact]
    public void HeartlessSummoning_DispatchesViaNamedCardFactory()
    {
        var card = NamedCardFactory.Create("Heartless Summoning", _alice);

        card.Should().BeOfType<Enchantment>();
        card.Name.Should().Be("Heartless Summoning");
        card.Abilities.OfType<SpellCostReductionAbility>().Should().HaveCount(1);
    }

    // -------------------------------------------------------------------------
    // Cost-reduction rider (CR 117.7)
    // -------------------------------------------------------------------------

    [Fact]
    public void CreatureCast_GenericReducedBy2()
    {
        var hs = HeartlessSummoningFactory.Create(_alice);
        PutOnBattlefield(_alice, hs);

        // {3}{G} creature — generic 3 → 1.
        var creature = new Creature("Test Beast", "{3}{G}", power: 4, toughness: 4);
        creature.SetOwner(_alice);
        creature.SetController(_alice);

        var effective = CostReduction.GetEffectiveCost(creature, _alice);

        effective.Generic.Should().Be(1, "{3} generic reduced by 2 → {1}");
        effective.Green.Should().Be(1, "coloured pip untouched (CR 117.7c)");
        effective.TotalValue.Should().Be(2);
    }

    [Fact]
    public void InstantCast_NoReduction()
    {
        var hs = HeartlessSummoningFactory.Create(_alice);
        PutOnBattlefield(_alice, hs);

        var cancel = new Instant("Cancel", "{1}{U}{U}");
        cancel.SetOwner(_alice);
        cancel.SetController(_alice);

        var effective = CostReduction.GetEffectiveCost(cancel, _alice);

        effective.Generic.Should().Be(1,
            "instant — Heartless Summoning's rider matches creature spells only.");
        effective.Blue.Should().Be(2);
    }

    [Fact]
    public void SorceryCast_NoReduction()
    {
        var hs = HeartlessSummoningFactory.Create(_alice);
        PutOnBattlefield(_alice, hs);

        var sorcery = new Sorcery("Mind Rot", "{2}{B}");
        sorcery.SetOwner(_alice);
        sorcery.SetController(_alice);

        var effective = CostReduction.GetEffectiveCost(sorcery, _alice);

        effective.Generic.Should().Be(2,
            "sorcery — Heartless Summoning's rider doesn't apply.");
        effective.Black.Should().Be(1);
    }

    [Fact]
    public void OffBattlefield_NoReduction()
    {
        var hs = HeartlessSummoningFactory.Create(_alice);
        _alice.Zones.Hand.AddCard(hs);
        hs.SetZone(ZoneType.Hand);

        var creature = new Creature("Test Beast", "{3}{G}", power: 4, toughness: 4);
        creature.SetOwner(_alice);
        creature.SetController(_alice);

        var effective = CostReduction.GetEffectiveCost(creature, _alice);

        effective.Generic.Should().Be(3, "Heartless Summoning not on battlefield — no discount.");
        effective.Green.Should().Be(1);
    }

    [Fact]
    public void TwoHeartlessSummonings_ReductionStacks()
    {
        var hs1 = HeartlessSummoningFactory.Create(_alice);
        var hs2 = HeartlessSummoningFactory.Create(_alice);
        PutOnBattlefield(_alice, hs1);
        PutOnBattlefield(_alice, hs2);

        var creature = new Creature("Test Beast", "{5}{G}", power: 6, toughness: 6);
        creature.SetOwner(_alice);
        creature.SetController(_alice);

        var effective = CostReduction.GetEffectiveCost(creature, _alice);

        effective.Generic.Should().Be(1, "{5} generic reduced by 2+2=4 → {1}");
        effective.Green.Should().Be(1);
    }

    [Fact]
    public void OpponentControlsHeartlessSummoning_DoesNotDiscountYourSpells()
    {
        var bobHs = HeartlessSummoningFactory.Create(_bob);
        PutOnBattlefield(_bob, bobHs);

        var aliceCreature = new Creature("Test Beast", "{3}{G}", power: 4, toughness: 4);
        aliceCreature.SetOwner(_alice);
        aliceCreature.SetController(_alice);

        var effective = CostReduction.GetEffectiveCost(aliceCreature, _alice);

        effective.Generic.Should().Be(3,
            "Bob's Heartless Summoning doesn't reduce Alice's creature spells — " +
            "rider is scoped to controller's battlefield.");
    }

    [Fact]
    public void CreatureCast_FloorsAtZero()
    {
        // Memnite — {0}. Floor-at-zero should still report 0 generic.
        var hs = HeartlessSummoningFactory.Create(_alice);
        PutOnBattlefield(_alice, hs);

        var memnite = new Creature("Memnite", "", power: 1, toughness: 1);
        memnite.SetOwner(_alice);
        memnite.SetController(_alice);

        var effective = CostReduction.GetEffectiveCost(memnite, _alice);

        effective.Generic.Should().Be(0, "{0} cost — floor-at-zero (CR 117.7c).");
        effective.TotalValue.Should().Be(0);
    }

    // -------------------------------------------------------------------------
    // Anthem (-1/-1)
    // -------------------------------------------------------------------------

    [Fact]
    public void HeartlessSummoning_DebuffsControllersCreatures_Minus1Minus1()
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

        var hs = HeartlessSummoningFactory.Create(_alice, svc);
        hs.Zone = ZoneType.Battlefield;

        bear.GetPower().Should().Be(1,
            "Heartless Summoning: creatures you control get -1/-1 (2→1).");
        bear.GetToughness().Should().Be(1);
    }

    [Fact]
    public void HeartlessSummoning_DoesNotDebuff_OpponentCreatures()
    {
        var svc = new ContinuousEffectsService();

        var oppBear = new Creature("Grizzly Bears", "1G", 2, 2,
            subtypes: new[] { CardSubtype.Bear })
        {
            Owner = _bob,
            Controller = _bob,
            Zone = ZoneType.Battlefield,
            ActiveEffects = svc,
        };

        var hs = HeartlessSummoningFactory.Create(_alice, svc);
        hs.Zone = ZoneType.Battlefield;

        oppBear.GetPower().Should().Be(2,
            "Heartless Summoning is scoped to controller's creatures (CR 109.5).");
        oppBear.GetToughness().Should().Be(2);
    }

    [Fact]
    public void HeartlessSummoning_LTB_LiftsPenalty()
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

        var hs = HeartlessSummoningFactory.Create(_alice, svc);
        hs.Zone = ZoneType.Battlefield;

        bear.GetPower().Should().Be(1);

        hs.SetZone(ZoneType.Graveyard);

        bear.GetPower().Should().Be(2, "penalty lifts on LTB.");
        bear.GetToughness().Should().Be(2);
    }
}
