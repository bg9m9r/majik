using System.Text.Json;
using Majik.Core.Abilities;
using Majik.Core.CardData.Database;
using Majik.Core.Players;

namespace Majik.Core.CardData.Parsing;

/// <summary>
/// Parser for keyword abilities from the database Keywords field.
/// Converts keyword names into ability entities that can be stored in the database.
/// </summary>
public class KeywordAbilityParser
{
    /// <summary>
    /// Parse keywords from a JSON array string and create ability entities.
    /// </summary>
    public List<CardAbilityEntity> ParseKeywords(string keywordsJson, int cardId)
    {
        var abilities = new List<CardAbilityEntity>();
        
        // Parse keywords using KeywordParser to handle parameterized keywords
        var parsedKeywords = KeywordParser.ParseKeywordsFromJson(keywordsJson);
        
        int abilityIndex = 0;
        foreach (var parsedKeyword in parsedKeywords)
        {
            // Check if it's likely a real keyword (not a card name)
            if (!KeywordParser.IsLikelyRealKeyword(parsedKeyword.BaseKeyword))
            {
                // Skip card names and custom abilities
                continue;
            }

            // Try to get keyword info (handles base keyword)
            var keywordInfo = KeywordRegistry.GetKeywordInfo(parsedKeyword.BaseKeyword);
            if (keywordInfo == null)
            {
                // Unknown keyword - skip or log warning
                // Could be a parameterized keyword we need to handle specially
                continue;
            }

            var ability = CreateAbilityFromKeyword(parsedKeyword.BaseKeyword, keywordInfo, cardId, abilityIndex++, parsedKeyword.Parameters);
            if (ability != null)
            {
                abilities.Add(ability);
            }
        }

        return abilities;
    }

    /// <summary>
    /// Create a CardAbilityEntity from a keyword.
    /// </summary>
    private CardAbilityEntity? CreateAbilityFromKeyword(
        string keyword,
        KeywordInfo keywordInfo,
        int cardId,
        int abilityIndex,
        Dictionary<string, string>? parameters = null)
    {
        var ability = new CardAbilityEntity
        {
            CardId = cardId,
            Type = MapKeywordTypeToAbilityType(keywordInfo.Type),
            AbilityIndex = abilityIndex,
            Layer = keywordInfo.Layer,
            ParsingMethod = ParsingMethod.Pattern,
            ParsedText = keyword,
            ParsingConfidence = "1.0",  // Keywords are 100% confident
            ParsedAt = DateTime.UtcNow
        };

        // For static abilities, we can create effect references
        if (keywordInfo.Type == KeywordType.Static && keywordInfo.Layer.HasValue)
        {
            // Create effect reference for "add_ability" effect
            var effectParams = new Dictionary<string, object> { { "ability", keyword } };
            if (parameters != null)
            {
                foreach (var param in parameters)
                {
                    effectParams[param.Key] = param.Value;
                }
            }

            ability.EffectReferences = JsonSerializer.Serialize(new[]
            {
                new { effectId = "add_ability", parameters = effectParams }
            });
        }

        return ability;
    }

    /// <summary>
    /// Map keyword type to ability type.
    /// </summary>
    private AbilityType MapKeywordTypeToAbilityType(KeywordType keywordType)
    {
        return keywordType switch
        {
            KeywordType.Static => AbilityType.Static,
            KeywordType.Triggered => AbilityType.Triggered,
            KeywordType.Activated => AbilityType.Activated,
            KeywordType.Replacement => AbilityType.Replacement,
            _ => AbilityType.Static
        };
    }

    /// <summary>
    /// Create ability instances from keyword abilities for runtime use.
    /// </summary>
    public List<object> CreateAbilitiesFromKeywords(
        IEnumerable<string> keywords,
        object source,
        Player controller)
    {
        var abilities = new List<object>();

        foreach (var keyword in keywords)
        {
            if (string.IsNullOrWhiteSpace(keyword))
                continue;

            var keywordInfo = KeywordRegistry.GetKeywordInfo(keyword);
            if (keywordInfo == null)
                continue;

            var ability = keywordInfo.CreateAbility(source, controller);
            if (ability != null)
            {
                abilities.Add(ability);
            }
        }

        return abilities;
    }
}
