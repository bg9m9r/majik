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
/// Tests for <see cref="CatharticReunionFactory"/>.
///
/// Cathartic Reunion (Kaladesh, {1}{R}):
///   Sorcery. As an additional cost to cast this spell, discard two cards.
///   Draw three cards.
///
/// Covers:
///   - Card identity (Sorcery, {1}{R}, owner / controller).
///   - <see cref="NamedCardFactory"/> dispatcher entry.
///   - Resolve: discard 2 then draw 3; net hand size change = -2 + 3 = +1
///     when hand had ≥2 starting cards.
///   - Short hand at resolve: discards what's available, still draws all 3.
///   - Empty library: draws what's available, SBA flag set
///     (CR 704.5b).
///
/// Note on the documented printed-text deviation (additional cost vs
/// resolve-side discard): see <see cref="CatharticReunionFactory"/>'s XML
/// docs. v1 ships the discard at resolve, so this test suite exercises
/// resolve-side discard behaviour rather than the cast-time additional-
/// cost gate.
/// </summary>
public class CatharticReunionTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void CatharticReunion_Identity()
    {
        var c = CatharticReunionFactory.Create(_alice);

        c.Name.Should().Be("Cathartic Reunion");
        c.ManaCost.Should().Be("{1}{R}");
        c.HasType(CardType.Sorcery).Should().BeTrue();
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_CatharticReunion()
    {
        var card = NamedCardFactory.Create("Cathartic Reunion", _alice);

        card.Should().BeOfType<Sorcery>();
        card.Name.Should().Be("Cathartic Reunion");
        card.HasType(CardType.Sorcery).Should().BeTrue();
        card.ManaCost.Should().Be("{1}{R}");
        card.Owner.Should().BeSameAs(_alice);
    }

    [Fact]
    public void Resolve_DiscardsTwo_ThenDrawsThree_NetHandSize_Plus1()
    {
        // Starting hand: 2 cards. Library: 4 cards.
        // Net: 2 + 3 drawn - 2 discarded = 3 in hand.
        var inHand1 = SeedHandCard(_alice, "Hand1");
        var inHand2 = SeedHandCard(_alice, "Hand2");
        var top1 = SeedLibraryCard(_alice, "Top1");
        var top2 = SeedLibraryCard(_alice, "Top2");
        var top3 = SeedLibraryCard(_alice, "Top3");
        var top4 = SeedLibraryCard(_alice, "Top4");

        var effects = CatharticReunionFactory.BuildResolveEffect(_alice);
        foreach (var e in effects) e.Execute();

        _alice.Zones.Hand.GetCards().Should().HaveCount(3);

        // Deterministic v1 discard picks the last two cards in hand. The
        // starting hand (inHand1, inHand2) is what's available at the
        // discard step — discards happen BEFORE draws, so the starting
        // hand is what gets discarded. Top1..Top3 land AFTER discards.
        _alice.Zones.Graveyard.GetCards().Should().Contain(new[] { inHand1, inHand2 });
        _alice.Zones.Hand.GetCards().Should().Contain(new[] { top1, top2, top3 });

        // Library lost exactly three cards off the top.
        _alice.Zones.Library.GetCards().Should().ContainSingle()
            .Which.Should().BeSameAs(top4);

        inHand1.Zone.Should().Be(ZoneType.Graveyard);
        inHand2.Zone.Should().Be(ZoneType.Graveyard);
    }

    [Fact]
    public void Resolve_FromEmptyHand_DiscardIsNoOp_StillDrawsThree()
    {
        // Empty hand → no discards. Library has 3 cards — all draw.
        var top1 = SeedLibraryCard(_alice, "Top1");
        var top2 = SeedLibraryCard(_alice, "Top2");
        var top3 = SeedLibraryCard(_alice, "Top3");

        var effects = CatharticReunionFactory.BuildResolveEffect(_alice);
        foreach (var e in effects) e.Execute();

        _alice.Zones.Hand.GetCards().Should().HaveCount(3)
            .And.Contain(new[] { top1, top2, top3 });
        _alice.Zones.Graveyard.GetCards().Should().BeEmpty();
    }

    [Fact]
    public void Resolve_ShortHand_DiscardsAvailable_AndStillDrawsThree()
    {
        // Starting hand: 1 card. Should discard 1 (CR 701.16a — "discard
        // N" allows "up to N" when fewer exist) and still draw 3.
        var only = SeedHandCard(_alice, "Only");
        var top1 = SeedLibraryCard(_alice, "Top1");
        var top2 = SeedLibraryCard(_alice, "Top2");
        var top3 = SeedLibraryCard(_alice, "Top3");

        var effects = CatharticReunionFactory.BuildResolveEffect(_alice);
        foreach (var e in effects) e.Execute();

        _alice.Zones.Hand.GetCards().Should().HaveCount(3)
            .And.Contain(new[] { top1, top2, top3 });
        _alice.Zones.Graveyard.GetCards().Should().ContainSingle()
            .Which.Should().BeSameAs(only);
    }

    [Fact]
    public void Resolve_EmptyLibrary_DrawsWhatsAvailable_AndFlagsSbaLoss()
    {
        // Hand: 2 cards. Library: 1 card. After discard 2, hand has 0;
        // first draw lands the only library card; remaining 2 draws hit
        // empty → SBA flag set.
        SeedHandCard(_alice, "Hand1");
        SeedHandCard(_alice, "Hand2");
        var only = SeedLibraryCard(_alice, "Only");

        var effects = CatharticReunionFactory.BuildResolveEffect(_alice);
        foreach (var e in effects) e.Execute();

        _alice.Zones.Library.GetCards().Should().BeEmpty();
        _alice.TriedToDrawFromEmptyLibrary.Should().BeTrue(
            "second / third draw hit an empty library — SBA flag must be set");
        _alice.Zones.Hand.GetCards().Should().ContainSingle()
            .Which.Should().BeSameAs(only);
        _alice.Zones.Graveyard.GetCards().Should().HaveCount(2);
    }

    private static ICard SeedLibraryCard(Player p, string name)
    {
        var c = new Card(name, "");
        c.SetOwner(p);
        p.Zones.Library.AddCard(c);
        c.SetZone(ZoneType.Library);
        return c;
    }

    private static ICard SeedHandCard(Player p, string name)
    {
        var c = new Card(name, "");
        c.SetOwner(p);
        p.Zones.Hand.AddCard(c);
        c.SetZone(ZoneType.Hand);
        return c;
    }
}
