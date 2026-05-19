using Majik.Core.Cards;
using Majik.Core.Cards.Types;

namespace Majik.Core.Rules;

/// <summary>
/// CR 100.2 / 100.4 / 903 — deck-construction legality checks. Static
/// surface returns a <see cref="DeckValidationResult"/> with an IsValid
/// flag + human-readable errors. Card legality (banned/restricted lists)
/// is not enforced here; this is structure-only.
/// </summary>
public static class DeckValidator
{
    public const int ConstructedMinimum = 60;
    public const int ConstructedFourOfLimit = 4;
    public const int CommanderDeckSize = 100; // 99 + commander

    /// <summary>CR 100.2a / 100.4 — Constructed: ≥60 cards, ≤4 of any
    /// non-basic-land card.</summary>
    public static DeckValidationResult ValidateConstructed(IReadOnlyList<ICard> deck)
    {
        if (deck == null) throw new ArgumentNullException(nameof(deck));
        var errors = new List<string>();
        if (deck.Count < ConstructedMinimum)
            errors.Add($"Deck has {deck.Count}; minimum {ConstructedMinimum}.");

        foreach (var grp in deck.GroupBy(c => c.Name))
        {
            var rep = grp.First();
            if (IsBasicLand(rep)) continue;
            if (grp.Count() > ConstructedFourOfLimit)
                errors.Add($"{rep.Name}: {grp.Count()} copies; max {ConstructedFourOfLimit}.");
        }
        return new DeckValidationResult(errors.Count == 0, errors);
    }

    /// <summary>CR 903 — Commander: commander must be legendary, deck must
    /// total exactly 100 cards (commander + 99), and each non-basic-land
    /// card may appear at most once (singleton).</summary>
    public static DeckValidationResult ValidateCommander(ICard commander, IReadOnlyList<ICard> deck)
    {
        if (commander == null) throw new ArgumentNullException(nameof(commander));
        if (deck == null) throw new ArgumentNullException(nameof(deck));
        var errors = new List<string>();

        if (!commander.Supertypes.Contains(CardSupertype.Legendary))
            errors.Add($"Commander {commander.Name} must be legendary.");

        if (deck.Count + 1 != CommanderDeckSize)
            errors.Add($"Commander deck has {deck.Count + 1}; required {CommanderDeckSize}.");

        foreach (var grp in deck.GroupBy(c => c.Name))
        {
            var rep = grp.First();
            if (IsBasicLand(rep)) continue;
            if (grp.Count() > 1)
                errors.Add($"{rep.Name}: {grp.Count()} copies; singleton rule.");
        }
        return new DeckValidationResult(errors.Count == 0, errors);
    }

    private static bool IsBasicLand(ICard card) =>
        card.Supertypes.Contains(CardSupertype.Basic)
        && card.CardTypes.Contains(CardType.Land);
}

public sealed record DeckValidationResult(bool IsValid, IReadOnlyList<string> Errors);
