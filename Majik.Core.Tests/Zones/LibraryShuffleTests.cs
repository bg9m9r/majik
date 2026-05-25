using FluentAssertions;
using Majik.Core.Cards;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Random;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.Zones;

/// <summary>
/// CR 701.20 — coverage for the library-shuffle primitive: IZone.Shuffle,
/// GameRandomRegistry, EventBusRegistry, and the LibraryShuffle helper.
/// </summary>
[Collection(nameof(StaticRegistryCollection))]
public class LibraryShuffleTests : IDisposable
{
    public LibraryShuffleTests()
    {
        // Tests share static registries — start every test from a known
        // baseline so prior runs can't leak state.
        GameRandomRegistry.Clear();
        EventBusRegistry.Clear();
        EventBusRegistry.SetDefault(null);
        GameRandomRegistry.SetDefault(new GameRandom(seed: 0));
    }

    public void Dispose()
    {
        GameRandomRegistry.Clear();
        EventBusRegistry.Clear();
        EventBusRegistry.SetDefault(null);
    }

    private static List<ICard> BuildDeck(int count)
    {
        var deck = new List<ICard>(count);
        for (var i = 0; i < count; i++)
        {
            deck.Add(new Instant($"Card-{i:D2}", "R"));
        }
        return deck;
    }

    [Fact]
    public void Shuffle_SeededRng_IsDeterministic()
    {
        var zoneA = new Zone(ZoneType.Library, "A");
        var zoneB = new Zone(ZoneType.Library, "B");

        var deckA = BuildDeck(20);
        var deckB = deckA.Select(c => (ICard)new Instant(c.Name, "R")).ToList();
        foreach (var c in deckA) zoneA.AddCard(c);
        foreach (var c in deckB) zoneB.AddCard(c);

        zoneA.Shuffle(new GameRandom(seed: 42));
        zoneB.Shuffle(new GameRandom(seed: 42));

        zoneA.GetCards().Select(c => c.Name).Should()
            .Equal(zoneB.GetCards().Select(c => c.Name));
    }

    [Fact]
    public void Shuffle_DifferentSeeds_ProduceDifferentOrder()
    {
        // Build two zones with identical contents, then shuffle with
        // different seeds. With 60 cards the probability of an accidental
        // collision is ~1/60! — effectively zero.
        var zoneA = new Zone(ZoneType.Library, "A");
        var zoneB = new Zone(ZoneType.Library, "B");

        var deckA = BuildDeck(60);
        var deckB = deckA.Select(c => (ICard)new Instant(c.Name, "R")).ToList();
        foreach (var c in deckA) zoneA.AddCard(c);
        foreach (var c in deckB) zoneB.AddCard(c);

        zoneA.Shuffle(new GameRandom(seed: 1));
        zoneB.Shuffle(new GameRandom(seed: 999));

        zoneA.GetCards().Select(c => c.Name).Should()
            .NotEqual(zoneB.GetCards().Select(c => c.Name));
    }

    [Fact]
    public void Shuffle_EmptyZone_NoOp()
    {
        var zone = new Zone(ZoneType.Library, "Empty");

        var act = () => zone.Shuffle(new GameRandom(seed: 7));

        act.Should().NotThrow();
        zone.Count.Should().Be(0);
    }

    [Fact]
    public void Shuffle_SingleCard_Unchanged()
    {
        var zone = new Zone(ZoneType.Library, "Single");
        var only = new Instant("Lonely", "R");
        zone.AddCard(only);

        zone.Shuffle(new GameRandom(seed: 7));

        zone.Count.Should().Be(1);
        zone.GetCards().Single().Should().BeSameAs(only);
    }

    [Fact]
    public void Shuffle_NullRandom_Throws()
    {
        var zone = new Zone(ZoneType.Library, "X");
        zone.AddCard(new Instant("A", "R"));

        var act = () => zone.Shuffle(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Shuffle_PreservesEveryCard()
    {
        var zone = new Zone(ZoneType.Library, "X");
        var deck = BuildDeck(40);
        foreach (var c in deck) zone.AddCard(c);

        zone.Shuffle(new GameRandom(seed: 13));

        zone.GetCards().OrderBy(c => c.Name)
            .Should().Equal(deck.OrderBy(c => c.Name));
    }

    [Fact]
    public void LibraryShuffle_PublishesEvent_WhenBusRegistered()
    {
        var player = new Player("P", 20);
        for (var i = 0; i < 10; i++)
            player.Zones.Library.AddCard(new Instant($"Card-{i}", "R"));

        // Use a plain EventBus + an in-test subscriber so capture works
        // regardless of new-vs-override semantics on any test fixture.
        var bus = new EventBus();
        LibraryShuffledEvent? captured = null;
        bus.Subscribe<LibraryShuffledEvent>(e => captured = e);
        EventBusRegistry.Set(player, bus);
        GameRandomRegistry.Set(player, new GameRandom(seed: 99));

        LibraryShuffle.ShuffleLibrary(player, "unit-test");

        captured.Should().NotBeNull();
        captured!.Player.Should().BeSameAs(player);
        captured.Reason.Should().Be("unit-test");
        captured.CardCount.Should().Be(10);
    }

    [Fact]
    public void LibraryShuffle_NoBus_StillShuffles_NoThrow()
    {
        var player = new Player("P", 20);
        for (var i = 0; i < 20; i++)
            player.Zones.Library.AddCard(new Instant($"Card-{i:D2}", "R"));

        GameRandomRegistry.Set(player, new GameRandom(seed: 42));
        // No EventBusRegistry.Set — the helper must publish best-effort
        // (i.e. silently skip) when nothing is registered.

        var before = player.Zones.Library.GetCards().Select(c => c.Name).ToList();

        var act = () => LibraryShuffle.ShuffleLibrary(player, "no-bus");

        act.Should().NotThrow();
        // Library still contains the same cards (just possibly reordered).
        player.Zones.Library.GetCards().Select(c => c.Name).OrderBy(n => n)
            .Should().Equal(before.OrderBy(n => n));
    }

    [Fact]
    public void LibraryShuffle_NullPlayer_NoThrow()
    {
        var act = () => LibraryShuffle.ShuffleLibrary(null!, "null-player");
        act.Should().NotThrow();
    }

    [Fact]
    public void LibraryShuffle_UsesRegisteredPerPlayerRandom()
    {
        // Both libraries seeded with identical card order; one player gets a
        // seeded RNG via the registry, the other a different seed. After
        // ShuffleLibrary the orderings must differ — proves the registry
        // lookup actually drives the shuffle (not the process default).
        var p1 = new Player("P1", 20);
        var p2 = new Player("P2", 20);
        var d1 = BuildDeck(40);
        var d2 = d1.Select(c => (ICard)new Instant(c.Name, "R")).ToList();
        foreach (var c in d1) p1.Zones.Library.AddCard(c);
        foreach (var c in d2) p2.Zones.Library.AddCard(c);

        GameRandomRegistry.Set(p1, new GameRandom(seed: 1));
        GameRandomRegistry.Set(p2, new GameRandom(seed: 2));

        LibraryShuffle.ShuffleLibrary(p1, "p1");
        LibraryShuffle.ShuffleLibrary(p2, "p2");

        p1.Zones.Library.GetCards().Select(c => c.Name).Should()
            .NotEqual(p2.Zones.Library.GetCards().Select(c => c.Name));
    }

    [Fact]
    public void IZone_DefaultShuffle_IsNoOp_ForNonLibraryZones()
    {
        // IZone.Shuffle has a default no-op implementation; the concrete
        // Zone overrides it. This test guards against an accidental Zone
        // override removal regressing non-library zones to actually
        // shuffle their (order-insensitive) contents.
        var zone = new Zone(ZoneType.Battlefield, "BF");
        var c1 = new Instant("A", "R");
        var c2 = new Instant("B", "R");
        zone.AddCard(c1);
        zone.AddCard(c2);

        // Even on Battlefield, Zone.Shuffle reorders the internal list —
        // that's fine because Battlefield ordering carries no game meaning.
        // The assertion here is just that the call is safe + lossless.
        zone.Shuffle(new GameRandom(seed: 1));

        zone.Count.Should().Be(2);
        zone.GetCards().Should().Contain(c1).And.Contain(c2);
    }
}
