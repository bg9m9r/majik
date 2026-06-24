using FluentAssertions;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="DragonlordsServantFactory"/>.
///
/// Dragonlord's Servant (Dragons of Tarkir) — Creature — Goblin Shaman 1/3,
/// {1}{R}. Oracle: "Dragon spells you cast cost {1} less to cast." (CR 117.7).
///
/// Covers the card's UNIQUE behaviour (the Dragon-subtype-scoped cost reducer)
/// plus a single identity assert for printed mana cost / P-T / subtypes.
/// Dispatch + well-formedness are covered globally by CardFactoryContractTests.
/// </summary>
[Trait("Color", "R")]
public class DragonlordsServantTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    private static void PutOnBattlefield(Player owner, Card card)
    {
        owner.Zones.Battlefield.AddCard(card);
        card.SetZone(ZoneType.Battlefield);
    }

    [Fact]
    public void DragonlordsServant_Identity()
    {
        var c = DragonlordsServantFactory.Create(_alice);

        c.ManaCost.Should().Be("{1}{R}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Goblin).Should().BeTrue("Goblin is a printed subtype");
        c.HasSubtype(CardSubtype.Shaman).Should().BeTrue("Shaman is a printed subtype");
        c.Power.Should().Be(1);
        c.Toughness.Should().Be(3);

        c.Abilities.OfType<SpellCostReductionAbility>()
            .Should().HaveCount(1, "the Dragon cost-reduction rider is attached");
    }

    [Fact]
    public void DragonCast_GenericReducedByOne()
    {
        var servant = DragonlordsServantFactory.Create(_alice);
        PutOnBattlefield(_alice, servant);

        // A Dragon spell — generic-bearing so the reduction is observable in the
        // generic bucket. Shivan Dragon-shape: {4}{R}{R}, Creature — Dragon.
        var dragon = new Creature(
            name: "Test Dragon",
            manaCost: "{4}{R}{R}",
            power: 5,
            toughness: 5,
            subtypes: new[] { CardSubtype.Dragon });
        dragon.SetOwner(_alice);
        dragon.SetController(_alice);

        var effective = CostReduction.GetEffectiveCost(dragon, _alice);

        effective.Generic.Should().Be(3, "{4} generic reduced by 1 → {3}");
        effective.Red.Should().Be(2, "coloured pips untouched (CR 117.7c)");
        effective.TotalValue.Should().Be(5);
    }

    [Fact]
    public void NonDragonCast_NoReduction()
    {
        var servant = DragonlordsServantFactory.Create(_alice);
        PutOnBattlefield(_alice, servant);

        // A non-Dragon creature — {2}{R}. The rider's predicate matches only
        // Dragon spells; this must pass through at full printed cost.
        var goblin = new Creature(
            name: "Test Goblin",
            manaCost: "{2}{R}",
            power: 2,
            toughness: 2,
            subtypes: new[] { CardSubtype.Goblin });
        goblin.SetOwner(_alice);
        goblin.SetController(_alice);

        var effective = CostReduction.GetEffectiveCost(goblin, _alice);

        effective.Generic.Should().Be(2, "non-Dragon spell — no Servant discount");
        effective.Red.Should().Be(1);
        effective.TotalValue.Should().Be(3);
    }

    [Fact]
    public void OffBattlefield_NoReduction()
    {
        // Servant in hand, not on battlefield → rider inert.
        var servant = DragonlordsServantFactory.Create(_alice);
        _alice.Zones.Hand.AddCard(servant);
        servant.SetZone(ZoneType.Hand);

        var dragon = new Creature(
            name: "Test Dragon",
            manaCost: "{4}{R}{R}",
            power: 5,
            toughness: 5,
            subtypes: new[] { CardSubtype.Dragon });
        dragon.SetOwner(_alice);
        dragon.SetController(_alice);

        var effective = CostReduction.GetEffectiveCost(dragon, _alice);

        effective.Generic.Should().Be(4, "Servant isn't on the battlefield — no discount");
        effective.Red.Should().Be(2);
    }

    [Fact]
    public void TwoServants_ReductionStacks()
    {
        // Two Servants each contribute {1} → {2} total reduction.
        var s1 = DragonlordsServantFactory.Create(_alice);
        var s2 = DragonlordsServantFactory.Create(_alice);
        PutOnBattlefield(_alice, s1);
        PutOnBattlefield(_alice, s2);

        var dragon = new Creature(
            name: "Test Dragon",
            manaCost: "{4}{R}{R}",
            power: 5,
            toughness: 5,
            subtypes: new[] { CardSubtype.Dragon });
        dragon.SetOwner(_alice);
        dragon.SetController(_alice);

        var effective = CostReduction.GetEffectiveCost(dragon, _alice);

        effective.Generic.Should().Be(2, "two Servants reduce {4} generic → {2}");
        effective.Red.Should().Be(2, "coloured pips still required");
        effective.TotalValue.Should().Be(4);
    }

    [Fact]
    public void OpponentControlsServant_DoesNotDiscountYourSpells()
    {
        // Bob controls a Servant; Alice casts a Dragon. The rider is scoped to
        // the controller's battlefield ("spells YOU cast"), so Alice gets no
        // discount.
        var bobServant = DragonlordsServantFactory.Create(_bob);
        PutOnBattlefield(_bob, bobServant);

        var aliceDragon = new Creature(
            name: "Test Dragon",
            manaCost: "{4}{R}{R}",
            power: 5,
            toughness: 5,
            subtypes: new[] { CardSubtype.Dragon });
        aliceDragon.SetOwner(_alice);
        aliceDragon.SetController(_alice);

        var effective = CostReduction.GetEffectiveCost(aliceDragon, _alice);

        effective.Generic.Should().Be(4,
            "Bob's Servant doesn't reduce Alice's spells — 'spells you cast' is " +
            "scoped to the controller of the reducer permanent");
        effective.Red.Should().Be(2);
    }
}
