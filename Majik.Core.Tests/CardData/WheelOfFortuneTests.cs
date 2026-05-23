using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Tests for Wheel of Fortune (Limited Edition Alpha / Revised, {2}{R},
/// Sorcery).
///
/// Oracle text:
///   "Each player discards their hand, then draws seven cards."
///
/// Covers:
///   - Card identity (Sorcery, {2}{R}, owner/controller).
///   - NamedCardFactory dispatch.
///   - Resolve: both players' hands → graveyards, both players draw 7.
///   - Hand smaller than 7: discards what is there, draws 7 fresh cards.
///   - Library smaller than 7: draws what's available and flags the
///     try-to-draw-from-empty-library SBA loss (CR 704.5b).
/// </summary>
public class WheelOfFortuneTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Card identity / dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void WheelOfFortune_IsSorcery_AtCost2R()
    {
        var w = WheelOfFortuneFactory.Create(_alice);

        w.Name.Should().Be("Wheel of Fortune");
        w.ManaCost.Should().Be("{2}{R}");
        w.HasType(CardType.Sorcery).Should().BeTrue();
        w.Owner.Should().BeSameAs(_alice);
        w.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_WheelOfFortune()
    {
        var card = NamedCardFactory.Create("Wheel of Fortune", _alice);

        card.Should().BeOfType<Sorcery>();
        card.Name.Should().Be("Wheel of Fortune");
        card.HasType(CardType.Sorcery).Should().BeTrue();
        card.ManaCost.Should().Be("{2}{R}");
        card.Owner.Should().BeSameAs(_alice);
    }

    // -----------------------------------------------------------------------
    // Resolution — discard hand, draw 7
    // -----------------------------------------------------------------------

    [Fact]
    public void Resolve_BothPlayersDiscardHand_ThenDrawSeven()
    {
        // Each player starts with 3 cards in hand and 10 cards in library
        // (more than enough for the 7-card refill).
        var aliceHand = SeedHand(_alice, 3);
        var bobHand = SeedHand(_bob, 3);
        var aliceLib = SeedLibrary(_alice, 10);
        var bobLib = SeedLibrary(_bob, 10);

        var effects = WheelOfFortuneFactory.BuildResolveEffect(
            new[] { _alice, _bob });
        foreach (var e in effects) e.Execute();

        // Both original hands moved to their owners' graveyards.
        _alice.Zones.Graveyard.GetCards().Should().BeEquivalentTo(aliceHand);
        _bob.Zones.Graveyard.GetCards().Should().BeEquivalentTo(bobHand);
        foreach (var c in aliceHand) c.Zone.Should().Be(ZoneType.Graveyard);
        foreach (var c in bobHand) c.Zone.Should().Be(ZoneType.Graveyard);

        // Both players now hold exactly 7 fresh cards — the top 7 of each
        // library before resolution.
        _alice.Zones.Hand.GetCards().Should().HaveCount(7);
        _bob.Zones.Hand.GetCards().Should().HaveCount(7);
        _alice.Zones.Hand.GetCards().Should().BeEquivalentTo(aliceLib.Take(7));
        _bob.Zones.Hand.GetCards().Should().BeEquivalentTo(bobLib.Take(7));

        // Library was drained by exactly 7.
        _alice.Zones.Library.GetCards().Should().HaveCount(3);
        _bob.Zones.Library.GetCards().Should().HaveCount(3);

        // Neither player flagged the empty-library SBA loss.
        _alice.TriedToDrawFromEmptyLibrary.Should().BeFalse();
        _bob.TriedToDrawFromEmptyLibrary.Should().BeFalse();
    }

    [Fact]
    public void Resolve_HandSmallerThanSeven_DiscardsWhatsThere_DrawsSeven()
    {
        // Alice: 0 cards in hand. Bob: 1 card in hand. Both have enough
        // library to draw 7. The "discard your hand" half is a no-op for
        // empty hands; the draw half still pulls 7.
        SeedHand(_alice, 0);
        var bobOne = SeedHand(_bob, 1);
        var aliceLib = SeedLibrary(_alice, 10);
        var bobLib = SeedLibrary(_bob, 10);

        var effects = WheelOfFortuneFactory.BuildResolveEffect(
            new[] { _alice, _bob });
        foreach (var e in effects) e.Execute();

        // Alice's graveyard stays empty (had no hand to discard).
        _alice.Zones.Graveyard.GetCards().Should().BeEmpty();
        // Bob's single hand card moved to his graveyard.
        _bob.Zones.Graveyard.GetCards().Should().BeEquivalentTo(bobOne);

        // Both drew exactly 7.
        _alice.Zones.Hand.GetCards().Should().HaveCount(7);
        _bob.Zones.Hand.GetCards().Should().HaveCount(7);
        _alice.Zones.Hand.GetCards().Should().BeEquivalentTo(aliceLib.Take(7));
        _bob.Zones.Hand.GetCards().Should().BeEquivalentTo(bobLib.Take(7));
    }

    [Fact]
    public void Resolve_LibrarySmallerThanSeven_DrawsWhatsAvailable_AndFlagsSbaLoss()
    {
        // Alice's library has only 3 cards — she'll draw all 3 then try
        // to draw a 4th from an empty library, flagging the SBA loss
        // (CR 704.5b). Bob has a full library and is unaffected.
        SeedHand(_alice, 2);
        SeedHand(_bob, 2);
        var aliceLib = SeedLibrary(_alice, 3);
        var bobLib = SeedLibrary(_bob, 10);

        var effects = WheelOfFortuneFactory.BuildResolveEffect(
            new[] { _alice, _bob });
        foreach (var e in effects) e.Execute();

        // Alice ended up with exactly 3 cards in hand (full library drained).
        _alice.Zones.Library.GetCards().Should().BeEmpty();
        _alice.Zones.Hand.GetCards().Should().HaveCount(3);
        _alice.Zones.Hand.GetCards().Should().BeEquivalentTo(aliceLib);
        _alice.TriedToDrawFromEmptyLibrary.Should().BeTrue(
            "the 4th draw hit an empty library — SBA flag must be set");

        // Bob is unaffected by Alice's empty draw — still drew 7 cleanly.
        _bob.Zones.Hand.GetCards().Should().HaveCount(7);
        _bob.Zones.Hand.GetCards().Should().BeEquivalentTo(bobLib.Take(7));
        _bob.TriedToDrawFromEmptyLibrary.Should().BeFalse();
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static IReadOnlyList<ICard> SeedHand(Player p, int n)
    {
        var seeded = new List<ICard>(n);
        for (var i = 0; i < n; i++)
        {
            var c = new Card($"{p.Name}-Hand-{i}", "");
            c.SetOwner(p);
            p.Zones.Hand.AddCard(c);
            c.SetZone(ZoneType.Hand);
            seeded.Add(c);
        }
        return seeded;
    }

    private static IReadOnlyList<ICard> SeedLibrary(Player p, int n)
    {
        var seeded = new List<ICard>(n);
        for (var i = 0; i < n; i++)
        {
            var c = new Card($"{p.Name}-Lib-{i}", "");
            c.SetOwner(p);
            p.Zones.Library.AddCard(c);
            c.SetZone(ZoneType.Library);
            seeded.Add(c);
        }
        return seeded;
    }
}
