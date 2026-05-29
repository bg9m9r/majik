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
/// Tests for Goblin Lore (Tempest, {1}{R}, Sorcery).
///
/// Oracle text:
///   "Draw four cards, then discard three cards at random."
///
/// Covers:
///   - Card identity (Sorcery, {1}{R}, MV 2, owner/controller).
///   - NamedCardFactory dispatch.
///   - Resolve: caster draws four, then discards three of the resulting
///     hand at random (CR 121.1 draw, CR 701.16/CR 701.16e random discard).
///   - Net hand-size change is +1 (draw 4, discard 3) when the library has
///     at least four cards.
///   - Random pick is deterministic when the per-game RNG is seeded.
///   - Library smaller than four: draws what's available, flags the
///     try-to-draw-from-empty-library SBA loss (CR 704.5b), then discards
///     three at random from whatever ended up in hand.
/// </summary>
public class GoblinLoreTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Card identity / dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void GoblinLore_IsSorcery_At1R()
    {
        var card = GoblinLoreFactory.Create(_alice);

        card.Name.Should().Be("Goblin Lore");
        card.ManaCost.Should().Be("{1}{R}");
        card.HasType(CardType.Sorcery).Should().BeTrue();
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_GoblinLore()
    {
        var card = NamedCardFactory.Create("Goblin Lore", _alice);

        card.Should().BeOfType<Sorcery>();
        card.Name.Should().Be("Goblin Lore");
        card.HasType(CardType.Sorcery).Should().BeTrue();
        card.ManaCost.Should().Be("{1}{R}");
        card.Owner.Should().BeSameAs(_alice);
    }

    // -----------------------------------------------------------------------
    // Resolution — draw 4, then discard 3 at random
    // -----------------------------------------------------------------------

    [Fact]
    public void Resolve_DrawsFour_ThenDiscardsThreeAtRandom_NetPlusOne()
    {
        // Alice starts with 0 cards in hand and 10 in library. After
        // resolving she has drawn 4 and discarded 3 at random — net +1.
        SeedHand(_alice, 0);
        var lib = SeedLibrary(_alice, 10);
        GameRandomRegistry.SetDefault(new GameRandom(seed: 1234));

        var effects = GoblinLoreFactory.BuildResolveEffect(_alice);
        foreach (var e in effects) e.Execute();

        // Drew 4 off the top, library drained by 4.
        _alice.Zones.Library.GetCards().Should().HaveCount(6);
        // Drew 4, discarded 3 at random => 1 card left in hand.
        _alice.Zones.Hand.GetCards().Should().HaveCount(1);
        // Exactly three cards went to the graveyard.
        _alice.Zones.Graveyard.GetCards().Should().HaveCount(3);

        // The four drawn cards are accounted for: 1 in hand + 3 in graveyard.
        var top4 = lib.Take(4).ToList();
        var handPlusGy = _alice.Zones.Hand.GetCards()
            .Concat(_alice.Zones.Graveyard.GetCards());
        handPlusGy.Should().BeEquivalentTo(top4);

        _alice.TriedToDrawFromEmptyLibrary.Should().BeFalse();
    }

    [Fact]
    public void Resolve_SeededRng_IsDeterministic()
    {
        SeedHand(_alice, 0);
        SeedLibrary(_alice, 10);
        GameRandomRegistry.SetDefault(new GameRandom(seed: 42));

        var effects = GoblinLoreFactory.BuildResolveEffect(_alice);
        foreach (var e in effects) e.Execute();

        var firstGraveyard = _alice.Zones.Graveyard.GetCards()
            .Select(c => c.Name).OrderBy(n => n).ToList();

        // Replay from scratch with the same seed → identical discards.
        var alice2 = new Player("Alice", 20);
        for (var i = 0; i < 10; i++)
        {
            var c = new Card($"Alice-Lib-{i}", "");
            c.SetOwner(alice2);
            alice2.Zones.Library.AddCard(c);
            c.SetZone(ZoneType.Library);
        }
        GameRandomRegistry.SetDefault(new GameRandom(seed: 42));
        var effects2 = GoblinLoreFactory.BuildResolveEffect(alice2);
        foreach (var e in effects2) e.Execute();

        var secondGraveyard = alice2.Zones.Graveyard.GetCards()
            .Select(c => c.Name).OrderBy(n => n).ToList();

        secondGraveyard.Should().BeEquivalentTo(firstGraveyard);
    }

    [Fact]
    public void Resolve_LibrarySmallerThanFour_DrawsWhatsAvailable_AndFlagsSbaLoss()
    {
        // Library has only 2 cards. Alice starts with 2 cards in hand.
        // She draws both library cards then tries to draw a 3rd from an
        // empty library (SBA flag, CR 704.5b). Hand then holds the 2
        // original + 2 drawn = 4 cards; discard 3 at random => 1 left.
        SeedHand(_alice, 2);
        SeedLibrary(_alice, 2);
        GameRandomRegistry.SetDefault(new GameRandom(seed: 7));

        var effects = GoblinLoreFactory.BuildResolveEffect(_alice);
        foreach (var e in effects) e.Execute();

        _alice.Zones.Library.GetCards().Should().BeEmpty();
        _alice.TriedToDrawFromEmptyLibrary.Should().BeTrue(
            "the draw past an empty library must set the SBA flag");

        // 2 starting + 2 drawn = 4 in hand before discard; discard 3 => 1.
        _alice.Zones.Hand.GetCards().Should().HaveCount(1);
        _alice.Zones.Graveyard.GetCards().Should().HaveCount(3);
    }

    [Fact]
    public void Resolve_FewerThanThreeCardsAfterDraw_DiscardsWhatsThere()
    {
        // Empty library, empty hand: draws nothing, has nothing to discard.
        // "Discard three cards" with fewer than three available discards
        // what is there (CR 701.16a) — here zero.
        SeedHand(_alice, 0);
        SeedLibrary(_alice, 0);
        GameRandomRegistry.SetDefault(new GameRandom(seed: 99));

        var effects = GoblinLoreFactory.BuildResolveEffect(_alice);
        foreach (var e in effects) e.Execute();

        _alice.Zones.Hand.GetCards().Should().BeEmpty();
        _alice.Zones.Graveyard.GetCards().Should().BeEmpty();
        _alice.TriedToDrawFromEmptyLibrary.Should().BeTrue();
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
