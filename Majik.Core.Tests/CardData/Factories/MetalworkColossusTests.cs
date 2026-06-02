using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;
using Artifact = Majik.Core.Cards.Artifact;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="MetalworkColossusFactory"/>.
///
/// Card: Metalwork Colossus — Artifact Creature — Construct
///   {11}, 10/10 (Kaladesh).
///   "This spell costs {X} less to cast, where X is the total mana value of
///    noncreature artifacts you control.
///    Sacrifice two artifacts: Return this card from your graveyard to your
///    hand."
///
/// Covers:
/// - Identity (name, {11}, Artifact + Creature types, Construct subtype,
///   10/10, owner/controller).
/// - NamedCardFactory dispatch returns a Creature shell carrying the
///   self cost-reduction static + the sacrifice-two-artifacts activated
///   ability.
/// - Self cost-reduction (CR 117.7):
///     * No noncreature artifacts — full {11}.
///     * Noncreature artifacts present — generic reduced by their total
///       mana value, floored at zero.
///     * Artifact creatures excluded from the tally.
///     * Off-battlefield artifacts excluded (controller scope).
/// - Sacrifice-two-artifacts graveyard recursion (CR 602 / CR 701.16):
///     * Activated ability shape (one SacrificeTwoArtifactsCost cost).
///     * Resolution returns the card graveyard → owner's hand.
///     * Resolution from a non-graveyard zone is a clean no-op.
/// </summary>
public class MetalworkColossusTests
{
    private readonly Player _alice = new("Alice", 20);

    private static void PutOnBattlefield(Player owner, Card card)
    {
        owner.Zones.Battlefield.AddCard(card);
        card.SetZone(ZoneType.Battlefield);
    }

    private static Artifact ArtifactPermanent(Player owner, string name, string manaCost)
    {
        var a = new Artifact(name, manaCost);
        a.SetOwner(owner);
        a.SetController(owner);
        return a;
    }

    // -------------------------------------------------------------------------
    // Identity + dispatch
    // -------------------------------------------------------------------------

    [Fact]
    public void MetalworkColossus_Identity()
    {
        var c = MetalworkColossusFactory.Create(_alice);

        c.Name.Should().Be("Metalwork Colossus");
        c.ManaCost.Should().Be("{11}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasType(CardType.Artifact).Should().BeTrue("Metalwork Colossus is an Artifact Creature (CR 301.1)");
        c.HasSubtype(CardSubtype.Construct).Should().BeTrue("Construct is the printed subtype");
        c.Power.Should().Be(10);
        c.Toughness.Should().Be(10);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);

        c.Abilities.OfType<CostReductionAbility>()
            .Should().HaveCount(1, "the self cost-reduction static is attached");
        c.Abilities.OfType<ActivatedAbility>()
            .Should().HaveCount(1, "the sacrifice-two-artifacts graveyard recursion is attached");
    }

    [Fact]
    public void MetalworkColossus_DispatchesViaNamedCardFactory()
    {
        var c = NamedCardFactory.Create("Metalwork Colossus", _alice);

        c.Should().BeOfType<Creature>();
        c.Name.Should().Be("Metalwork Colossus");
        c.HasType(CardType.Artifact).Should().BeTrue();
        c.HasSubtype(CardSubtype.Construct).Should().BeTrue();
        c.Abilities.OfType<CostReductionAbility>().Should().HaveCount(1);
        c.Abilities.OfType<ActivatedAbility>().Should().HaveCount(1);
    }

    // -------------------------------------------------------------------------
    // Self cost-reduction (CR 117.7)
    // -------------------------------------------------------------------------

    [Fact]
    public void NoNoncreatureArtifacts_FullCost()
    {
        var colossus = MetalworkColossusFactory.Create(_alice);

        var effective = CostReduction.GetEffectiveCost(colossus, _alice);

        effective.Generic.Should().Be(11, "no noncreature artifacts → no discount");
        effective.TotalValue.Should().Be(11);
    }

    [Fact]
    public void NoncreatureArtifacts_ReduceByTotalManaValue()
    {
        var colossus = MetalworkColossusFactory.Create(_alice);

        // Two noncreature artifacts: {3} + {2} = 5 total mana value.
        PutOnBattlefield(_alice, ArtifactPermanent(_alice, "Worn Powerstone", "{3}"));
        PutOnBattlefield(_alice, ArtifactPermanent(_alice, "Mind Stone", "{2}"));

        var effective = CostReduction.GetEffectiveCost(colossus, _alice);

        effective.Generic.Should().Be(6, "{11} reduced by total mana value 5 → {6}");
        effective.TotalValue.Should().Be(6);
    }

    [Fact]
    public void NoncreatureArtifacts_ExceedingCost_FloorsAtZero()
    {
        var colossus = MetalworkColossusFactory.Create(_alice);

        // {8} + {6} = 14 total mana value > {11}.
        PutOnBattlefield(_alice, ArtifactPermanent(_alice, "Big Artifact", "{8}"));
        PutOnBattlefield(_alice, ArtifactPermanent(_alice, "Bigger Artifact", "{6}"));

        var effective = CostReduction.GetEffectiveCost(colossus, _alice);

        effective.Generic.Should().Be(0, "reduction exceeds {11}; floor-at-zero (CR 117.7c)");
        effective.TotalValue.Should().Be(0);
    }

    [Fact]
    public void ArtifactCreatures_DoNotCountTowardReduction()
    {
        var colossus = MetalworkColossusFactory.Create(_alice);

        // An ARTIFACT CREATURE — must NOT count ("noncreature artifacts").
        var artCreature = new Creature("Test Golem", "{5}", power: 5, toughness: 5);
        artCreature.AddCardType(CardType.Artifact);
        artCreature.SetOwner(_alice);
        artCreature.SetController(_alice);
        PutOnBattlefield(_alice, artCreature);

        // One genuine noncreature artifact {4}.
        PutOnBattlefield(_alice, ArtifactPermanent(_alice, "Sol Ring", "{4}"));

        var effective = CostReduction.GetEffectiveCost(colossus, _alice);

        effective.Generic.Should().Be(7,
            "{11} reduced only by the noncreature artifact's {4}; the artifact " +
            "creature is excluded → {7}");
    }

    [Fact]
    public void OffBattlefieldArtifacts_DoNotCount()
    {
        var colossus = MetalworkColossusFactory.Create(_alice);

        // A noncreature artifact in hand — not "you control" on the
        // battlefield, so it must not reduce.
        var inHand = ArtifactPermanent(_alice, "Hand Artifact", "{4}");
        _alice.Zones.Hand.AddCard(inHand);
        inHand.SetZone(ZoneType.Hand);

        var effective = CostReduction.GetEffectiveCost(colossus, _alice);

        effective.Generic.Should().Be(11, "artifact in hand isn't on the battlefield — no discount");
    }

    [Fact]
    public void NoncreatureArtifactManaValue_Helper_TalliesBattlefieldOnly()
    {
        PutOnBattlefield(_alice, ArtifactPermanent(_alice, "A", "{3}"));
        PutOnBattlefield(_alice, ArtifactPermanent(_alice, "B", "{1}"));

        MetalworkColossusFactory.NoncreatureArtifactManaValue(_alice).Should().Be(4);
    }

    // -------------------------------------------------------------------------
    // Sacrifice-two-artifacts graveyard recursion (CR 602 / CR 701.16)
    // -------------------------------------------------------------------------

    [Fact]
    public void GraveyardRecursion_AbilityShape()
    {
        var colossus = MetalworkColossusFactory.Create(_alice);

        var ability = colossus.Abilities.OfType<ActivatedAbility>().Single();
        ability.Costs.Should().ContainSingle()
            .Which.Should().BeOfType<SacrificeTwoArtifactsCost>(
                "the only cost is sacrificing two artifacts");
        ability.Effects.Should().NotBeEmpty();
    }

    [Fact]
    public void GraveyardRecursion_ReturnsCardToHand()
    {
        var colossus = MetalworkColossusFactory.Create(_alice);

        // Card is in the graveyard.
        _alice.Zones.Graveyard.AddCard(colossus);
        colossus.SetZone(ZoneType.Graveyard);

        var ability = colossus.Abilities.OfType<ActivatedAbility>().Single();
        ability.Resolve();

        _alice.Zones.Hand.GetCards().Should().Contain(colossus,
            "the activated ability returns the card from graveyard to its owner's hand");
        _alice.Zones.Graveyard.GetCards().Should().NotContain(colossus);
        colossus.Zone.Should().Be(ZoneType.Hand);
    }

    [Fact]
    public void GraveyardRecursion_FromNonGraveyardZone_IsNoOp()
    {
        var colossus = MetalworkColossusFactory.Create(_alice);

        // Card is on the battlefield, not the graveyard.
        PutOnBattlefield(_alice, colossus);

        var ability = colossus.Abilities.OfType<ActivatedAbility>().Single();
        ability.Resolve();

        _alice.Zones.Battlefield.GetCards().Should().Contain(colossus,
            "resolution does nothing when the source isn't in the graveyard (CR 608.2)");
        colossus.Zone.Should().Be(ZoneType.Battlefield);
    }
}
