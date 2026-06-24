using Majik.Core.Cards.Types;

namespace Majik.Core.CardData;

/// <summary>
/// Parses a Scryfall TypeLine string like "Legendary Creature — Human Wizard"
/// into our typed enums. Tokens left of the dash are types/supertypes; right
/// of the dash are subtypes. Unknown subtypes are dropped (no enum value).
///
/// Both the em-dash (U+2014) and the ASCII "-" are accepted as separators —
/// the importer sometimes normalizes one way, sometimes the other.
///
/// <para>
/// MTG has a handful of MULTI-WORD creature subtypes (CR 205.3m) whose words
/// individually collide with single-word subtypes — e.g. printed "Badger Mole"
/// (Badgermole Cub) is ONE creature type, not "Badger" + "Mole", and must not
/// decompose into the standalone <see cref="CardSubtype.Badger"/> token that a
/// genuine "Badger Warrior" (Hugs, Grisly Guardian) carries. Such phrases are
/// consumed as a unit by <see cref="MultiWordSubtypes"/> before per-token
/// matching; none currently has an enum value, so (as before) they drop out of
/// the parsed subtype set rather than mis-resolving to a constituent word.
/// </para>
/// </summary>
public static class TypeLineParser
{
    public sealed record ParsedTypeLine(
        IReadOnlyList<CardType> Types,
        IReadOnlyList<CardSupertype> Supertypes,
        IReadOnlyList<CardSubtype> Subtypes);

    /// <summary>
    /// Multi-word printed creature subtypes (CR 205.3m) that must be matched as
    /// a single unit so their constituent words don't mis-resolve to unrelated
    /// single-word subtype enum values. Each phrase is a space-joined,
    /// case-insensitive key; the value is the <see cref="CardSubtype"/> it maps
    /// to, or <c>null</c> when the engine has no enum value for that type (it is
    /// then dropped, matching the "unknown subtypes are dropped" contract).
    /// </summary>
    private static readonly IReadOnlyDictionary<string, CardSubtype?> MultiWordSubtypes =
        new Dictionary<string, CardSubtype?>(StringComparer.OrdinalIgnoreCase)
        {
            // Bloomburrow "Badger Mole" (Badgermole Cub) — distinct from the
            // standalone "Badger" type (Hugs, Grisly Guardian). No enum value.
            ["Badger Mole"] = null,
        };

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
        for (var i = 0; i < rightTokens.Length; i++)
        {
            // Greedily match a known multi-word subtype (CR 205.3m) before
            // falling back to single-token matching, so e.g. "Badger Mole" is
            // consumed as one unit and never decomposes into "Badger".
            if (i + 1 < rightTokens.Length)
            {
                var pair = rightTokens[i] + " " + rightTokens[i + 1];
                if (MultiWordSubtypes.TryGetValue(pair, out var mapped))
                {
                    if (mapped is { } known) subtypes.Add(known);
                    i++; // consumed both tokens
                    continue;
                }
            }

            if (Enum.TryParse<CardSubtype>(rightTokens[i], ignoreCase: true, out var sub))
            {
                subtypes.Add(sub);
            }
        }

        return new ParsedTypeLine(types, supertypes, subtypes);
    }
}
