using System.Text.RegularExpressions;

namespace Majik.Core.CardData.Coverage;

/// <summary>
/// Parses plain-text decklists in the user-pasted "<c>4 Lightning Bolt</c>"
/// shape. Liberal about formatting variations:
/// <list type="bullet">
///   <item>Lines like <c>4 Lightning Bolt</c>, <c>4x Lightning Bolt</c>,
///   <c>  4 Lightning Bolt  </c>.</item>
///   <item>Optional set-printing tail: <c>4 Lightning Bolt (LEA) 161</c>.
///   The tail is stripped before name match.</item>
///   <item>Section headers like <c>Sideboard</c> / <c>Deck</c> /
///   <c>// Sideboard</c> are ignored; cards from all sections are
///   aggregated.</item>
///   <item>Blank lines and lines starting with <c>#</c> or <c>//</c> are
///   ignored.</item>
/// </list>
///
/// Coverage cares about copy counts, so the parser returns a flat
/// name → total-count map (sideboard rolled in).
/// </summary>
public static class DecklistParser
{
    private static readonly Regex LineRx = new(
        @"^\s*(?<count>\d+)\s*[xX]?\s+(?<name>.+?)\s*$",
        RegexOptions.Compiled);

    // Strip a trailing set/collector tail: " (SET) 123" or " [SET] 123".
    private static readonly Regex TailRx = new(
        @"\s*[\(\[][A-Za-z0-9]{2,6}[\)\]](\s+\d+\w?)?\s*$",
        RegexOptions.Compiled);

    /// <summary>
    /// Parse the raw decklist text into a name → total-copies map.
    /// Multiple entries for the same card name (e.g. one in mainboard and
    /// one in sideboard) are summed.
    /// </summary>
    public static IReadOnlyDictionary<string, int> Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var totals = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0) continue;
            if (line.StartsWith('#')) continue;
            if (line.StartsWith("//")) continue;

            // Section header — Arena-style "Deck"/"Sideboard"/"Commander"
            // lines have no leading count. Skip them; we roll all cards
            // into a single total.
            var m = LineRx.Match(line);
            if (!m.Success) continue;
            if (!int.TryParse(m.Groups["count"].Value, out var count)) continue;
            if (count <= 0) continue;

            var name = TailRx.Replace(m.Groups["name"].Value, "").Trim();
            if (name.Length == 0) continue;

            totals[name] = totals.GetValueOrDefault(name) + count;
        }
        return totals;
    }
}
