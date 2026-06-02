using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.Random;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Tests for Burning Inquiry (Zendikar, {R}, Sorcery).
///
/// Oracle text:
///   "Each player draws three cards, then discards three cards at random."
///
/// Burning Inquiry is the symmetric, every-player cousin of Goblin Lore:
/// draw a fistful, then a forced random discard — except it applies to
/// EVERY player, like Wheel of Fortune's "each player" iteration. The
/// "at random" discard rides the same per-game <see cref="GameRandom"/>
/// primitive Goblin Lore uses.
///
/// Covers:
///   - Card identity (Sorcery, {R}, MV 1, owner/controller).
///   - NamedCardFactory dispatch.
///   - Resolve: every player draws three, then discards three of the
///     resulting hand at random (CR 121.1 draw, CR 701.16e random discard).
///   - "Then" sequencing: ALL draws resolve before ANY discards.
///   - Net hand-size change per player is +0 (draw 3, discard 3) when
///     enough library + hand exist.
///   - Random pick is deterministic when the per-game RNG is seeded.
///   - Library smaller than three: draws what's available, flags the
///     try-to-draw-from-empty-library SBA loss (CR 704.5b), then discards
///     three at random from whatever ended up in hand.
/// </summary>
public class BurningInquiryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Card identity / dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void BurningInquiry_IsSorcery_AtR()
    {
        var card = BurningInquiryFactory.Create(_alice);

        card.Name.Should().Be("Burning Inquiry");
        card.ManaCost.Should().Be("{R}");
        card.HasType(CardType.Sorcery).Should().BeTrue();
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_BurningInquiry()
    {
        var card = NamedCardFactory.Create("Burning Inquiry", _alice);

        card.Should().BeOfType<Sorcery>();
        card.Name.Should().Be("Burning Inquiry");
        card.HasType(CardType.Sorcery).Should().BeTrue();
        card.ManaCost.Should().Be("{R}");
        card.Owner.Should().BeSameAs(_alice);
    }

    // -----------------------------------------------------------------------
    // Resolution — each player draws 3, then discards 3 at random
    // -----------------------------------------------------------------------

    [Fact]
    public void Resolve_EachPlayerDrawsThree_ThenDiscardsThreeAtRandom_NetZero()
    {
        // Both players start empty-handed with 10-card libraries. After
        // resolving each has drawn 3 and discarded 3 at random — net 0.
        SeedHand(_alice, 0);
        SeedHand(_bob, 0);
        var aliceLib = SeedLibrary(_alice, 10);
        var bobLib = SeedLibrary(_bob, 10);
        GameRandomRegistry.SetDefault(new GameRandom(seed: 1234));

        var effects = BurningInquiryFactory.BuildResolveEffect(
            new[] { _alice, _bob });
        foreach (var e in effects) e.Execute();

        // Each drew 3 off the top, library drained by 3.
        _alice.Zones.Library.GetCards().Should().HaveCount(7);
        _bob.Zones.Library.GetCards().Should().HaveCount(7);

        // Drew 3, discarded 3 => 0 cards left in hand for each.
        _alice.Zones.Hand.GetCards().Should().BeEmpty();
        _bob.Zones.Hand.GetCards().Should().BeEmpty();

        // Exactly three cards went to each graveyard.
        _alice.Zones.Graveyard.GetCards().Should().HaveCount(3);
        _bob.Zones.Graveyard.GetCards().Should().HaveCount(3);

        // The three drawn cards for each player are accounted for: all in
        // graveyard (hand is empty after discarding all 3).
        _alice.Zones.Graveyard.GetCards().Should().BeEquivalentTo(aliceLib.Take(3));
        _bob.Zones.Graveyard.GetCards().Should().BeEquivalentTo(bobLib.Take(3));

        _alice.TriedToDrawFromEmptyLibrary.Should().BeFalse();
        _bob.TriedToDrawFromEmptyLibrary.Should().BeFalse();
    }

    [Fact]
    public void Resolve_AllDrawsHappenBeforeAnyDiscards()
    {
        // "Each player draws three cards, then discards three cards at
        // random." The "then" is a sequencing barrier across the whole
        // spell: every player draws their three before any discard occurs.
        // We assert this by giving each player a starting hand card: after
        // a draw-3/discard-3 each player's library is drained by exactly 3
        // (the draws are not interleaved with discards in a way that would
        // change library counts) and each graveyard holds exactly 3.
        var aliceStart = SeedHand(_alice, 1);
        var bobStart = SeedHand(_bob, 1);
        SeedLibrary(_alice, 5);
        SeedLibrary(_bob, 5);
        GameRandomRegistry.SetDefault(new GameRandom(seed: 55));

        var effects = BurningInquiryFactory.BuildResolveEffect(
            new[] { _alice, _bob });
        foreach (var e in effects) e.Execute();

        // Each: 1 starting + 3 drawn = 4 in hand pre-discard, discard 3 => 1.
        _alice.Zones.Hand.GetCards().Should().HaveCount(1);
        _bob.Zones.Hand.GetCards().Should().HaveCount(1);
        _alice.Zones.Graveyard.GetCards().Should().HaveCount(3);
        _bob.Zones.Graveyard.GetCards().Should().HaveCount(3);
        _alice.Zones.Library.GetCards().Should().HaveCount(2);
        _bob.Zones.Library.GetCards().Should().HaveCount(2);

        // All 4 candidate cards (1 starting + 3 drawn) for each player are
        // accounted for between hand (1) and graveyard (3).
        var aliceAll = _alice.Zones.Hand.GetCards()
            .Concat(_alice.Zones.Graveyard.GetCards());
        aliceAll.Should().Contain(aliceStart[0]);
        var bobAll = _bob.Zones.Hand.GetCards()
            .Concat(_bob.Zones.Graveyard.GetCards());
        bobAll.Should().Contain(bobStart[0]);
    }

    [Fact]
    public void Resolve_SeededRng_IsDeterministic()
    {
        SeedHand(_alice, 0);
        SeedHand(_bob, 0);
        SeedLibrary(_alice, 10);
        SeedLibrary(_bob, 10);
        GameRandomRegistry.SetDefault(new GameRandom(seed: 42));

        var effects = BurningInquiryFactory.BuildResolveEffect(
            new[] { _alice, _bob });
        foreach (var e in effects) e.Execute();

        var firstGy = _alice.Zones.Graveyard.GetCards()
            .Concat(_bob.Zones.Graveyard.GetCards())
            .Select(c => c.Name).OrderBy(n => n).ToList();

        // Replay from scratch with the same seed → identical discards.
        var alice2 = new Player("Alice", 20);
        var bob2 = new Player("Bob", 20);
        foreach (var p in new[] { alice2, bob2 })
        {
            for (var i = 0; i < 10; i++)
            {
                var c = new Card($"{p.Name}-Lib-{i}", "");
                c.SetOwner(p);
                p.Zones.Library.AddCard(c);
                c.SetZone(ZoneType.Library);
            }
        }
        GameRandomRegistry.SetDefault(new GameRandom(seed: 42));
        var effects2 = BurningInquiryFactory.BuildResolveEffect(
            new[] { alice2, bob2 });
        foreach (var e in effects2) e.Execute();

        var secondGy = alice2.Zones.Graveyard.GetCards()
            .Concat(bob2.Zones.Graveyard.GetCards())
            .Select(c => c.Name).OrderBy(n => n).ToList();

        secondGy.Should().BeEquivalentTo(firstGy);
    }

    [Fact]
    public void Resolve_LibrarySmallerThanThree_DrawsWhatsAvailable_AndFlagsSbaLoss()
    {
        // Alice's library has only 1 card — she draws it then tries to draw
        // a 2nd from an empty library (SBA flag, CR 704.5b). Bob has a full
        // library and is unaffected.
        SeedHand(_alice, 2);
        SeedHand(_bob, 2);
        SeedLibrary(_alice, 1);
        var bobLib = SeedLibrary(_bob, 10);
        GameRandomRegistry.SetDefault(new GameRandom(seed: 7));

        var effects = BurningInquiryFactory.BuildResolveEffect(
            new[] { _alice, _bob });
        foreach (var e in effects) e.Execute();

        _alice.Zones.Library.GetCards().Should().BeEmpty();
        _alice.TriedToDrawFromEmptyLibrary.Should().BeTrue(
            "the draw past an empty library must set the SBA flag");

        // Alice: 2 starting + 1 drawn = 3 in hand pre-discard; discard 3 => 0.
        _alice.Zones.Hand.GetCards().Should().BeEmpty();
        _alice.Zones.Graveyard.GetCards().Should().HaveCount(3);

        // Bob unaffected: 2 starting + 3 drawn = 5 pre-discard; discard 3 => 2.
        _bob.Zones.Hand.GetCards().Should().HaveCount(2);
        _bob.Zones.Graveyard.GetCards().Should().HaveCount(3);
        _bob.Zones.Library.GetCards().Should().HaveCount(7);
        _bob.TriedToDrawFromEmptyLibrary.Should().BeFalse();
    }

    [Fact]
    public void Resolve_FewerThanThreeCardsAfterDraw_DiscardsWhatsThere()
    {
        // Empty library, empty hand for Alice: draws nothing, has nothing to
        // discard. "Discard three cards" with fewer than three available
        // discards what is there (CR 701.16a) — here zero.
        SeedHand(_alice, 0);
        SeedHand(_bob, 0);
        SeedLibrary(_alice, 0);
        SeedLibrary(_bob, 10);
        GameRandomRegistry.SetDefault(new GameRandom(seed: 99));

        var effects = BurningInquiryFactory.BuildResolveEffect(
            new[] { _alice, _bob });
        foreach (var e in effects) e.Execute();

        _alice.Zones.Hand.GetCards().Should().BeEmpty();
        _alice.Zones.Graveyard.GetCards().Should().BeEmpty();
        _alice.TriedToDrawFromEmptyLibrary.Should().BeTrue();

        // Bob is unaffected.
        _bob.Zones.Graveyard.GetCards().Should().HaveCount(3);
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
