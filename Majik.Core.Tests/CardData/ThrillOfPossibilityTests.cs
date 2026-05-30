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
/// Tests for <see cref="ThrillOfPossibilityFactory"/>.
///
/// Thrill of Possibility (Throne of Eldraine, {1}{R}):
///   Instant. As an additional cost to cast this spell, discard a card.
///   Draw two cards.
///
/// Same additional-discard-cost + draw shape as
/// <see cref="CatharticReunionFactory"/> (discard 2, draw 3), reduced to
/// discard 1 / draw 2 at instant speed.
///
/// Covers:
///   - Card identity (Instant, {1}{R}, owner / controller).
///   - <see cref="NamedCardFactory"/> dispatcher entry.
///   - Resolve: discard 1 then draw 2; net hand size change = -1 + 2 = +1
///     when hand had ≥1 starting card.
///   - Empty hand at resolve: discard is a no-op, still draws 2.
///   - Empty library: draws what's available, SBA flag set (CR 704.5b).
///
/// Note on the documented printed-text deviation (additional cost vs
/// resolve-side discard): see <see cref="ThrillOfPossibilityFactory"/>'s XML
/// docs. v1 ships the discard at resolve, mirroring Cathartic Reunion.
/// </summary>
public class ThrillOfPossibilityTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void ThrillOfPossibility_Identity()
    {
        var c = ThrillOfPossibilityFactory.Create(_alice);

        c.Name.Should().Be("Thrill of Possibility");
        c.ManaCost.Should().Be("{1}{R}");
        c.HasType(CardType.Instant).Should().BeTrue();
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_ThrillOfPossibility()
    {
        var card = NamedCardFactory.Create("Thrill of Possibility", _alice);

        card.Should().BeOfType<Instant>();
        card.Name.Should().Be("Thrill of Possibility");
        card.HasType(CardType.Instant).Should().BeTrue();
        card.ManaCost.Should().Be("{1}{R}");
        card.Owner.Should().BeSameAs(_alice);
    }

    [Fact]
    public void Resolve_DiscardsOne_ThenDrawsTwo_NetHandSize_Plus1()
    {
        // Starting hand: 1 card. Library: 3 cards.
        // Net: 1 + 2 drawn - 1 discarded = 2 in hand.
        var inHand1 = SeedHandCard(_alice, "Hand1");
        var top1 = SeedLibraryCard(_alice, "Top1");
        var top2 = SeedLibraryCard(_alice, "Top2");
        var top3 = SeedLibraryCard(_alice, "Top3");

        var effects = ThrillOfPossibilityFactory.BuildResolveEffect(_alice);
        foreach (var e in effects) e.Execute();

        _alice.Zones.Hand.GetCards().Should().HaveCount(2);

        // Deterministic v1 discard picks the last card in hand. The starting
        // hand (inHand1) is what's available at the discard step — discards
        // happen BEFORE draws, so it gets discarded. Top1..Top2 land AFTER.
        _alice.Zones.Graveyard.GetCards().Should().ContainSingle()
            .Which.Should().BeSameAs(inHand1);
        _alice.Zones.Hand.GetCards().Should().Contain(new[] { top1, top2 });

        // Library lost exactly two cards off the top.
        _alice.Zones.Library.GetCards().Should().ContainSingle()
            .Which.Should().BeSameAs(top3);

        inHand1.Zone.Should().Be(ZoneType.Graveyard);
    }

    [Fact]
    public void Resolve_FromEmptyHand_DiscardIsNoOp_StillDrawsTwo()
    {
        // Empty hand → no discard. Library has 2 cards — both draw.
        var top1 = SeedLibraryCard(_alice, "Top1");
        var top2 = SeedLibraryCard(_alice, "Top2");

        var effects = ThrillOfPossibilityFactory.BuildResolveEffect(_alice);
        foreach (var e in effects) e.Execute();

        _alice.Zones.Hand.GetCards().Should().HaveCount(2)
            .And.Contain(new[] { top1, top2 });
        _alice.Zones.Graveyard.GetCards().Should().BeEmpty();
    }

    [Fact]
    public void Resolve_EmptyLibrary_DrawsWhatsAvailable_AndFlagsSbaLoss()
    {
        // Hand: 1 card. Library: 1 card. After discard 1, hand has 0;
        // first draw lands the only library card; second draw hits
        // empty → SBA flag set.
        SeedHandCard(_alice, "Hand1");
        var only = SeedLibraryCard(_alice, "Only");

        var effects = ThrillOfPossibilityFactory.BuildResolveEffect(_alice);
        foreach (var e in effects) e.Execute();

        _alice.Zones.Library.GetCards().Should().BeEmpty();
        _alice.TriedToDrawFromEmptyLibrary.Should().BeTrue(
            "second draw hit an empty library — SBA flag must be set");
        _alice.Zones.Hand.GetCards().Should().ContainSingle()
            .Which.Should().BeSameAs(only);
        _alice.Zones.Graveyard.GetCards().Should().ContainSingle();
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
