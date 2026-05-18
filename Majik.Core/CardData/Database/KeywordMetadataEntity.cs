namespace Majik.Core.CardData.Database;

/// <summary>
/// Entity model for storing keyword metadata and categorization results.
/// </summary>
public class KeywordMetadataEntity
{
    public int Id { get; set; }
    
    /// <summary>
    /// The keyword name (unique).
    /// </summary>
    public string Keyword { get; set; } = "";
    
    /// <summary>
    /// Category of the keyword.
    /// </summary>
    public KeywordCategory Category { get; set; }
    
    /// <summary>
    /// Confidence level (0.0 to 1.0).
    /// </summary>
    public double Confidence { get; set; }
    
    /// <summary>
    /// Reason for categorization.
    /// </summary>
    public string? Reason { get; set; }
    
    /// <summary>
    /// Base keyword (for parameterized keywords).
    /// </summary>
    public string? BaseKeyword { get; set; }
    
    /// <summary>
    /// Parameters (JSON object) for parameterized keywords.
    /// </summary>
    public string? Parameters { get; set; }
    
    /// <summary>
    /// Ability type (Static, Triggered, Activated, Replacement).
    /// </summary>
    public AbilityType? AbilityType { get; set; }
    
    /// <summary>
    /// Layer (for static abilities).
    /// </summary>
    public int? Layer { get; set; }
    
    /// <summary>
    /// Implementation status.
    /// </summary>
    public ImplementationStatus ImplementationStatus { get; set; }
    
    /// <summary>
    /// Notes about this keyword (implementation notes from Claude).
    /// </summary>
    public string? Notes { get; set; }
    
    /// <summary>
    /// Sublayer (for Layer 7 sublayers: a, b, c, d).
    /// </summary>
    public string? Sublayer { get; set; }
    
    /// <summary>
    /// Code example from Claude implementation notes (JSON).
    /// </summary>
    public string? CodeExample { get; set; }
    
    /// <summary>
    /// Testing notes from Claude (JSON).
    /// </summary>
    public string? TestingNotes { get; set; }
    
    /// <summary>
    /// Common mistakes to avoid (JSON array).
    /// </summary>
    public string? CommonMistakes { get; set; }
    
    /// <summary>
    /// Related keywords (JSON array).
    /// </summary>
    public string? RelatedKeywords { get; set; }
    
    /// <summary>
    /// Complexity level (Simple, Medium, Complex).
    /// </summary>
    public string? Complexity { get; set; }
    
    /// <summary>
    /// Full raw response from Claude API (JSON) - for debugging and future reference.
    /// </summary>
    public string? ClaudeRawResponse { get; set; }
    
    /// <summary>
    /// When this metadata was created.
    /// </summary>
    public DateTime CreatedAt { get; set; }
    
    /// <summary>
    /// When this metadata was last updated.
    /// </summary>
    public DateTime? UpdatedAt { get; set; }
}

/// <summary>
/// Category of keyword.
/// </summary>
public enum KeywordCategory
{
    Official = 0,        // Official Magic keyword (Rule 702)
    Parameterized = 1,   // Parameterized variant (ward {2}, protection from X)
    Custom = 2,          // Custom/unofficial keyword
    CardName = 3,        // Actually a card name, not a keyword
    PossibleKeyword = 4, // Might be a keyword, needs review
    Unknown = 5          // Unable to categorize
}

/// <summary>
/// Implementation status of a keyword.
/// </summary>
public enum ImplementationStatus
{
    NotImplemented = 0,
    Implemented = 1,
    Placeholder = 2,
    NeedsReview = 3
}
