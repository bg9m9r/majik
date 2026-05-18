namespace Majik.Core.CardData.Database;

/// <summary>
/// Entity model for storing parsed abilities tied to cards.
/// </summary>
public class CardAbilityEntity
{
    public int Id { get; set; }
    
    /// <summary>
    /// Foreign key to CardEntity.
    /// </summary>
    public int CardId { get; set; }
    
    /// <summary>
    /// Navigation property to the card.
    /// </summary>
    public CardEntity Card { get; set; } = null!;
    
    /// <summary>
    /// Type of ability: Triggered, Activated, Static, Replacement.
    /// </summary>
    public AbilityType Type { get; set; }
    
    /// <summary>
    /// Order in oracle text (0-based).
    /// </summary>
    public int AbilityIndex { get; set; }
    
    /// <summary>
    /// Trigger condition for triggered abilities (JSON).
    /// </summary>
    public string? TriggerCondition { get; set; }
    
    /// <summary>
    /// Whether this triggered ability has an intervening-if clause.
    /// </summary>
    public bool HasInterveningIf { get; set; }
    
    /// <summary>
    /// Intervening-if condition (JSON).
    /// </summary>
    public string? InterveningIfCondition { get; set; }
    
    /// <summary>
    /// Activation cost for activated abilities (JSON).
    /// </summary>
    public string? ActivationCost { get; set; }
    
    /// <summary>
    /// Effect references (JSON array of effect reference IDs).
    /// </summary>
    public string EffectReferences { get; set; } = "[]";
    
    /// <summary>
    /// Layer for static abilities (1-7).
    /// </summary>
    public int? Layer { get; set; }
    
    /// <summary>
    /// Sublayer for Layer 1 and Layer 7.
    /// </summary>
    public int? Sublayer { get; set; }
    
    /// <summary>
    /// Method used to parse this ability.
    /// </summary>
    public ParsingMethod ParsingMethod { get; set; }
    
    /// <summary>
    /// Original ability text that was parsed.
    /// </summary>
    public string? ParsedText { get; set; }
    
    /// <summary>
    /// Parsing confidence (for AI parsing, 0.0-1.0).
    /// </summary>
    public string? ParsingConfidence { get; set; }
    
    /// <summary>
    /// When this ability was parsed.
    /// </summary>
    public DateTime ParsedAt { get; set; }
    
    /// <summary>
    /// Navigation property to ability effects.
    /// </summary>
    public ICollection<CardAbilityEffectEntity> Effects { get; set; } = new List<CardAbilityEffectEntity>();
}

/// <summary>
/// Type of ability.
/// </summary>
public enum AbilityType
{
    Triggered = 0,
    Activated = 1,
    Static = 2,
    Replacement = 3
}

/// <summary>
/// Method used to parse the ability.
/// </summary>
public enum ParsingMethod
{
    Pattern = 0,
    AI = 1,
    Manual = 2
}
