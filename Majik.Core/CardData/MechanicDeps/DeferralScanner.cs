using System.Text;
using System.Text.RegularExpressions;

namespace Majik.Core.CardData.MechanicDeps;

/// <summary>
/// Lightweight source-text scanner for "deferred rider" mentions in
/// factory source files. We deliberately do NOT pull in Roslyn —
/// xmldoc + inline comments are picked up by simple lexical rules,
/// which is enough for the impact-ranking use case.
///
/// Heuristic:
/// <list type="bullet">
///   <item>A "comment region" starts on any line whose first non-whitespace
///         characters are <c>///</c> or <c>//</c>. Consecutive comment
///         lines are stitched together into a single logical paragraph.</item>
///   <item>Within each paragraph we split on sentence boundaries
///         (<c>.</c>, <c>!</c>, <c>?</c>, em-dash <c>—</c>) and keep
///         any sentence containing one of the trigger keywords
///         (<c>deferred</c>, <c>DEFERRED</c>, <c>blocked on</c>,
///         <c>same gap</c>).</item>
///   <item>If the sentence contains a <c>CR XXX.Yz</c> citation we
///         capture it for downstream cluster keying.</item>
/// </list>
///
/// Output sentences are length-capped at 320 chars; longer paragraphs
/// stay coherent (the scanner does not chop mid-citation).
/// </summary>
public sealed class DeferralScanner
{
    private static readonly Regex CommentLineRx =
        new(@"^\s*(///|//)\s?(?<body>.*)$",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex CompRulesRx =
        new(@"\bCR\s*(?<n>\d{3}(?:\.\d+[a-z]?)?)\b",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly string[] TriggerKeywords =
    {
        "deferred", "DEFERRED",
        "blocked on", "same gap",
        "v1 gap",
    };

    /// <summary>
    /// Scan a single C# factory source file (already read into a string)
    /// for deferral mentions. <paramref name="factoryName"/> is just a
    /// label (the type name or filename stem) used to populate
    /// <see cref="DeferralMention.FactoryName"/> on the emitted records.
    /// </summary>
    public IReadOnlyList<DeferralMention> Scan(string filePath, string factoryName, string sourceText)
    {
        ArgumentNullException.ThrowIfNull(sourceText);
        var lines = sourceText.Split('\n');

        var paragraphs = new List<(int startLine, string text)>();
        var current = new StringBuilder();
        var paragraphStart = -1;

        for (int i = 0; i < lines.Length; i++)
        {
            var raw = lines[i].TrimEnd('\r');
            var m = CommentLineRx.Match(raw);
            if (m.Success)
            {
                var body = m.Groups["body"].Value;
                if (paragraphStart < 0) paragraphStart = i + 1;
                if (current.Length > 0) current.Append(' ');
                current.Append(body.Trim());
            }
            else if (paragraphStart >= 0)
            {
                paragraphs.Add((paragraphStart, current.ToString()));
                current.Clear();
                paragraphStart = -1;
            }
        }
        if (paragraphStart >= 0)
        {
            paragraphs.Add((paragraphStart, current.ToString()));
        }

        var results = new List<DeferralMention>();
        foreach (var (startLine, paragraph) in paragraphs)
        {
            if (!ContainsAny(paragraph, TriggerKeywords)) continue;

            foreach (var sentence in SplitSentences(paragraph))
            {
                if (!ContainsAny(sentence, TriggerKeywords)) continue;
                var trimmed = sentence.Trim();
                if (trimmed.Length == 0) continue;

                // Skip bare xmldoc section headers like "## Deferred (v1 gaps)".
                // They're navigational, not content — the actual deferred-rider
                // descriptions live in the following bullets / paragraphs and
                // are picked up there.
                if (IsBareSectionHeader(trimmed)) continue;

                if (trimmed.Length > 320) trimmed = trimmed[..320] + "…";

                var citation = ExtractCitation(trimmed);
                results.Add(new DeferralMention(
                    FactoryFile: filePath,
                    FactoryName: factoryName,
                    LineNumber: startLine,
                    Sentence: trimmed,
                    CompRulesCitation: citation));
            }
        }
        return results;
    }

    /// <summary>
    /// Scan every <c>*.cs</c> file under the supplied directory. The
    /// factory name is derived from the filename stem (e.g.
    /// <c>SlickshotShowOffFactory.cs</c> →
    /// <c>SlickshotShowOffFactory</c>).
    /// </summary>
    public IReadOnlyList<DeferralMention> ScanDirectory(string factoriesDir)
    {
        if (!Directory.Exists(factoriesDir))
        {
            return Array.Empty<DeferralMention>();
        }
        var all = new List<DeferralMention>();
        var files = Directory.EnumerateFiles(factoriesDir, "*.cs", SearchOption.AllDirectories);
        foreach (var file in files)
        {
            var name = Path.GetFileNameWithoutExtension(file);
            string text;
            try { text = File.ReadAllText(file); }
            catch (IOException) { continue; }
            all.AddRange(Scan(file, name, text));
        }
        return all;
    }

    private static readonly Regex BareHeaderRx =
        new(@"^\s*#{1,6}\s*Deferred\s*(\([^)]*\))?\s*[—:-]?\s*(</?\w+/?>)*\s*$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    /// Section headers like "## Deferred (v1 gaps)" contain a trigger
    /// keyword but no usable content; the bullets that follow carry the
    /// actual deferred-rider descriptions.
    /// </summary>
    private static bool IsBareSectionHeader(string sentence) =>
        BareHeaderRx.IsMatch(sentence);

    private static bool ContainsAny(string haystack, IEnumerable<string> needles)
    {
        foreach (var n in needles)
        {
            if (haystack.IndexOf(n, StringComparison.Ordinal) >= 0) return true;
        }
        return false;
    }

    private static IEnumerable<string> SplitSentences(string paragraph)
    {
        // Split on sentence terminators; keep the punctuation attached
        // by walking the string manually. We also treat the xmldoc bullet
        // pattern (" - " preceded by ">" or punctuation) as a soft break
        // so each "## Deferred (v1 gaps) - <b>Foo</b>: …" bullet becomes
        // its own logical sentence.
        var sb = new StringBuilder();
        for (int i = 0; i < paragraph.Length; i++)
        {
            var c = paragraph[i];

            // Bullet break: hyphen-space at start of a new bullet, but
            // only if preceded by whitespace and followed by another
            // chunk of content. This catches " - <b>" boundaries.
            if (c == '-' && i + 1 < paragraph.Length && paragraph[i + 1] == ' '
                && i >= 2 && paragraph[i - 1] == ' '
                && (paragraph[i - 2] == ')' || paragraph[i - 2] == '.'
                    || paragraph[i - 2] == '>' || paragraph[i - 2] == ':'))
            {
                if (sb.Length > 0) yield return sb.ToString();
                sb.Clear();
                continue;
            }

            sb.Append(c);
            if (c is '.' or '!' or '?')
            {
                // Don't split on decimals (e.g. "CR 702.143").
                bool decimalGuard =
                    c == '.' &&
                    i > 0 && char.IsDigit(paragraph[i - 1]) &&
                    i + 1 < paragraph.Length && char.IsDigit(paragraph[i + 1]);
                if (!decimalGuard)
                {
                    yield return sb.ToString();
                    sb.Clear();
                }
            }
        }
        if (sb.Length > 0) yield return sb.ToString();
    }

    private static string? ExtractCitation(string sentence)
    {
        var m = CompRulesRx.Match(sentence);
        if (!m.Success) return null;
        return "CR " + m.Groups["n"].Value;
    }
}
