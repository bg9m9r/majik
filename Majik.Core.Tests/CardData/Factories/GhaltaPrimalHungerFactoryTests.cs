using FluentAssertions;
using Majik.Core.Abilities;
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
/// Unit tests for <see cref="GhaltaPrimalHungerFactory"/>.
///
/// Card: Ghalta, Primal Hunger — Legendary Creature — Elder Dinosaur
///   {10}{G}{G}, 12/12 (Rivals of Ixalan).
///   "This spell costs {X} less to cast, where X is the total power of
///    creatures you control.
///    Trample"
///
/// Covers the card's UNIQUE behaviour:
/// - Identity (name, {10}{G}{G}, Legendary supertype, Creature type, Elder +
///   Dinosaur subtypes, 12/12) plus the Trample keyword marker (CR 702.19).
/// - Self cost-reduction (CR 117.7):
///     * No creatures — full {10}{G}{G}.
///     * Creatures present — generic reduced by their total power, the two
///       {G} pips untouched.
///     * Reduction exceeding the {10} generic floors at zero ({G}{G} stays).
///     * Off-battlefield creatures excluded (controller scope).
/// - TotalPowerOfCreaturesYouControl helper tallies battlefield creatures.
/// </summary>
[Trait("Color", "G")]
public class GhaltaPrimalHungerFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    private static void PutOnBattlefield(Player owner, Card card)
    {
        owner.Zones.Battlefield.AddCard(card);
        card.SetZone(ZoneType.Battlefield);
    }

    private static Creature CreaturePermanent(Player owner, string name, int power, int toughness)
    {
        var c = new Creature(name, "{1}", power: power, toughness: toughness);
        c.SetOwner(owner);
        c.SetController(owner);
        return c;
    }

    // -------------------------------------------------------------------------
    // Identity + Trample
    // -------------------------------------------------------------------------

    [Fact]
    public void Ghalta_Identity()
    {
        var c = GhaltaPrimalHungerFactory.Create(_alice);

        c.Name.Should().Be("Ghalta, Primal Hunger");
        c.ManaCost.Should().Be("{10}{G}{G}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSupertype(CardSupertype.Legendary).Should().BeTrue("Ghalta is a Legendary creature (CR 205.4)");
        c.HasSubtype(CardSubtype.Elder).Should().BeTrue("Elder is a printed subtype");
        c.HasSubtype(CardSubtype.Dinosaur).Should().BeTrue("Dinosaur is a printed subtype");
        c.Power.Should().Be(12);
        c.Toughness.Should().Be(12);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);

        c.Abilities.OfType<KeywordAbility>()
            .Should().Contain(k => k.Keyword == "Trample", "CR 702.19 — Ghalta has Trample");
        c.Abilities.OfType<CostReductionAbility>()
            .Should().HaveCount(1, "the self cost-reduction static is attached");
    }

    // -------------------------------------------------------------------------
    // Self cost-reduction (CR 117.7)
    // -------------------------------------------------------------------------

    [Fact]
    public void NoCreatures_FullCost()
    {
        var ghalta = GhaltaPrimalHungerFactory.Create(_alice);

        var effective = CostReduction.GetEffectiveCost(ghalta, _alice);

        effective.Generic.Should().Be(10, "no creatures → no discount");
        effective.TotalValue.Should().Be(12, "{10}{G}{G} — the two green pips count toward mana value");
    }

    [Fact]
    public void CreaturesYouControl_ReduceGenericByTotalPower()
    {
        var ghalta = GhaltaPrimalHungerFactory.Create(_alice);

        // Two creatures: power 3 + 2 = 5 total power.
        PutOnBattlefield(_alice, CreaturePermanent(_alice, "Bear", power: 3, toughness: 3));
        PutOnBattlefield(_alice, CreaturePermanent(_alice, "Elf", power: 2, toughness: 1));

        var effective = CostReduction.GetEffectiveCost(ghalta, _alice);

        effective.Generic.Should().Be(5, "{10} generic reduced by total power 5 → {5}");
        effective.Green.Should().Be(2, "the two {G} pips are untouched (CR 117.7c)");
    }

    [Fact]
    public void TotalPower_ExceedingGeneric_FloorsAtZero_GreenPipsRemain()
    {
        var ghalta = GhaltaPrimalHungerFactory.Create(_alice);

        // 8 + 6 = 14 total power > {10} generic.
        PutOnBattlefield(_alice, CreaturePermanent(_alice, "Big", power: 8, toughness: 8));
        PutOnBattlefield(_alice, CreaturePermanent(_alice, "Bigger", power: 6, toughness: 6));

        var effective = CostReduction.GetEffectiveCost(ghalta, _alice);

        effective.Generic.Should().Be(0, "reduction exceeds the {10} generic; floor-at-zero (CR 117.7c)");
        effective.Green.Should().Be(2, "only generic mana is reduced — the {G}{G} pips remain");
    }

    [Fact]
    public void OffBattlefieldCreatures_DoNotCount()
    {
        var ghalta = GhaltaPrimalHungerFactory.Create(_alice);

        // A creature in hand — not "you control" on the battlefield.
        var inHand = CreaturePermanent(_alice, "Hand Creature", power: 5, toughness: 5);
        _alice.Zones.Hand.AddCard(inHand);
        inHand.SetZone(ZoneType.Hand);

        var effective = CostReduction.GetEffectiveCost(ghalta, _alice);

        effective.Generic.Should().Be(10, "creature in hand isn't on the battlefield — no discount");
    }

    [Fact]
    public void TotalPowerOfCreaturesYouControl_Helper_TalliesBattlefieldCreatures()
    {
        PutOnBattlefield(_alice, CreaturePermanent(_alice, "A", power: 4, toughness: 4));
        PutOnBattlefield(_alice, CreaturePermanent(_alice, "B", power: 1, toughness: 1));
        // A non-creature artifact must not contribute.
        var artifact = new Artifact("Sol Ring", "{1}");
        artifact.SetOwner(_alice);
        artifact.SetController(_alice);
        PutOnBattlefield(_alice, artifact);

        GhaltaPrimalHungerFactory.TotalPowerOfCreaturesYouControl(_alice).Should().Be(5);
    }
}
