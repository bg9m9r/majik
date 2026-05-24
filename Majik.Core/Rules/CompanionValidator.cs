using Majik.Core.Cards;

namespace Majik.Core.Rules;

/// <summary>
/// CR 702.139 — Companion validation surface. Pure-function entrypoint
/// that callers (deck registration, future game-init, deck-builder UI)
/// invoke to decide whether a companion may legally accompany a given
/// starting deck.
///
/// Pair-wise with <see cref="DeckValidator"/>: this checks the companion
/// half of deck legality, <see cref="DeckValidator"/> checks the main-deck
/// half. Both return a <see cref="DeckValidationResult"/> so call sites
/// can fan errors into a single bag.
/// </summary>
public static class CompanionValidator
{
    /// <summary>
    /// Validate the companion against the starting deck. Returns
    /// <see cref="DeckValidationResult.IsValid"/>=true with an empty error
    /// list iff the companion's
    /// <see cref="ICompanionRestriction.IsSatisfiedBy"/> returns true.
    /// </summary>
    /// <param name="companion">
    /// The card nominated as companion. May be any card; whether it's
    /// actually a Companion-keyword card is the factory layer's job to
    /// surface via <paramref name="restriction"/>.
    /// </param>
    /// <param name="restriction">
    /// The companion's deck-construction predicate, sourced from its
    /// factory (e.g. <c>LurrusOfTheDreamDenFactory.CompanionRestriction</c>).
    /// </param>
    /// <param name="startingDeck">
    /// The main starting deck, excluding the companion.
    /// </param>
    public static DeckValidationResult Validate(
        ICard companion,
        ICompanionRestriction restriction,
        IReadOnlyList<ICard> startingDeck)
    {
        ArgumentNullException.ThrowIfNull(companion);
        ArgumentNullException.ThrowIfNull(restriction);
        ArgumentNullException.ThrowIfNull(startingDeck);

        if (restriction.IsSatisfiedBy(startingDeck))
        {
            return new DeckValidationResult(true, Array.Empty<string>());
        }

        return new DeckValidationResult(
            false,
            new[]
            {
                $"Companion {companion.Name} rejected: "
                + $"starting deck does not satisfy \"{restriction.Description}\"."
            });
    }
}
