using System.Text.RegularExpressions;
using Majik.Core.CardData.Database;

namespace Majik.Core.CardData.Parsing;

/// <summary>
/// Pattern-based parser for common oracle text patterns.
/// Uses regex patterns to identify and parse common ability structures.
/// </summary>
public class PatternBasedParser
{
    /// <summary>
    /// Parse an ability from oracle text.
    /// </summary>
    public ParsedAbility? ParseAbility(string abilityText)
    {
        if (string.IsNullOrWhiteSpace(abilityText))
            return null;

        var trimmed = abilityText.Trim();

        // Try to identify ability type and parse accordingly
        if (IsTriggeredAbility(trimmed))
        {
            return ParseTriggeredAbility(trimmed);
        }
        else if (IsActivatedAbility(trimmed))
        {
            return ParseActivatedAbility(trimmed);
        }
        else if (IsStaticAbility(trimmed))
        {
            return ParseStaticAbility(trimmed);
        }
        else if (IsReplacementEffect(trimmed))
        {
            return ParseReplacementEffect(trimmed);
        }

        // Unknown ability type - return null (will need AI parsing)
        return null;
    }

    private bool IsTriggeredAbility(string text)
    {
        // Triggered abilities start with "Whenever", "When", or "At"
        return Regex.IsMatch(text, @"^(Whenever|When|At)\s+", RegexOptions.IgnoreCase);
    }

    private bool IsActivatedAbility(string text)
    {
        // Activated abilities have format: "{Cost}: {Effect}"
        return Regex.IsMatch(text, @"^\{[^}]+\}:");
    }

    private bool IsStaticAbility(string text)
    {
        // Static abilities don't have trigger words or activation costs
        // Common patterns: "Creatures you control get...", "This creature has...", etc.
        return !IsTriggeredAbility(text) && !IsActivatedAbility(text) && !IsReplacementEffect(text);
    }

    private bool IsReplacementEffect(string text)
    {
        // Replacement effects use "instead", "skip", "as [permanent] enters"
        return Regex.IsMatch(text, @"\b(instead|skip|as\s+\w+\s+enters)\b", RegexOptions.IgnoreCase);
    }

    private ParsedAbility ParseTriggeredAbility(string text)
    {
        var ability = new ParsedAbility
        {
            Type = AbilityType.Triggered,
            OriginalText = text,
            Confidence = 0.7  // Medium confidence for pattern matching
        };

        // Extract trigger word and condition
        var triggerMatch = Regex.Match(text, @"^(Whenever|When|At)\s+(.+?)(?:,\s*if\s+(.+?))?,\s*(.+)", RegexOptions.IgnoreCase);
        
        if (triggerMatch.Success)
        {
            ability.TriggerCondition = triggerMatch.Groups[2].Value.Trim();
            
            // Check for intervening-if
            if (triggerMatch.Groups[3].Success)
            {
                ability.HasInterveningIf = true;
                ability.InterveningIfCondition = triggerMatch.Groups[3].Value.Trim();
            }

            // Extract effect text
            var effectText = triggerMatch.Groups[4].Value.Trim();
            ability.Effects = ParseEffects(effectText);
        }
        else
        {
            // Fallback: try simpler pattern
            var simpleMatch = Regex.Match(text, @"^(Whenever|When|At)\s+(.+?),\s*(.+)", RegexOptions.IgnoreCase);
            if (simpleMatch.Success)
            {
                ability.TriggerCondition = simpleMatch.Groups[2].Value.Trim();
                var effectText = simpleMatch.Groups[3].Value.Trim();
                ability.Effects = ParseEffects(effectText);
            }
            else
            {
                // Can't parse - low confidence
                ability.Confidence = 0.3;
            }
        }

        return ability;
    }

    private ParsedAbility ParseActivatedAbility(string text)
    {
        var ability = new ParsedAbility
        {
            Type = AbilityType.Activated,
            OriginalText = text,
            Confidence = 0.7
        };

        // Extract cost and effect: "{Cost}: {Effect}"
        var match = Regex.Match(text, @"^(\{[^}]+\}(?:\s*,\s*\{[^}]+\})*)\s*:\s*(.+)", RegexOptions.IgnoreCase);
        
        if (match.Success)
        {
            ability.ActivationCost = match.Groups[1].Value.Trim();
            var effectText = match.Groups[2].Value.Trim();
            ability.Effects = ParseEffects(effectText);
        }
        else
        {
            ability.Confidence = 0.3;
        }

        return ability;
    }

    private ParsedAbility ParseStaticAbility(string text)
    {
        var ability = new ParsedAbility
        {
            Type = AbilityType.Static,
            OriginalText = text,
            Confidence = 0.6
        };

        // Try to determine layer
        DetermineLayer(ability, text);

        // Parse effects
        ability.Effects = ParseEffects(text);

        return ability;
    }

    private ParsedAbility ParseReplacementEffect(string text)
    {
        var ability = new ParsedAbility
        {
            Type = AbilityType.Replacement,
            OriginalText = text,
            Confidence = 0.6
        };

        // Parse replacement effect
        ability.Effects = ParseEffects(text);

        return ability;
    }

    private void DetermineLayer(ParsedAbility ability, string text)
    {
        // Determine which layer this static ability applies to
        // Layer 7: P/T modifications
        if (Regex.IsMatch(text, @"\b(gets?\s+\+?\d+/\+\d+|power\s+and\s+toughness|base\s+power|base\s+toughness)\b", RegexOptions.IgnoreCase))
        {
            ability.Layer = 7;
            // Determine sublayer
            if (Regex.IsMatch(text, @"power\s+and\s+toughness\s+are\s+each\s+equal", RegexOptions.IgnoreCase))
            {
                ability.Sublayer = 1;  // Layer 7a: CDA
            }
            else if (Regex.IsMatch(text, @"becomes\s+\d+/\d+|base\s+power|base\s+toughness", RegexOptions.IgnoreCase))
            {
                ability.Sublayer = 2;  // Layer 7b: Set P/T
            }
            else
            {
                ability.Sublayer = 3;  // Layer 7c: Modify P/T
            }
        }
        // Layer 6: Ability adding/removing
        else if (Regex.IsMatch(text, @"\b(has|gains?|loses?)\s+\w+", RegexOptions.IgnoreCase))
        {
            ability.Layer = 6;
        }
        // Layer 5: Color changing
        else if (Regex.IsMatch(text, @"\b(is|becomes?)\s+(all\s+)?colors?", RegexOptions.IgnoreCase))
        {
            ability.Layer = 5;
        }
        // Layer 4: Type changing
        else if (Regex.IsMatch(text, @"\b(is|becomes?)\s+a\s+\w+", RegexOptions.IgnoreCase))
        {
            ability.Layer = 4;
        }
        // Layer 2: Control changing
        else if (Regex.IsMatch(text, @"\b(gain\s+control|control)\b", RegexOptions.IgnoreCase))
        {
            ability.Layer = 2;
        }
    }

    private List<EffectReference> ParseEffects(string effectText)
    {
        var effects = new List<EffectReference>();
        int order = 0;

        // Try to match common effect patterns
        // Damage effects
        var damageMatch = Regex.Match(effectText, @"deals?\s+(\d+)\s+damage\s+to\s+(?:any\s+)?target", RegexOptions.IgnoreCase);
        if (damageMatch.Success)
        {
            effects.Add(new EffectReference
            {
                EffectId = "damage_any",
                Parameters = new Dictionary<string, object> { { "amount", int.Parse(damageMatch.Groups[1].Value) } },
                Order = order++
            });
        }

        // Life effects
        var gainLifeMatch = Regex.Match(effectText, @"gain\s+(\d+)\s+life", RegexOptions.IgnoreCase);
        if (gainLifeMatch.Success)
        {
            effects.Add(new EffectReference
            {
                EffectId = "gain_life",
                Parameters = new Dictionary<string, object> { { "amount", int.Parse(gainLifeMatch.Groups[1].Value) } },
                Order = order++
            });
        }

        var loseLifeMatch = Regex.Match(effectText, @"lose\s+(\d+)\s+life", RegexOptions.IgnoreCase);
        if (loseLifeMatch.Success)
        {
            effects.Add(new EffectReference
            {
                EffectId = "lose_life",
                Parameters = new Dictionary<string, object> { { "amount", int.Parse(loseLifeMatch.Groups[1].Value) } },
                Order = order++
            });
        }

        // Draw effects
        var drawMatch = Regex.Match(effectText, @"draw\s+(\d+)\s+card", RegexOptions.IgnoreCase);
        if (drawMatch.Success)
        {
            effects.Add(new EffectReference
            {
                EffectId = "draw_cards",
                Parameters = new Dictionary<string, object> { { "amount", int.Parse(drawMatch.Groups[1].Value) } },
                Order = order++
            });
        }

        // If no effects matched, return empty list
        // This will need AI parsing or manual handling
        return effects;
    }
}
