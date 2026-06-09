using Majik.Core.CardData;
using Majik.Core.Cards;
using Majik.Core.Players;
using Majik.Core.Random;
using Majik.Core.Zones;

namespace Majik.Bot.Search;

/// <summary>
/// Replaces a game's HIDDEN zones (opponent hand + both libraries) with a plausible
/// arrangement sampled deterministically from <c>worldSeed</c>, decklist-aware. Pure
/// function of (unknown multiset, worldSeed): same seed -> same world. The searched
/// seat's own HAND is KNOWN -> untouched; its library ORDER is hidden -> reshuffled.
/// Operates on the players passed in (intended: the CLONED players inside a sandbox),
/// never live game state.
///
/// <para><b>Card-build path.</b> Each sampled opponent card is built into a live,
/// castable instance via <see cref="ScryfallCardFactory"/> — the same canonical
/// shell+bind path the prod match loader uses. <c>GameFacade.BuildDeckCard</c> takes
/// a shell built from a <see cref="CardEntity"/> and runs the binder chain on it;
/// <see cref="ScryfallCardFactory.Create(string, Player)"/> does both steps in one
/// call (build the typed shell from <c>GetByName</c>, set the owner, then run the
/// SAME binder chain — keyword / mana / saga / triggered / ETB-replacement binders).
/// It is NOT the test-only <see cref="NamedCardFactory"/>: that one bypasses the prod
/// binder chain. We use the single-argument factory overload (no live
/// <see cref="Majik.Core.Effects.ContinuousEffectsService"/> / TriggerManager /
/// ZoneService threaded in), because a standalone resampler has no live per-game
/// services. That is the documented tradeoff: sampled cards carry their printed
/// characteristics + statically-bound abilities (the only thing the search needs to
/// reason about an unknown card it might draw), but continuous/replacement effects
/// from sampled cards are not wired to a live service until the card is actually drawn
/// and played inside the sandbox. The factory can be constructed with those services
/// later (see <see cref="ScryfallCardFactory"/> ctor) if a plumbing task wants
/// fully-live sampled cards; pass a pre-built factory via <see cref="Resample"/>'s
/// optional <c>factory</c> parameter.</para>
///
/// <para><b>2-player assumption.</b> Exactly one non-self seat is treated as the
/// opponent. With &gt;2 players the FIRST non-self seat is taken as the opponent and
/// the rest are left untouched — revisit when multiplayer search lands.</para>
/// </summary>
internal static class DeterminizationSampler
{
    // Lazily-constructed shared repo + factory for the default (no-injection) path.
    // EmbeddedCardRepository loads its 22k-row seed lazily on first GetByName, so
    // constructing this is cheap; one shared instance avoids re-reading the gz per call.
    private static readonly Lazy<ScryfallCardFactory> DefaultFactory =
        new(() => new ScryfallCardFactory(new EmbeddedCardRepository()));

    /// <summary>
    /// Resample the hidden zones in place. See the type-level docs for semantics.
    /// </summary>
    /// <param name="players">The players to operate on — intended to be the CLONED
    /// players inside a search sandbox, never live game state.</param>
    /// <param name="searchedSeatId">The <see cref="Player.Id"/> of the searched seat
    /// (its hand is known; only its library order is reshuffled).</param>
    /// <param name="opponentDecklist">The opponent's full decklist as name strings
    /// (name -&gt; count). The unknown multiset is this list minus the opponent's
    /// VISIBLE cards (battlefield + graveyard + exile) by name.</param>
    /// <param name="worldSeed">The deterministic world seed. Identical
    /// (players-state, worldSeed) -&gt; identical result.</param>
    /// <param name="factory">Optional card-build factory. Defaults to a shared
    /// <see cref="ScryfallCardFactory"/> over the embedded repo. Inject a factory
    /// wired with live services if you need fully-live sampled cards.</param>
    public static void Resample(
        IReadOnlyList<Player> players,
        Guid searchedSeatId,
        IReadOnlyList<string> opponentDecklist,
        int worldSeed,
        ScryfallCardFactory? factory = null)
    {
        ArgumentNullException.ThrowIfNull(players);
        ArgumentNullException.ThrowIfNull(opponentDecklist);

        var self = players.FirstOrDefault(p => p.Id == searchedSeatId)
            ?? throw new ArgumentException(
                $"No player with Id {searchedSeatId} in the supplied players.",
                nameof(searchedSeatId));
        var opp = players.FirstOrDefault(p => p.Id != searchedSeatId);
        if (opp == null) return; // solo seat — nothing hidden to resample.

        var build = factory ?? DefaultFactory.Value;

        // One seeded RNG drives BOTH the opponent shuffle and the self-library
        // reshuffle, so the whole operation is a pure function of worldSeed.
        var rng = new GameRandom(worldSeed);

        ResampleOpponentHidden(opp, opponentDecklist, build, rng);
        ReshuffleSelfLibrary(self, rng);
    }

    /// <summary>
    /// Rebuild the opponent's hidden zones (hand + library) from the decklist minus
    /// the opponent's visible cards, dealing the recorded hand size off the top of a
    /// seeded shuffle and the rest into the library.
    /// </summary>
    private static void ResampleOpponentHidden(
        Player opp,
        IReadOnlyList<string> opponentDecklist,
        ScryfallCardFactory factory,
        GameRandom rng)
    {
        // Unknown multiset = decklist counts MINUS the opponent's VISIBLE cards by
        // name (battlefield + graveyard + exile). Revealed-but-in-hidden-zone cards
        // are out of scope (the engine does not surface a per-card "revealed" flag
        // here), so we count only the public zones. Counts clamp at >= 0.
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var name in opponentDecklist)
            counts[name] = counts.GetValueOrDefault(name) + 1;

        foreach (var visible in VisibleCardNames(opp))
        {
            if (counts.TryGetValue(visible, out var c) && c > 0)
                counts[visible] = c - 1;
        }

        // Expand to a flat name list, then shuffle with the seeded rng.
        var pool = new List<string>();
        foreach (var (name, count) in counts)
            for (var i = 0; i < count; i++)
                pool.Add(name);
        rng.Shuffle(pool);

        // Record how many cards the opponent is holding, then clear the hidden zones.
        var handSize = opp.Zones.Hand.GetCards().Count();
        opp.Zones.Hand.Clear();
        opp.Zones.GetZone(ZoneType.Library).Clear();

        // Deal the first handSize names to hand (clamped to what we have), the rest
        // to the library. If the unknown multiset is smaller than handSize (decklist
        // under-supplied vs. what the opponent supposedly holds), we deal what we can.
        var dealtToHand = Math.Min(handSize, pool.Count);
        for (var i = 0; i < pool.Count; i++)
        {
            var live = factory.Create(pool[i], opp);
            var zone = i < dealtToHand ? ZoneType.Hand : ZoneType.Library;
            opp.Zones.GetZone(zone).AddCard(live);
        }
    }

    /// <summary>
    /// Reshuffle the searched seat's library in place using the same seeded rng.
    /// The hand is untouched; only library ORDER is hidden. Reads the cards, shuffles
    /// the name-bearing instances, clears, and re-adds — preserving the exact card
    /// instances (so identity is kept), just re-ordered.
    /// </summary>
    private static void ReshuffleSelfLibrary(Player self, GameRandom rng)
    {
        var lib = self.Zones.GetZone(ZoneType.Library);
        var cards = lib.GetCards().ToList();
        if (cards.Count < 2) return;
        rng.Shuffle(cards);
        lib.Clear();
        foreach (var c in cards)
            lib.AddCard(c);
    }

    /// <summary>
    /// The opponent's publicly-visible card names: battlefield + graveyard + exile.
    /// These are subtracted from the decklist to form the unknown multiset so a
    /// known on-board permanent is never double-counted into a hidden zone.
    /// </summary>
    private static IEnumerable<string> VisibleCardNames(Player opp)
        => opp.Zones.Battlefield.GetCards()
            .Concat(opp.Zones.Graveyard.GetCards())
            .Concat(opp.Zones.GetZone(ZoneType.Exile).GetCards())
            .Select(c => c.Name);
}
