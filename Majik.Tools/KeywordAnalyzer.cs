using System.Text.Json;
using System.Text.RegularExpressions;
using Majik.Core.CardData.Database;
using Majik.Core.CardData.Parsing;
using Microsoft.EntityFrameworkCore;

namespace Majik.Tools;

/// <summary>
/// Tool to analyze and categorize keywords from the database CSV file.
/// Helps identify which keywords are real Magic keywords vs card names.
/// </summary>
public class KeywordAnalyzer
{
    private readonly HashSet<string> _officialKeywords;
    private HashSet<string>? _cardNames;
    private readonly Dictionary<string, KeywordCategory> _categorizations;

    public KeywordAnalyzer()
    {
        _officialKeywords = LoadOfficialKeywords();
        _categorizations = new Dictionary<string, KeywordCategory>(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Get the count of loaded card names.
    /// </summary>
    public int GetCardNameCount() => _cardNames?.Count ?? 0;

    /// <summary>
    /// Load card names from database to filter out card names.
    /// </summary>
    public async Task LoadCardNamesAsync(CardDbContext dbContext, CancellationToken cancellationToken = default)
    {
        var names = await dbContext.Cards
            .Select(c => c.Name)
            .ToListAsync(cancellationToken);
        
        _cardNames = new HashSet<string>(names, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Analyze a keyword and categorize it.
    /// </summary>
    public KeywordAnalysis AnalyzeKeyword(string keyword)
    {
        if (string.IsNullOrWhiteSpace(keyword))
        {
            return new KeywordAnalysis
            {
                Keyword = keyword,
                Category = KeywordCategory.Unknown,
                Confidence = 0.0,
                Reason = "Empty keyword"
            };
        }

        var trimmed = keyword.Trim();

        // Check if it's a card name
        if (_cardNames != null && _cardNames.Contains(trimmed))
        {
            return new KeywordAnalysis
            {
                Keyword = trimmed,
                Category = KeywordCategory.CardName,
                Confidence = 1.0,
                Reason = "Matches card name in database"
            };
        }

        // Check if it's an official keyword
        if (_officialKeywords.Contains(trimmed, StringComparer.OrdinalIgnoreCase))
        {
            return new KeywordAnalysis
            {
                Keyword = trimmed,
                Category = KeywordCategory.Official,
                Confidence = 1.0,
                Reason = "Official Magic keyword (Rule 702)"
            };
        }

        // Check if it's registered in KeywordRegistry
        if (KeywordRegistry.IsRegistered(trimmed))
        {
            return new KeywordAnalysis
            {
                Keyword = trimmed,
                Category = KeywordCategory.Official,
                Confidence = 0.9,
                Reason = "Registered in KeywordRegistry"
            };
        }

        // Check for parameterized keywords
        var parsed = KeywordParser.ParseKeyword(trimmed);
        if (parsed.Parameters.Count > 0)
        {
            if (KeywordRegistry.IsRegistered(parsed.BaseKeyword))
            {
                return new KeywordAnalysis
                {
                    Keyword = trimmed,
                    Category = KeywordCategory.Parameterized,
                    Confidence = 0.95,
                    Reason = $"Parameterized variant of '{parsed.BaseKeyword}'",
                    BaseKeyword = parsed.BaseKeyword,
                    Parameters = parsed.Parameters
                };
            }
        }

        // Check if it looks like a card name
        if (IsLikelyCardName(trimmed))
        {
            return new KeywordAnalysis
            {
                Keyword = trimmed,
                Category = KeywordCategory.CardName,
                Confidence = 0.8,
                Reason = "Heuristics suggest card name"
            };
        }

        // Check if it looks like a real keyword
        if (KeywordParser.IsLikelyRealKeyword(trimmed))
        {
            return new KeywordAnalysis
            {
                Keyword = trimmed,
                Category = KeywordCategory.PossibleKeyword,
                Confidence = 0.6,
                Reason = "Heuristics suggest real keyword"
            };
        }

        // Check for custom/unofficial keywords
        if (IsCustomKeyword(trimmed))
        {
            return new KeywordAnalysis
            {
                Keyword = trimmed,
                Category = KeywordCategory.Custom,
                Confidence = 0.7,
                Reason = "Appears to be custom/unofficial keyword"
            };
        }

        // Unknown
        return new KeywordAnalysis
        {
            Keyword = trimmed,
            Category = KeywordCategory.Unknown,
            Confidence = 0.3,
            Reason = "Unable to categorize"
        };
    }

    /// <summary>
    /// Analyze all keywords from CSV file.
    /// </summary>
    public async Task<List<KeywordAnalysis>> AnalyzeKeywordsFromCsvAsync(
        string csvPath, 
        CancellationToken cancellationToken = default)
    {
        var analyses = new List<KeywordAnalysis>();

        if (!File.Exists(csvPath))
        {
            throw new FileNotFoundException($"CSV file not found: {csvPath}");
        }

        var lines = await File.ReadAllLinesAsync(csvPath, cancellationToken);
        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;

            // CSV format: just keyword names, one per line
            var keyword = line.Trim();
            if (keyword.StartsWith("\"") && keyword.EndsWith("\""))
            {
                keyword = keyword.Substring(1, keyword.Length - 2);
            }

            var analysis = AnalyzeKeyword(keyword);
            analyses.Add(analysis);
        }

        return analyses;
    }

    /// <summary>
    /// Save analysis results to database.
    /// </summary>
    public async Task SaveAnalysesToDatabaseAsync(
        CardDbContext dbContext,
        List<KeywordAnalysis> analyses,
        CancellationToken cancellationToken = default)
    {
        foreach (var analysis in analyses)
        {
            // Check if keyword metadata already exists
            var existing = await dbContext.KeywordMetadata
                .FirstOrDefaultAsync(k => k.Keyword == analysis.Keyword, cancellationToken);

            if (existing != null)
            {
                // Update existing
                existing.Category = MapCategory(analysis.Category);
                existing.Confidence = analysis.Confidence;
                existing.Reason = analysis.Reason;
                existing.BaseKeyword = analysis.BaseKeyword;
                existing.Parameters = analysis.Parameters != null 
                    ? JsonSerializer.Serialize(analysis.Parameters) 
                    : null;
                existing.UpdatedAt = DateTime.UtcNow;
                
                // Update implementation status if keyword is registered
                if (KeywordRegistry.IsRegistered(analysis.Keyword) || 
                    (analysis.BaseKeyword != null && KeywordRegistry.IsRegistered(analysis.BaseKeyword)))
                {
                    existing.ImplementationStatus = ImplementationStatus.Implemented;
                }
            }
            else
            {
                // Create new
                var keywordInfo = KeywordRegistry.GetKeywordInfo(analysis.Keyword) ??
                    (analysis.BaseKeyword != null ? KeywordRegistry.GetKeywordInfo(analysis.BaseKeyword) : null);

                var entity = new KeywordMetadataEntity
                {
                    Keyword = analysis.Keyword,
                    Category = MapCategory(analysis.Category),
                    Confidence = analysis.Confidence,
                    Reason = analysis.Reason,
                    BaseKeyword = analysis.BaseKeyword,
                    Parameters = analysis.Parameters != null 
                        ? JsonSerializer.Serialize(analysis.Parameters) 
                        : null,
                    AbilityType = keywordInfo != null ? MapKeywordTypeToAbilityType(keywordInfo.Type) : null,
                    Layer = keywordInfo?.Layer,
                    ImplementationStatus = keywordInfo != null 
                        ? ImplementationStatus.Implemented 
                        : ImplementationStatus.NotImplemented,
                    CreatedAt = DateTime.UtcNow
                };

                dbContext.KeywordMetadata.Add(entity);
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private KeywordCategory MapCategory(KeywordCategory category)
    {
        return category; // Same enum, just mapping for clarity
    }

    private AbilityType? MapKeywordTypeToAbilityType(KeywordType keywordType)
    {
        return keywordType switch
        {
            KeywordType.Static => AbilityType.Static,
            KeywordType.Triggered => AbilityType.Triggered,
            KeywordType.Activated => AbilityType.Activated,
            KeywordType.Replacement => AbilityType.Replacement,
            _ => null
        };
    }

    /// <summary>
    /// Generate a categorization report.
    /// </summary>
    public KeywordReport GenerateReport(List<KeywordAnalysis> analyses)
    {
        var report = new KeywordReport
        {
            TotalKeywords = analyses.Count,
            ByCategory = analyses
                .GroupBy(a => a.Category)
                .ToDictionary(g => g.Key, g => g.Count()),
            OfficialKeywords = analyses
                .Where(a => a.Category == KeywordCategory.Official)
                .Select(a => a.Keyword)
                .ToList(),
            ParameterizedKeywords = analyses
                .Where(a => a.Category == KeywordCategory.Parameterized)
                .Select(a => a.Keyword)
                .ToList(),
            CardNames = analyses
                .Where(a => a.Category == KeywordCategory.CardName)
                .Select(a => a.Keyword)
                .ToList(),
            CustomKeywords = analyses
                .Where(a => a.Category == KeywordCategory.Custom)
                .Select(a => a.Keyword)
                .ToList(),
            UnknownKeywords = analyses
                .Where(a => a.Category == KeywordCategory.Unknown)
                .Select(a => a.Keyword)
                .ToList()
        };

        return report;
    }

    private HashSet<string> LoadOfficialKeywords()
    {
        // Load official keywords from Magic Rules (Rule 702)
        // This is a comprehensive list extracted from the rules
        var keywords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            // Static abilities
            "deathtouch", "defender", "double strike", "first strike", "flash", "flying",
            "haste", "hexproof", "indestructible", "intimidate", "lifelink", "protection",
            "reach", "shroud", "trample", "vigilance", "shadow", "horsemanship", "fear",
            "skulk", "menace", "infect", "wither", "toxic", "decayed",
            
            // Landwalk variants
            "islandwalk", "swampwalk", "forestwalk", "mountainwalk", "plainswalk",
            "landwalk", "desertwalk", "nonbasic landwalk", "legendary landwalk",
            
            // Triggered abilities
            "prowess", "landfall", "exalted", "enrage", "ward", "constellation",
            "delirium", "morbid", "raid", "revolt", "threshold", "hellbent",
            "metalcraft", "spell mastery", "fateful hour", "ferocious", "formidable",
            "battalion", "celebration", "coven", "domain", "pack tactics",
            "radiance", "rampage", "flanking", "bushido",
            
            // Activated abilities
            "cycling", "equip", "channel", "flashback", "kicker", "multikicker",
            "buyback", "retrace", "rebound", "replicate", "overload", "dash",
            "bloodrush", "bestow", "evoke", "morph", "megamorph", "manifest",
            "ninjutsu", "prowl", "scavenge", "unearth", "jump-start", "escape",
            "foretell", "boast", "encore", "mutate", "disguise", "cloak",
            
            // Cycling variants
            "basic landcycling", "forestcycling", "islandcycling", "mountaincycling",
            "plainscycling", "swampcycling", "landcycling", "typecycling",
            "slivercycling", "wizardcycling",
            
            // Other keywords
            "banding", "cumulative upkeep", "echo", "fading", "amplify", "provoke",
            "storm", "affinity", "entwine", "modular", "sunburst", "soulshift",
            "splice", "offering", "epic", "convoke", "dredge", "transmute",
            "bloodthirst", "haunt", "graft", "recover", "ripple", "split second",
            "suspend", "vanishing", "absorb", "aura swap", "delve", "fortify",
            "frenzy", "gravestorm", "poisonous", "transfigure", "champion",
            "changeling", "hideaway", "reinforce", "conspire", "persist",
            "devour", "cascade", "annihilator", "level up", "umbra armor",
            "battle cry", "living weapon", "undying", "miracle", "soulbond",
            "scavenge", "unleash", "cipher", "evolve", "extort", "fuse",
            "tribute", "dethrone", "hidden agenda", "outlast", "exploit",
            "renown", "awaken", "devoid", "ingest", "myriad", "surge",
            "emerge", "escalate", "melee", "crew", "fabricate", "partner",
            "partner with", "undaunted", "improvise", "aftermath", "embalm",
            "eternalize", "afflict", "ascend", "assist", "mentor", "afterlife",
            "riot", "spectacle", "companion", "daybound", "nightbound", "disturb",
            "cleave", "training", "compleated", "reconfigure", "blitz", "casualty",
            "enlist", "read ahead", "ravenous", "squad", "backup", "bargain",
            "craft", "plot", "saddle", "spree", "freerunning", "gift", "offspring",
            "impending", "exhaust", "max speed", "start your engines!", "harmonize",
            "mobilize", "job select", "tiered", "station", "warp", "mayhem",
            "web-slinging", "firebending"
        };

        return keywords;
    }

    private bool IsLikelyCardName(string keyword)
    {
        // Heuristics for card names
        if (keyword.Length > 40)
            return true;

        // Multiple capital letters (likely card name)
        if (Regex.IsMatch(keyword, @"[A-Z][a-z]+.*[A-Z]"))
            return true;

        // Ends with punctuation (likely card name)
        if (Regex.IsMatch(keyword, @"[!?\.]$"))
            return true;

        // Contains quotes (likely card name)
        if (keyword.Contains("\""))
            return true;

        // Very long with spaces (likely card name)
        var words = keyword.Split(' ');
        if (words.Length > 4)
            return true;

        return false;
    }

    private bool IsCustomKeyword(string keyword)
    {
        // Heuristics for custom keywords
        // Custom keywords often have specific patterns
        
        // Bending keywords (custom)
        if (keyword.EndsWith("bend", StringComparison.OrdinalIgnoreCase) ||
            keyword.EndsWith("bending", StringComparison.OrdinalIgnoreCase))
            return true;

        // Doctor Who related
        if (keyword.Contains("doctor", StringComparison.OrdinalIgnoreCase) ||
            keyword.Contains("tardis", StringComparison.OrdinalIgnoreCase))
            return true;

        // Special set keywords
        if (keyword.Contains("companion", StringComparison.OrdinalIgnoreCase) ||
            keyword.Contains("friends", StringComparison.OrdinalIgnoreCase))
            return true;

        return false;
    }
}

/// <summary>
/// Analysis result for a keyword.
/// </summary>
public class KeywordAnalysis
{
    public string Keyword { get; set; } = "";
    public KeywordCategory Category { get; set; }
    public double Confidence { get; set; }  // 0.0 to 1.0
    public string Reason { get; set; } = "";
    public string? BaseKeyword { get; set; }
    public Dictionary<string, string>? Parameters { get; set; }
}

/// <summary>
/// Category of keyword.
/// </summary>
public enum KeywordCategory
{
    Official,        // Official Magic keyword (Rule 702)
    Parameterized,   // Parameterized variant (ward {2}, protection from X)
    Custom,          // Custom/unofficial keyword
    CardName,        // Actually a card name, not a keyword
    PossibleKeyword, // Might be a keyword, needs review
    Unknown          // Unable to categorize
}

/// <summary>
/// Report of keyword categorization.
/// </summary>
public class KeywordReport
{
    public int TotalKeywords { get; set; }
    public Dictionary<KeywordCategory, int> ByCategory { get; set; } = new();
    public List<string> OfficialKeywords { get; set; } = new();
    public List<string> ParameterizedKeywords { get; set; } = new();
    public List<string> CardNames { get; set; } = new();
    public List<string> CustomKeywords { get; set; } = new();
    public List<string> UnknownKeywords { get; set; } = new();
}
