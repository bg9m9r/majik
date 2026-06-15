using FluentAssertions;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Tests for <see cref="ThirstForKnowledgeFactory"/>.
///
/// Thirst for Knowledge (Mirrodin, {2}{U}):
///   Instant. Draw three cards. Then discard two cards unless you
///   discard an artifact card.
///
/// Covers ONLY the card's unique behaviour (the conditional discard rider)
/// plus a single identity assert. Dispatcher + well-formedness are covered
/// for every implemented card by CardFactoryContractTests.
///
///   - Card identity (Instant, {2}{U}).
///   - Resolve with an artifact in hand: draw 3, discard ONLY the artifact
///     (net +2 hand size). Per the card's printed ruling — "you discard
///     only that card."
///   - Resolve with no artifact: draw 3, discard two cards (net +1).
///   - Empty library: draws what's available, SBA flag set (CR 704.5b).
/// </summary>
[Trait("Color", "U")]
public class ThirstForKnowledgeTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void ThirstForKnowledge_Identity()
    {
        var c = ThirstForKnowledgeFactory.Create(_alice);

        c.Name.Should().Be("Thirst for Knowledge");
        c.ManaCost.Should().Be("{2}{U}");
        c.HasType(CardType.Instant).Should().BeTrue();
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void Resolve_WithArtifact_DrawsThree_DiscardsOnlyTheArtifact()
    {
        // Hand starts empty. Library: 4 cards, one of which (Top1) is an
        // artifact. Draw 3 (Top1..Top3); then the "unless you discard an
        // artifact" clause lets us pay with the single artifact.
        // Net hand size: 0 + 3 drawn - 1 artifact discarded = 2.
        var artifact = SeedArtifactLibraryCard(_alice, "Top1");
        var top2 = SeedLibraryCard(_alice, "Top2");
        var top3 = SeedLibraryCard(_alice, "Top3");
        var top4 = SeedLibraryCard(_alice, "Top4");

        var effects = ThirstForKnowledgeFactory.BuildResolveEffect(_alice);
        foreach (var e in effects) e.Execute();

        _alice.Zones.Hand.GetCards().Should().HaveCount(2)
            .And.Contain(new[] { top2, top3 });
        _alice.Zones.Hand.GetCards().Should().NotContain(artifact);

        _alice.Zones.Graveyard.GetCards().Should().ContainSingle()
            .Which.Should().BeSameAs(artifact);
        artifact.Zone.Should().Be(ZoneType.Graveyard);

        _alice.Zones.Library.GetCards().Should().ContainSingle()
            .Which.Should().BeSameAs(top4);
    }

    [Fact]
    public void Resolve_NoArtifact_DrawsThree_DiscardsTwo_NetPlus1()
    {
        // Hand empty, library: 4 non-artifact cards. Draw 3, discard 2.
        // Net hand size: 0 + 3 - 2 = 1.
        var top1 = SeedLibraryCard(_alice, "Top1");
        var top2 = SeedLibraryCard(_alice, "Top2");
        var top3 = SeedLibraryCard(_alice, "Top3");
        SeedLibraryCard(_alice, "Top4");

        var effects = ThirstForKnowledgeFactory.BuildResolveEffect(_alice);
        foreach (var e in effects) e.Execute();

        _alice.Zones.Hand.GetCards().Should().HaveCount(1);
        _alice.Zones.Graveyard.GetCards().Should().HaveCount(2);
        // Top1 was drawn first; deterministic policy discards the most
        // recently drawn cards (last in hand), leaving Top1.
        _alice.Zones.Hand.GetCards().Should().Contain(top1);
        _alice.Zones.Graveyard.GetCards().Should().Contain(new[] { top2, top3 });
    }

    [Fact]
    public void Resolve_EmptyLibrary_DrawsWhatsAvailable_AndFlagsSbaLoss()
    {
        // Hand empty, library: only 1 card (non-artifact). Draw 1 then hit
        // an empty library on the 2nd draw → SBA loss flag (CR 704.5b).
        // After drawing 1 non-artifact, no artifact available → discard up
        // to 2; only 1 card in hand so discard that one.
        SeedLibraryCard(_alice, "Only");

        var effects = ThirstForKnowledgeFactory.BuildResolveEffect(_alice);
        foreach (var e in effects) e.Execute();

        _alice.Zones.Library.GetCards().Should().BeEmpty();
        _alice.TriedToDrawFromEmptyLibrary.Should().BeTrue();
        _alice.Zones.Hand.GetCards().Should().BeEmpty();
        _alice.Zones.Graveyard.GetCards().Should().HaveCount(1);
    }

    private static ICard SeedLibraryCard(Player p, string name)
    {
        var c = new Card(name, "");
        c.SetOwner(p);
        p.Zones.Library.AddCard(c);
        c.SetZone(ZoneType.Library);
        return c;
    }

    private static ICard SeedArtifactLibraryCard(Player p, string name)
    {
        var c = new Card(
            name,
            "",
            cardTypes: new[] { CardType.Artifact });
        c.SetOwner(p);
        p.Zones.Library.AddCard(c);
        c.SetZone(ZoneType.Library);
        return c;
    }
}
