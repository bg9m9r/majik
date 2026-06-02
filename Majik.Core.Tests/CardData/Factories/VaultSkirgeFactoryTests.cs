using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;
using Artifact = Majik.Core.Cards.Artifact;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="VaultSkirgeFactory"/>
/// (New Phyrexia, {2}{B/P}).
///
/// Artifact Creature — Imp 1/1. Oracle text:
///   "({B/P} can be paid with either {B} or 2 life.)
///    Affinity for artifacts.
///    Flying, lifelink."
///
/// Covers:
///   - Identity (dual Artifact + Creature, Imp subtype, {2}{B} runtime
///     printed cost, 1/1, owner/controller).
///   - NamedCardFactory dispatch.
///   - Affinity for artifacts wires CostReductionAbility + KeywordAbility
///     marker; Frogmite-shape reduction at 0 / 2 / 3+ artifacts (floor
///     at zero).
///   - Flying + Lifelink + Phyrexian keyword markers attached.
///   - PhyrexianAlternativeCost: AlternativeManaCost = {2}, LifeCost = 2;
///     OnResolved drains 2 life from the caster.
/// </summary>
[Trait("Color", "B")]
public class VaultSkirgeFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    private static void PutOnBattlefield(Player owner, Card card)
    {
        card.SetOwner(owner);
        card.SetController(owner);
        owner.Zones.Battlefield.AddCard(card);
        card.SetZone(ZoneType.Battlefield);
    }

    // -------------------------------------------------------------------------
    // Identity + dispatch
    // -------------------------------------------------------------------------

    [Fact]
    public void VaultSkirge_Identity()
    {
        var c = VaultSkirgeFactory.Create(_alice);

        c.Name.Should().Be("Vault Skirge");
        c.ManaCost.Should().Be("{2}{B}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasType(CardType.Artifact).Should().BeTrue(
            "Artifact Creature — CR 301.1 / 302.1");
        c.HasSubtype(CardSubtype.Imp).Should().BeTrue();
        c.BasePower.Should().Be(1);
        c.BaseToughness.Should().Be(1);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }
    // -------------------------------------------------------------------------
    // Keyword markers
    // -------------------------------------------------------------------------

    [Fact]
    public void VaultSkirge_AbilityList_HasFlyingLifelinkAffinityPhyrexianMarkers()
    {
        var c = VaultSkirgeFactory.Create(_alice);
        var kw = c.Abilities.OfType<KeywordAbility>().Select(k => k.Keyword).ToList();

        kw.Should().Contain("Flying", "CR 702.9 — Flying marker");
        kw.Should().Contain("Lifelink", "CR 702.15 — Lifelink marker");
        kw.Should().Contain("Affinity",
            "CR 702.40 — Affinity-for-artifacts discoverability marker");
        kw.Should().Contain("Phyrexian",
            "CR 107.4f / 118.8 — Phyrexian-mana marker");
    }

    [Fact]
    public void VaultSkirge_HasOneCostReductionAbility()
    {
        var c = VaultSkirgeFactory.Create(_alice);

        c.Abilities.OfType<CostReductionAbility>().Should().HaveCount(1,
            "Affinity-for-artifacts is wired as a single CostReductionAbility");
    }

    // -------------------------------------------------------------------------
    // Affinity for artifacts (CR 702.40 / CR 117.7)
    // -------------------------------------------------------------------------

    [Fact]
    public void Affinity_NoArtifactsControlled_FullPrintedCost()
    {
        var skirge = VaultSkirgeFactory.Create(_alice);
        _alice.Zones.Hand.AddCard(skirge);
        skirge.SetZone(ZoneType.Hand);

        var effective = CostReduction.GetEffectiveCost(skirge, _alice);

        effective.Generic.Should().Be(2, "no artifacts → full {2} generic");
        effective.Black.Should().Be(1, "the {B} pip is not affected by Affinity");
    }

    [Fact]
    public void Affinity_TwoArtifactsControlled_GenericReducedByTwo()
    {
        var skirge = VaultSkirgeFactory.Create(_alice);
        _alice.Zones.Hand.AddCard(skirge);
        skirge.SetZone(ZoneType.Hand);

        for (var i = 0; i < 2; i++)
        {
            PutOnBattlefield(_alice, new Artifact($"Artifact {i}", "{0}"));
        }

        var effective = CostReduction.GetEffectiveCost(skirge, _alice);

        effective.Generic.Should().Be(0, "{2} reduced by 2 → {0} generic");
        effective.Black.Should().Be(1, "Affinity does not reduce coloured pips");
    }

    [Fact]
    public void Affinity_FiveArtifactsControlled_FloorAtZero()
    {
        var skirge = VaultSkirgeFactory.Create(_alice);
        _alice.Zones.Hand.AddCard(skirge);
        skirge.SetZone(ZoneType.Hand);

        for (var i = 0; i < 5; i++)
        {
            PutOnBattlefield(_alice, new Artifact($"Artifact {i}", "{0}"));
        }

        var effective = CostReduction.GetEffectiveCost(skirge, _alice);

        effective.Generic.Should().Be(0, "reduction floors at 0 (CR 117.7c)");
        effective.Black.Should().Be(1, "{B} pip unaffected");
    }

    // -------------------------------------------------------------------------
    // Phyrexian alternative cost (CR 107.4f / CR 118.8)
    // -------------------------------------------------------------------------

    [Fact]
    public void PhyrexianAlternativeCost_ReturnsTwoGenericPlusTwoLife()
    {
        var alt = VaultSkirgeFactory.PhyrexianAlternativeCost();

        alt.AlternativeManaCost.Generic.Should().Be(2,
            "the {B/P} pip is stripped; the {2} generic remains as mana cost");
        alt.AlternativeManaCost.Black.Should().Be(0,
            "the {B/P} pip becomes the life payment — no Black remains");
        alt.LifeCost.Should().Be(2,
            "one phyrexian pip → 2 life (CR 118.8)");
    }

    [Fact]
    public void PhyrexianAlternativeCost_CanCastFor_TrueWhenCasterHasEnoughLife()
    {
        var alt = VaultSkirgeFactory.PhyrexianAlternativeCost();
        var skirge = VaultSkirgeFactory.Create(_alice);

        alt.CanCastFor(skirge, _alice).Should().BeTrue(
            "Alice has 20 life >> 2 life requirement");
    }

    [Fact]
    public void PhyrexianAlternativeCost_OnResolved_DrainsTwoLifeFromCaster()
    {
        var alt = VaultSkirgeFactory.PhyrexianAlternativeCost();
        var skirge = VaultSkirgeFactory.Create(_alice);

        alt.OnResolved(skirge, _alice);

        _alice.LifeTotal.Should().Be(18, "20 - 2 life (one phyrexian pip)");
    }
}
