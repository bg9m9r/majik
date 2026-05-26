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

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Unit tests for <see cref="ThoughtcastFactory"/>.
///
/// Card: Thoughtcast — Sorcery {4}{U} (Mirrodin).
///   "Affinity for artifacts (This spell costs {1} less to cast for each
///    artifact you control.)
///    Draw two cards."
///
/// Covers:
///   - Identity (name, Sorcery type, mana cost {4}{U}, owner/controller).
///   - NamedCardFactory dispatch returns a Sorcery with the Affinity cost
///     reducer + Affinity keyword marker.
///   - Affinity for artifacts (CR 702.40) — generic reduced by 1 per
///     controlled artifact; floor-at-zero (CR 117.7c). {U} pip survives
///     the reduction.
///   - Resolve effect draws two cards from top of library.
///   - Empty library mid-resolve flags the SBA-driven loss.
/// </summary>
public class ThoughtcastTests
{
    private readonly Player _alice = new("Alice", 20);

    private static void PutOnBattlefield(Player owner, Card card)
    {
        card.SetOwner(owner);
        card.SetController(owner);
        owner.Zones.Battlefield.AddCard(card);
        card.SetZone(ZoneType.Battlefield);
    }

    private static Card SeedLibraryCard(Player owner, string name)
    {
        var c = new Creature(name, "{0}", 1, 1);
        c.SetOwner(owner);
        c.SetController(owner);
        owner.Zones.Library.AddCard(c);
        c.SetZone(ZoneType.Library);
        return c;
    }

    // -------------------------------------------------------------------------
    // Identity + dispatch
    // -------------------------------------------------------------------------

    [Fact]
    public void Thoughtcast_Identity()
    {
        var c = ThoughtcastFactory.Create(_alice);

        c.Name.Should().Be("Thoughtcast");
        c.ManaCost.Should().Be("{4}{U}");
        c.HasType(CardType.Sorcery).Should().BeTrue();
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void Thoughtcast_DispatchesViaNamedCardFactory()
    {
        var c = NamedCardFactory.Create("Thoughtcast", _alice);

        c.Should().BeOfType<Sorcery>();
        c.Name.Should().Be("Thoughtcast");
        c.HasType(CardType.Sorcery).Should().BeTrue();
        c.Abilities.OfType<CostReductionAbility>().Should().HaveCount(1,
            "the Affinity-for-artifacts cost reducer is attached");
        c.Abilities.OfType<KeywordAbility>()
            .Should().ContainSingle(k => k.Keyword == "Affinity",
                "the Affinity keyword marker is attached for keyword-scan callers");
    }

    // -------------------------------------------------------------------------
    // Affinity for artifacts (CR 702.40)
    // -------------------------------------------------------------------------

    [Fact]
    public void Affinity_NoArtifacts_FullPrintedCost()
    {
        var thoughtcast = ThoughtcastFactory.Create(_alice);
        _alice.Zones.Hand.AddCard(thoughtcast);
        thoughtcast.SetZone(ZoneType.Hand);

        var effective = CostReduction.GetEffectiveCost(thoughtcast, _alice);

        effective.Generic.Should().Be(4);
        effective.Blue.Should().Be(1, "the {U} pip is unaffected by Affinity (CR 117.7c — only generic reduces)");
        effective.TotalValue.Should().Be(5);
    }

    [Fact]
    public void Affinity_ThreeArtifacts_GenericOne_PlusBlue()
    {
        var thoughtcast = ThoughtcastFactory.Create(_alice);
        _alice.Zones.Hand.AddCard(thoughtcast);
        thoughtcast.SetZone(ZoneType.Hand);

        for (var i = 0; i < 3; i++)
        {
            PutOnBattlefield(_alice, new Artifact($"Artifact {i}", "{0}"));
        }

        var effective = CostReduction.GetEffectiveCost(thoughtcast, _alice);

        effective.Generic.Should().Be(1, "{4} reduced by 3 → {1}");
        effective.Blue.Should().Be(1);
        effective.TotalValue.Should().Be(2);
    }

    [Fact]
    public void Affinity_FourArtifacts_OneBlueOnly()
    {
        // The headline Affinity-blue dream: four artifacts → cast for {U}.
        var thoughtcast = ThoughtcastFactory.Create(_alice);
        _alice.Zones.Hand.AddCard(thoughtcast);
        thoughtcast.SetZone(ZoneType.Hand);

        for (var i = 0; i < 4; i++)
        {
            PutOnBattlefield(_alice, new Artifact($"Artifact {i}", "{0}"));
        }

        var effective = CostReduction.GetEffectiveCost(thoughtcast, _alice);

        effective.Generic.Should().Be(0, "{4} reduced by 4 → {0}");
        effective.Blue.Should().Be(1, "{U} pip is unaffected (CR 117.7c)");
        effective.TotalValue.Should().Be(1);
    }

    [Fact]
    public void Affinity_TenArtifacts_FloorAtZero_BlueRemains()
    {
        var thoughtcast = ThoughtcastFactory.Create(_alice);
        _alice.Zones.Hand.AddCard(thoughtcast);
        thoughtcast.SetZone(ZoneType.Hand);

        for (var i = 0; i < 10; i++)
        {
            PutOnBattlefield(_alice, new Artifact($"Artifact {i}", "{0}"));
        }

        var effective = CostReduction.GetEffectiveCost(thoughtcast, _alice);

        effective.Generic.Should().Be(0, "floor-at-zero (CR 117.7c) — never negative");
        effective.Blue.Should().Be(1, "{U} pip always survives");
    }

    // -------------------------------------------------------------------------
    // Resolve: draw two cards
    // -------------------------------------------------------------------------

    [Fact]
    public void Resolve_DrawsTwoCardsFromTopOfLibrary()
    {
        var c1 = SeedLibraryCard(_alice, "Top1");
        var c2 = SeedLibraryCard(_alice, "Top2");
        SeedLibraryCard(_alice, "Top3"); // remains in library

        var effects = ThoughtcastFactory.BuildResolveEffect(_alice);
        foreach (var e in effects) e.Execute();

        _alice.Zones.Hand.GetCards().Should().Contain(new[] { c1, c2 });
        c1.Zone.Should().Be(ZoneType.Hand);
        c2.Zone.Should().Be(ZoneType.Hand);
        _alice.Zones.Library.GetCards().Should().HaveCount(1,
            "exactly two cards were drawn off the top");
        _alice.TriedToDrawFromEmptyLibrary.Should().BeFalse();
    }

    [Fact]
    public void Resolve_EmptyLibrary_FlagsSbaLossOnFirstDraw()
    {
        var effects = ThoughtcastFactory.BuildResolveEffect(_alice);
        foreach (var e in effects) e.Execute();

        _alice.Zones.Hand.GetCards().Should().BeEmpty();
        _alice.TriedToDrawFromEmptyLibrary.Should().BeTrue(
            "empty library mid-draw flags the SBA-driven loss (CR 704.5b)");
    }

    [Fact]
    public void Resolve_OneCardLibrary_DrawsOne_FlagsSbaLossOnSecondDraw()
    {
        var only = SeedLibraryCard(_alice, "OnlyCard");

        var effects = ThoughtcastFactory.BuildResolveEffect(_alice);
        foreach (var e in effects) e.Execute();

        _alice.Zones.Hand.GetCards().Should().ContainSingle().Which.Should().BeSameAs(only);
        only.Zone.Should().Be(ZoneType.Hand);
        _alice.TriedToDrawFromEmptyLibrary.Should().BeTrue(
            "the second draw came up empty — SBA flag is set");
    }
}
