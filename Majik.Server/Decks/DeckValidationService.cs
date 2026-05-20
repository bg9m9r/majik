using Majik.Core.CardData;

namespace Majik.Server.Decks;

/// <summary>Server-authoritative deck legality check for Constructed.
/// Aggregates every violation in a single <see cref="DeckValidationResult"/>
/// so callers can surface them all to the UI at once.</summary>
public sealed class DeckValidationService
{
    private static readonly string[] LegalTypes =
    {
        "Instant", "Sorcery", "Creature", "Artifact",
        "Enchantment", "Planeswalker", "Land",
    };

    private const int MainboardMinimum = 60;
    private const int SideboardMaximum = 15;
    private const int FourOfLimit = 4;
    private const string BasicLandMarker = "Basic Land";

    private readonly ICardRepository _cards;

    public DeckValidationService(ICardRepository cards) { _cards = cards; }

    public DeckValidationResult Validate(Deck deck)
    {
        var errors = new List<string>();

        var mainCount = deck.Mainboard.Sum(e => e.Count);
        if (mainCount < MainboardMinimum)
        {
            errors.Add($"main deck has {mainCount} cards; minimum {MainboardMinimum}");
        }

        var sideCount = deck.Sideboard.Sum(e => e.Count);
        if (sideCount > SideboardMaximum)
        {
            errors.Add($"sideboard has {sideCount} cards; maximum {SideboardMaximum}");
        }

        CheckDuplicates(deck.Mainboard, "mainboard", errors);
        CheckDuplicates(deck.Sideboard, "sideboard", errors);

        foreach (var e in deck.Mainboard.Concat(deck.Sideboard))
        {
            if (e.Count < 1)
            {
                errors.Add($"{e.Name}: count must be at least 1");
            }
        }

        var allEntries = deck.Mainboard.Concat(deck.Sideboard);
        var totalsByName = allEntries
            .GroupBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Sum(e => e.Count));

        foreach (var (name, totalCount) in totalsByName)
        {
            var entity = _cards.GetByName(name);
            if (entity == null)
            {
                errors.Add($"unknown card: {name}");
                continue;
            }

            if (!_cards.IsImplemented(name))
            {
                errors.Add($"not implemented: {name}");
            }

            var typeLine = entity.TypeLine ?? "";

            var isToken = typeLine.Contains("Token", StringComparison.OrdinalIgnoreCase);
            var hasLegalType = !isToken && LegalTypes.Any(t => typeLine.Contains(t, StringComparison.OrdinalIgnoreCase));
            if (!hasLegalType)
            {
                errors.Add($"{name}: type not legal in Constructed");
            }

            var isBasicLand = typeLine.Contains(BasicLandMarker, StringComparison.OrdinalIgnoreCase);
            if (!isBasicLand && totalCount > FourOfLimit)
            {
                errors.Add($"{name}: {totalCount} copies combined main+side (max {FourOfLimit})");
            }
        }

        return new DeckValidationResult(errors.Count == 0, errors);
    }

    private static void CheckDuplicates(IReadOnlyList<DeckCardEntry> zone, string zoneName, List<string> errors)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var e in zone)
        {
            if (!seen.Add(e.Name))
            {
                errors.Add($"{e.Name}: duplicate entry in {zoneName}");
            }
        }
    }
}

public sealed record DeckValidationResult(bool IsValid, IReadOnlyList<string> Errors);
