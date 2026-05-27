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
/// Tests for <see cref="TormentingVoiceFactory"/>.
///
/// Tormenting Voice (Khans of Tarkir, {1}{R}):
///   Sorcery. Discard a card, then draw two cards.
///
/// Covers:
///   - Card identity (Sorcery, {1}{R}, owner / controller).
///   - <see cref="NamedCardFactory"/> dispatcher entry.
///   - Resolve: discard 1 then draw 2; net hand size change = +1 when
///     hand had ≥1 starting card.
///   - Empty hand at resolve: discard is no-op (CR 121.4 "then" still
///     permits the draw), draws 2.
///   - Empty library: draws what's available, SBA flag set
///     (CR 704.5b).
/// </summary>
public class TormentingVoiceTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void TormentingVoice_Identity()
    {
        var c = TormentingVoiceFactory.Create(_alice);

        c.Name.Should().Be("Tormenting Voice");
        c.ManaCost.Should().Be("{1}{R}");
        c.HasType(CardType.Sorcery).Should().BeTrue();
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_TormentingVoice()
    {
        var card = NamedCardFactory.Create("Tormenting Voice", _alice);

        card.Should().BeOfType<Sorcery>();
        card.Name.Should().Be("Tormenting Voice");
        card.HasType(CardType.Sorcery).Should().BeTrue();
        card.ManaCost.Should().Be("{1}{R}");
    }

    [Fact]
    public void Resolve_DiscardsOne_ThenDrawsTwo_NetHandSize_Plus1()
    {
        // Starting hand: 1 card. Library: 3 cards.
        // Net: 1 + 2 drawn - 1 discarded = 2 in hand.
        var inHand = SeedHandCard(_alice, "Hand1");
        var top1 = SeedLibraryCard(_alice, "Top1");
        var top2 = SeedLibraryCard(_alice, "Top2");
        var top3 = SeedLibraryCard(_alice, "Top3");

        var effects = TormentingVoiceFactory.BuildResolveEffect(_alice);
        foreach (var e in effects) e.Execute();

        _alice.Zones.Hand.GetCards().Should().HaveCount(2)
            .And.Contain(new[] { top1, top2 });

        _alice.Zones.Graveyard.GetCards().Should().ContainSingle()
            .Which.Should().BeSameAs(inHand);

        _alice.Zones.Library.GetCards().Should().ContainSingle()
            .Which.Should().BeSameAs(top3);

        inHand.Zone.Should().Be(ZoneType.Graveyard);
    }

    [Fact]
    public void Resolve_EmptyHand_DiscardIsNoOp_StillDrawsTwo()
    {
        // CR 121.4 — "Discard a card, then draw two cards." With an
        // empty hand, the discard is a no-op but the draw still
        // happens.
        var top1 = SeedLibraryCard(_alice, "Top1");
        var top2 = SeedLibraryCard(_alice, "Top2");

        var effects = TormentingVoiceFactory.BuildResolveEffect(_alice);
        foreach (var e in effects) e.Execute();

        _alice.Zones.Hand.GetCards().Should().HaveCount(2)
            .And.Contain(new[] { top1, top2 });
        _alice.Zones.Graveyard.GetCards().Should().BeEmpty();
    }

    [Fact]
    public void Resolve_EmptyLibrary_DrawsWhatsAvailable_AndFlagsSbaLoss()
    {
        // Hand: 1. Library: 1. After discard, hand = 0. First draw
        // lands the only library card; second draw hits empty.
        SeedHandCard(_alice, "Hand1");
        var only = SeedLibraryCard(_alice, "Only");

        var effects = TormentingVoiceFactory.BuildResolveEffect(_alice);
        foreach (var e in effects) e.Execute();

        _alice.Zones.Library.GetCards().Should().BeEmpty();
        _alice.TriedToDrawFromEmptyLibrary.Should().BeTrue();

        _alice.Zones.Hand.GetCards().Should().ContainSingle()
            .Which.Should().BeSameAs(only);
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

    private static ICard SeedHandCard(Player p, string name)
    {
        var c = new Card(name, "");
        c.SetOwner(p);
        p.Zones.Hand.AddCard(c);
        c.SetZone(ZoneType.Hand);
        return c;
    }
}
