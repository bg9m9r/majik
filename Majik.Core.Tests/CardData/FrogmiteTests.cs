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

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Unit tests for <see cref="FrogmiteFactory"/>.
///
/// Card: Frogmite — Artifact Creature — Frog {4} 2/2 (Mirrodin).
///   "Affinity for artifacts (This spell costs {1} less to cast for each
///    artifact you control.)"
///
/// Covers:
///   - Identity (name, dual types Artifact + Creature, subtype Frog,
///     mana cost {4}, 2/2, owner/controller).
///   - NamedCardFactory dispatch returns a Creature with the Affinity
///     cost reducer + KeywordAbility("Affinity") marker.
///   - Affinity for artifacts (CR 702.40) — generic reduced by 1 per
///     controlled artifact at 0 / 3 / 4 / 5+ artifact counts; floor-at-
///     zero at 5+.
/// </summary>
public class FrogmiteTests
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
    public void Frogmite_Identity()
    {
        var c = FrogmiteFactory.Create(_alice);

        c.Name.Should().Be("Frogmite");
        c.ManaCost.Should().Be("{4}");
        c.HasType(CardType.Creature).Should().BeTrue("Frogmite is a Creature");
        c.HasType(CardType.Artifact).Should().BeTrue("Frogmite is also an Artifact (CR 301.1 / 302.1)");
        c.HasSubtype(CardSubtype.Frog).Should().BeTrue("Frog is the printed creature subtype");
        c.Power.Should().Be(2);
        c.Toughness.Should().Be(2);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void Frogmite_DispatchesViaNamedCardFactory()
    {
        var c = NamedCardFactory.Create("Frogmite", _alice);

        c.Should().BeOfType<Creature>();
        c.Name.Should().Be("Frogmite");
        c.HasType(CardType.Artifact).Should().BeTrue();
        c.HasSubtype(CardSubtype.Frog).Should().BeTrue();
        c.Abilities.OfType<CostReductionAbility>().Should().HaveCount(1,
            "the Affinity-for-artifacts cost reducer is attached");
        c.Abilities.OfType<KeywordAbility>()
            .Should().ContainSingle(k => k.Keyword == "Affinity",
                "the Affinity keyword marker is attached for keyword-scan callers");
    }

    [Fact]
    public void Frogmite_AbilityList_OneCostReducer_OneKeywordMarker()
    {
        var c = FrogmiteFactory.Create(_alice);

        c.Abilities.OfType<CostReductionAbility>().Should().HaveCount(1);
        c.Abilities.OfType<KeywordAbility>().Should().ContainSingle(k => k.Keyword == "Affinity");
    }

    // -------------------------------------------------------------------------
    // Affinity for artifacts (CR 702.40)
    // -------------------------------------------------------------------------

    [Fact]
    public void Affinity_NoArtifactsControlled_FullPrintedCost()
    {
        var frogmite = FrogmiteFactory.Create(_alice);
        _alice.Zones.Hand.AddCard(frogmite);
        frogmite.SetZone(ZoneType.Hand);

        var effective = CostReduction.GetEffectiveCost(frogmite, _alice);

        effective.Generic.Should().Be(4, "no artifacts controlled — no Affinity discount");
        effective.TotalValue.Should().Be(4);
    }

    [Fact]
    public void Affinity_ThreeArtifactsControlled_GenericReducedByThree()
    {
        var frogmite = FrogmiteFactory.Create(_alice);
        _alice.Zones.Hand.AddCard(frogmite);
        frogmite.SetZone(ZoneType.Hand);

        for (var i = 0; i < 3; i++)
        {
            var bauble = new Artifact($"Artifact {i}", "{0}");
            PutOnBattlefield(_alice, bauble);
        }

        var effective = CostReduction.GetEffectiveCost(frogmite, _alice);

        effective.Generic.Should().Be(1, "{4} reduced by 3 → {1}");
        effective.TotalValue.Should().Be(1);
    }

    [Fact]
    public void Affinity_FourArtifactsControlled_FreeCast()
    {
        var frogmite = FrogmiteFactory.Create(_alice);
        _alice.Zones.Hand.AddCard(frogmite);
        frogmite.SetZone(ZoneType.Hand);

        for (var i = 0; i < 4; i++)
        {
            var bauble = new Artifact($"Artifact {i}", "{0}");
            PutOnBattlefield(_alice, bauble);
        }

        var effective = CostReduction.GetEffectiveCost(frogmite, _alice);

        effective.Generic.Should().Be(0, "{4} reduced by 4 → {0}");
        effective.TotalValue.Should().Be(0);
    }

    [Fact]
    public void Affinity_SixArtifactsControlled_FloorAtZero()
    {
        var frogmite = FrogmiteFactory.Create(_alice);
        _alice.Zones.Hand.AddCard(frogmite);
        frogmite.SetZone(ZoneType.Hand);

        for (var i = 0; i < 6; i++)
        {
            var bauble = new Artifact($"Artifact {i}", "{0}");
            PutOnBattlefield(_alice, bauble);
        }

        var effective = CostReduction.GetEffectiveCost(frogmite, _alice);

        effective.Generic.Should().Be(0, "reduction floors at 0 — never negative (CR 117.7c)");
        effective.TotalValue.Should().Be(0);
    }
}
