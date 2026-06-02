using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Tests for <see cref="ThirstForDiscoveryFactory"/>.
///
/// Thirst for Discovery (Modern Horizons 3, {2}{U}):
///   Instant. Draw three cards. Then discard two cards unless you
///   discard a basic land card.
///
/// Covers:
///   - Card identity (Instant, {2}{U}, owner / controller).
///   - <see cref="NamedCardFactory"/> dispatcher entry.
///   - Resolve with a basic land in hand: draw 3, discard ONLY the
///     basic land (net +2 hand size). Per the card's printed ruling —
///     "you discard only that card."
///   - Resolve with no basic land: draw 3, discard two cards
///     (net +1 hand size).
///   - Empty library: draws what's available, SBA flag set
///     (CR 704.5b).
/// </summary>
public class ThirstForDiscoveryTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void ThirstForDiscovery_Identity()
    {
        var c = ThirstForDiscoveryFactory.Create(_alice);

        c.Name.Should().Be("Thirst for Discovery");
        c.ManaCost.Should().Be("{2}{U}");
        c.HasType(CardType.Instant).Should().BeTrue();
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_ThirstForDiscovery()
    {
        var card = NamedCardFactory.Create("Thirst for Discovery", _alice);

        card.Should().BeOfType<Instant>();
        card.Name.Should().Be("Thirst for Discovery");
        card.HasType(CardType.Instant).Should().BeTrue();
        card.ManaCost.Should().Be("{2}{U}");
    }

    [Fact]
    public void Resolve_WithBasicLand_DrawsThree_DiscardsOnlyTheBasicLand()
    {
        // Hand starts empty. Library: 4 cards, one of which (Top1) is a
        // basic land. Draw 3 (Top1..Top3); then the "unless you discard a
        // basic land" clause lets us pay with the single basic land.
        // Net hand size: 0 + 3 drawn - 1 basic land discarded = 2.
        var island = SeedBasicLandLibraryCard(_alice, "Top1");
        var top2 = SeedLibraryCard(_alice, "Top2");
        var top3 = SeedLibraryCard(_alice, "Top3");
        var top4 = SeedLibraryCard(_alice, "Top4");

        var effects = ThirstForDiscoveryFactory.BuildResolveEffect(_alice);
        foreach (var e in effects) e.Execute();

        _alice.Zones.Hand.GetCards().Should().HaveCount(2)
            .And.Contain(new[] { top2, top3 });
        _alice.Zones.Hand.GetCards().Should().NotContain(island);

        _alice.Zones.Graveyard.GetCards().Should().ContainSingle()
            .Which.Should().BeSameAs(island);
        island.Zone.Should().Be(ZoneType.Graveyard);

        _alice.Zones.Library.GetCards().Should().ContainSingle()
            .Which.Should().BeSameAs(top4);
    }

    [Fact]
    public void Resolve_NoBasicLand_DrawsThree_DiscardsTwo_NetPlus1()
    {
        // Hand empty, library: 4 non-land cards. Draw 3, discard 2.
        // Net hand size: 0 + 3 - 2 = 1.
        var top1 = SeedLibraryCard(_alice, "Top1");
        var top2 = SeedLibraryCard(_alice, "Top2");
        var top3 = SeedLibraryCard(_alice, "Top3");
        SeedLibraryCard(_alice, "Top4");

        var effects = ThirstForDiscoveryFactory.BuildResolveEffect(_alice);
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
        // Hand empty, library: only 1 card (non-land). Draw 1 then hit
        // an empty library on the 2nd draw → SBA loss flag (CR 704.5b).
        // After drawing 1 non-land, no basic land available → discard up
        // to 2; only 1 card in hand so discard that one.
        SeedLibraryCard(_alice, "Only");

        var effects = ThirstForDiscoveryFactory.BuildResolveEffect(_alice);
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

    private static ICard SeedBasicLandLibraryCard(Player p, string name)
    {
        var c = new Card(
            name,
            "",
            cardTypes: new[] { CardType.Land },
            supertypes: new[] { CardSupertype.Basic },
            subtypes: new[] { CardSubtype.Island });
        c.SetOwner(p);
        p.Zones.Library.AddCard(c);
        c.SetZone(ZoneType.Library);
        return c;
    }
}
