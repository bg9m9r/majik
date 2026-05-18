using Majik.Core.Cards.Types;

namespace Majik.Core.CardData;

/// <summary>
/// Parses a Scryfall TypeLine string like "Legendary Creature — Human Wizard"
/// into our typed enums. Tokens left of the dash are types/supertypes; right
/// of the dash are subtypes. Unknown subtypes are dropped (no enum value).
///
/// Both the em-dash (U+2014) and the ASCII "-" are accepted as separators —
/// the importer sometimes normalizes one way, sometimes the other.
/// </summary>
public static class TypeLineParser
{
    public sealed record ParsedTypeLine(
        IReadOnlyList<CardType> Types,
        IReadOnlyList<CardSupertype> Supertypes,
        IReadOnlyList<CardSubtype> Subtypes);

    public static ParsedTypeLine Parse(string typeLine)
    {
        if (string.IsNullOrWhiteSpace(typeLine))
        {
            return new ParsedTypeLine(
                Array.Empty<CardType>(),
                Array.Empty<CardSupertype>(),
                Array.Empty<CardSubtype>());
        }

        var split = typeLine.Split(new[] { " — ", " - " }, 2, StringSplitOptions.None);
        var leftTokens = split[0].Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var rightTokens = split.Length > 1
            ? split[1].Split(' ', StringSplitOptions.RemoveEmptyEntries)
            : Array.Empty<string>();

        var types = new List<CardType>();
        var supertypes = new List<CardSupertype>();
        foreach (var token in leftTokens)
        {
            if (Enum.TryParse<CardSupertype>(token, ignoreCase: true, out var st))
            {
                supertypes.Add(st);
            }
            else if (Enum.TryParse<CardType>(token, ignoreCase: true, out var ct))
            {
                types.Add(ct);
            }
        }

        var subtypes = new List<CardSubtype>();
        foreach (var token in rightTokens)
        {
            if (Enum.TryParse<CardSubtype>(token, ignoreCase: true, out var sub))
            {
                subtypes.Add(sub);
            }
        }

        return new ParsedTypeLine(types, supertypes, subtypes);
    }
}
