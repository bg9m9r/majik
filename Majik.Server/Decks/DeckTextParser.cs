using System.Text.RegularExpressions;
using Majik.Core.CardData;

namespace Majik.Server.Decks;

public sealed record DeckTextParseResult(
    IReadOnlyList<DeckCardEntry> Mainboard,
    IReadOnlyList<DeckCardEntry> Sideboard,
    IReadOnlyList<string> Unknown,
    IReadOnlyList<string> Warnings);

public sealed class DeckTextParser
{
    private static readonly Regex LineRegex = new(
        @"^(\d+)\s*[xX]?\s+(.+?)(?:\s*\([A-Za-z0-9]{2,5}\)(?:\s*\d+)?)?\s*$",
        RegexOptions.Compiled);

    private static readonly HashSet<string> MainHeaders = new(StringComparer.OrdinalIgnoreCase)
    { "deck", "mainboard", "maindeck", "main" };

    private static readonly HashSet<string> SideHeaders = new(StringComparer.OrdinalIgnoreCase)
    { "sideboard", "side", "sb" };

    private readonly ICardRepository _cards;

    public DeckTextParser(ICardRepository cards) { _cards = cards; }

    public Task<DeckTextParseResult> ParseAsync(string text, CancellationToken ct)
    {
        var lines = (text ?? string.Empty).Replace("\r\n", "\n").Split('\n');

        // Decide whether to use header mode or blank-line-split mode.
        bool hasHeaders = lines.Any(l =>
        {
            var t = l.Trim();
            return MainHeaders.Contains(t) || SideHeaders.Contains(t);
        });

        var main = new Dictionary<string, int>(StringComparer.Ordinal);
        var side = new Dictionary<string, int>(StringComparer.Ordinal);
        var unknown = new List<string>();
        var warnings = new List<string>();

        bool inSide = false;
        bool seenBlank = false;

        for (int i = 0; i < lines.Length; i++)
        {
            ct.ThrowIfCancellationRequested();

            var raw = lines[i];
            var line = raw.Trim();

            if (line.Length == 0)
            {
                if (!hasHeaders) seenBlank = true;
                continue;
            }
            if (line.StartsWith("//") || line.StartsWith("#")) continue;

            if (hasHeaders)
            {
                if (MainHeaders.Contains(line)) { inSide = false; continue; }
                if (SideHeaders.Contains(line)) { inSide = true; continue; }
            }
            else
            {
                inSide = seenBlank;
            }

            var match = LineRegex.Match(line);
            if (!match.Success)
            {
                warnings.Add($"Line {i + 1}: malformed — expected '<count> <name>'");
                continue;
            }

            int count = int.Parse(match.Groups[1].Value);
            string rawName = match.Groups[2].Value.Trim();
            // Collapse internal whitespace
            rawName = Regex.Replace(rawName, @"\s+", " ");

            if (count < 1)
            {
                warnings.Add($"Line {i + 1}: count must be at least 1");
                continue;
            }

            var entity = _cards.GetByName(rawName);
            if (entity == null)
            {
                unknown.Add(rawName);
                continue;
            }

            if (!entity.IsImplemented)
            {
                warnings.Add($"Card not implemented: {entity.Name}");
            }

            var bucket = inSide ? side : main;
            bucket[entity.Name] = bucket.GetValueOrDefault(entity.Name) + count;
        }

        var result = new DeckTextParseResult(
            Mainboard: main.Select(kv => new DeckCardEntry { Name = kv.Key, Count = kv.Value }).ToList(),
            Sideboard: side.Select(kv => new DeckCardEntry { Name = kv.Key, Count = kv.Value }).ToList(),
            Unknown: unknown,
            Warnings: warnings);

        return Task.FromResult(result);
    }
}
