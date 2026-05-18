using Majik.Core.CardData.Database;

namespace Majik.Core.CardData.Parsing;

/// <summary>
/// Main parser for oracle text.
/// Splits oracle text into individual abilities and delegates parsing to specialized parsers.
/// </summary>
public class OracleTextParser
{
    private readonly PatternBasedParser _patternParser;

    public OracleTextParser()
    {
        _patternParser = new PatternBasedParser();
    }

    /// <summary>
    /// Parse oracle text into structured abilities.
    /// </summary>
    public ParseResult Parse(string oracleText)
    {
        if (string.IsNullOrWhiteSpace(oracleText))
        {
            return new ParseResult
            {
                Abilities = new List<ParsedAbility>(),
                Confidence = 1.0,
                Method = ParsingMethod.Pattern
            };
        }

        // Split oracle text into paragraphs (each paragraph is a separate ability per Rule 113.2c)
        var paragraphs = SplitIntoAbilities(oracleText);
        
        var abilities = new List<ParsedAbility>();
        double totalConfidence = 0.0;

        foreach (var paragraph in paragraphs)
        {
            var ability = _patternParser.ParseAbility(paragraph);
            if (ability != null)
            {
                abilities.Add(ability);
                totalConfidence += ability.Confidence;
            }
        }

        var averageConfidence = abilities.Count > 0 ? totalConfidence / abilities.Count : 0.0;

        return new ParseResult
        {
            Abilities = abilities,
            Confidence = averageConfidence,
            Method = ParsingMethod.Pattern
        };
    }

    /// <summary>
    /// Split oracle text into individual abilities.
    /// Each paragraph break indicates a separate ability (Rule 113.2c).
    /// </summary>
    private List<string> SplitIntoAbilities(string oracleText)
    {
        // Split by double newlines or single newline followed by capital letter (new sentence/ability)
        var abilities = new List<string>();
        
        // Remove reminder text (text in parentheses)
        var cleaned = RemoveReminderText(oracleText);
        
        // Split by paragraph breaks
        var paragraphs = cleaned.Split(new[] { "\n\n", "\r\n\r\n" }, StringSplitOptions.RemoveEmptyEntries);
        
        foreach (var paragraph in paragraphs)
        {
            var trimmed = paragraph.Trim();
            if (!string.IsNullOrWhiteSpace(trimmed))
            {
                abilities.Add(trimmed);
            }
        }

        // If no paragraph breaks, treat entire text as one ability
        if (abilities.Count == 0 && !string.IsNullOrWhiteSpace(cleaned))
        {
            abilities.Add(cleaned.Trim());
        }

        return abilities;
    }

    /// <summary>
    /// Remove reminder text (text in parentheses).
    /// </summary>
    private string RemoveReminderText(string text)
    {
        // Simple approach: remove text in parentheses
        // This is a heuristic and may not be perfect for all cases
        var result = System.Text.RegularExpressions.Regex.Replace(
            text, 
            @"\([^)]*\)", 
            string.Empty);
        
        // Clean up extra whitespace
        result = System.Text.RegularExpressions.Regex.Replace(result, @"\s+", " ");
        
        return result.Trim();
    }
}

/// <summary>
/// Result of parsing oracle text.
/// </summary>
public class ParseResult
{
    public List<ParsedAbility> Abilities { get; set; } = new();
    public double Confidence { get; set; }  // 0.0 to 1.0
    public ParsingMethod Method { get; set; }
}

/// <summary>
/// Parsed ability structure.
/// </summary>
public class ParsedAbility
{
    public AbilityType Type { get; set; }
    public int AbilityIndex { get; set; }
    public string? TriggerCondition { get; set; }  // JSON
    public bool HasInterveningIf { get; set; }
    public string? InterveningIfCondition { get; set; }  // JSON
    public string? ActivationCost { get; set; }  // JSON
    public List<EffectReference> Effects { get; set; } = new();
    public int? Layer { get; set; }
    public int? Sublayer { get; set; }
    public string OriginalText { get; set; } = "";
    public double Confidence { get; set; }  // 0.0 to 1.0
}

/// <summary>
/// Reference to an effect with parameters.
/// </summary>
public class EffectReference
{
    public string EffectId { get; set; } = "";
    public Dictionary<string, object> Parameters { get; set; } = new();
    public int Order { get; set; }
}
