using System.Text.Json;
using System.Text.RegularExpressions;

namespace Majik.Core.CardData.Parsing;

/// <summary>
/// Helper class for parsing and handling keywords, including parameterized keywords.
/// </summary>
public static class KeywordParser
{
    /// <summary>
    /// Parse a keyword string that may contain parameters.
    /// Examples: "ward {2}", "protection from red", "hexproof from blue"
    /// </summary>
    public static ParsedKeyword ParseKeyword(string keywordText)
    {
        if (string.IsNullOrWhiteSpace(keywordText))
            return new ParsedKeyword { BaseKeyword = keywordText, Parameters = new Dictionary<string, string>() };

        var trimmed = keywordText.Trim();

        // Check for parameterized keywords
        // Ward {X}
        var wardMatch = Regex.Match(trimmed, @"^ward\s+\{([^}]+)\}$", RegexOptions.IgnoreCase);
        if (wardMatch.Success)
        {
            return new ParsedKeyword
            {
                BaseKeyword = "ward",
                Parameters = new Dictionary<string, string> { { "cost", wardMatch.Groups[1].Value } }
            };
        }

        // Protection from X
        var protectionMatch = Regex.Match(trimmed, @"^protection\s+from\s+(.+)$", RegexOptions.IgnoreCase);
        if (protectionMatch.Success)
        {
            return new ParsedKeyword
            {
                BaseKeyword = "protection",
                Parameters = new Dictionary<string, string> { { "from", protectionMatch.Groups[1].Value.Trim() } }
            };
        }

        // Hexproof from X
        var hexproofMatch = Regex.Match(trimmed, @"^hexproof\s+from\s+(.+)$", RegexOptions.IgnoreCase);
        if (hexproofMatch.Success)
        {
            return new ParsedKeyword
            {
                BaseKeyword = "hexproof from",
                Parameters = new Dictionary<string, string> { { "from", hexproofMatch.Groups[1].Value.Trim() } }
            };
        }

        // Cycling variants with costs are handled as separate keywords (basic landcycling, etc.)
        // So we just return the base keyword
        return new ParsedKeyword
        {
            BaseKeyword = trimmed.ToLowerInvariant(),
            Parameters = new Dictionary<string, string>()
        };
    }

    /// <summary>
    /// Parse keywords from a JSON array string.
    /// </summary>
    public static List<ParsedKeyword> ParseKeywordsFromJson(string keywordsJson)
    {
        var parsed = new List<ParsedKeyword>();

        if (string.IsNullOrWhiteSpace(keywordsJson) || keywordsJson == "[]")
            return parsed;

        try
        {
            var keywords = JsonSerializer.Deserialize<List<string>>(keywordsJson);
            if (keywords == null)
                return parsed;

            foreach (var keyword in keywords)
            {
                if (string.IsNullOrWhiteSpace(keyword))
                    continue;

                var parsedKeyword = ParseKeyword(keyword);
                parsed.Add(parsedKeyword);
            }
        }
        catch (JsonException)
        {
            // Invalid JSON - return empty list
        }

        return parsed;
    }

    /// <summary>
    /// Check if a keyword string is likely a real Magic keyword vs a card name or custom ability.
    /// This is a heuristic - many entries in the CSV are card names, not keywords.
    /// </summary>
    public static bool IsLikelyRealKeyword(string keyword)
    {
        if (string.IsNullOrWhiteSpace(keyword))
            return false;

        // Real keywords are typically lowercase, single words or short phrases
        // Card names often have capital letters, punctuation, or are longer phrases
        var trimmed = keyword.Trim();

        // Check if it's registered
        if (KeywordRegistry.IsRegistered(trimmed))
            return true;

        // Heuristics for real keywords:
        // - All lowercase (or mostly lowercase)
        // - No punctuation (except in parameterized forms like "ward {2}")
        // - Short (typically 1-3 words)
        // - Common Magic terms

        // Check for parameterized forms
        if (Regex.IsMatch(trimmed, @"^(ward|protection|hexproof)\s+", RegexOptions.IgnoreCase))
            return true;

        // Check if it looks like a card name (has capitals, punctuation, long)
        if (trimmed.Length > 30)
            return false;

        if (Regex.IsMatch(trimmed, @"[A-Z][a-z]+.*[A-Z]")) // Multiple capital letters (likely card name)
            return false;

        if (Regex.IsMatch(trimmed, @"[!?\.]$")) // Ends with punctuation (likely card name)
            return false;

        // Common Magic keyword patterns
        var commonPatterns = new[]
        {
            @"^(flying|haste|trample|vigilance|deathtouch|lifelink|hexproof|shroud|indestructible|defender|menace|skulk|fear|intimidate|horsemanship)$",
            @"^(first strike|double strike|reach|shadow)$",
            @"^(islandwalk|swampwalk|forestwalk|mountainwalk|plainswalk|landwalk|desertwalk)$",
            @"^(cycling|equip|flashback|kicker|multikicker)$",
            @"^(landfall|prowess|exalted|enrage)$",
            @"^(flash|haste|vigilance|defender)$"
        };

        foreach (var pattern in commonPatterns)
        {
            if (Regex.IsMatch(trimmed, pattern, RegexOptions.IgnoreCase))
                return true;
        }

        // If it's a single lowercase word, it might be a keyword
        if (Regex.IsMatch(trimmed, @"^[a-z]+$"))
            return true;

        return false;
    }
}

/// <summary>
/// Parsed keyword with parameters.
/// </summary>
public class ParsedKeyword
{
    public string BaseKeyword { get; set; } = "";
    public Dictionary<string, string> Parameters { get; set; } = new();
}
