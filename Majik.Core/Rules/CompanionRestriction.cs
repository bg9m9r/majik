using Majik.Core.Cards;

namespace Majik.Core.Rules;

/// <summary>
/// CR 702.139 — Companion. A companion is a card outside your starting deck
/// (tracked alongside it, conventionally in the sideboard slot). Once per
/// game its controller may cast it from outside the game by paying {3} more
/// to put it into their hand first, provided their starting deck satisfies
/// the companion's deck-construction restriction.
///
/// This interface models <i>only the deck-construction half</i> of the
/// rule — the predicate that decides whether a given starting deck makes
/// the companion legal. The "cast from outside the game" runtime path is
/// intentionally not modelled here; the engine has no sideboard zone yet
/// (see <see cref="Majik.Core.Zones.ZoneType"/>), so the cast pipeline
/// has no source to draw from. Shipping the predicate now lets each
/// companion factory declare its rule today; the runtime half can be
/// layered on once the sideboard surface lands.
///
/// Each of the eight Ikoria companions (Lurrus, Yorion, Gyruda, Jegantha,
/// Kaheera, Keruga, Lutri, Obosh, Umori, Zirda) implements this with its
/// own predicate over the starting deck.
/// </summary>
public interface ICompanionRestriction
{
    /// <summary>
    /// Human-readable description of the restriction, suitable for surfacing
    /// in deck-builder UI / validation errors. Matches the companion's
    /// printed "Companion — …" reminder text.
    /// </summary>
    string Description { get; }

    /// <summary>
    /// True iff the starting deck (excluding the companion itself) satisfies
    /// this companion's deck-construction predicate per CR 702.139a.
    /// </summary>
    /// <param name="startingDeck">
    /// The cards in the player's starting (main) deck. Excludes the
    /// companion. Implementations must treat the sequence as read-only.
    /// </param>
    bool IsSatisfiedBy(IEnumerable<ICard> startingDeck);
}
