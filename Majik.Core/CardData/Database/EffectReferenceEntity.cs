namespace Majik.Core.CardData.Database;

/// <summary>
/// Entity model for storing references to effect library entries.
/// </summary>
public class EffectReferenceEntity
{
    public int Id { get; set; }
    
    /// <summary>
    /// Unique ID in effect library (e.g., "damage_target", "draw_cards").
    /// </summary>
    public string EffectId { get; set; } = "";
    
    /// <summary>
    /// Type of effect.
    /// </summary>
    public EffectType Type { get; set; }
    
    /// <summary>
    /// Human-readable name.
    /// </summary>
    public string Name { get; set; } = "";
    
    /// <summary>
    /// Effect description.
    /// </summary>
    public string? Description { get; set; }
    
    /// <summary>
    /// Parameter schema (JSON object describing expected parameters).
    /// </summary>
    public string Parameters { get; set; } = "{}";
    
    /// <summary>
    /// Whether this is a built-in effect in the effect library.
    /// </summary>
    public bool IsBuiltIn { get; set; }
    
    /// <summary>
    /// When this effect reference was created.
    /// </summary>
    public DateTime CreatedAt { get; set; }
    
    /// <summary>
    /// Navigation property to card ability effects using this reference.
    /// </summary>
    public ICollection<CardAbilityEffectEntity> CardAbilityEffects { get; set; } = new List<CardAbilityEffectEntity>();
}

/// <summary>
/// Type of effect.
/// </summary>
public enum EffectType
{
    Damage = 0,
    Life = 1,
    Draw = 2,
    Token = 3,
    Counter = 4,
    Destroy = 5,
    Exile = 6,
    Return = 7,
    Search = 8,
    Tap = 9,
    ModifyPT = 10,
    AddAbility = 11,
    ChangeControl = 12,
    ChangeType = 13,
    ChangeColor = 14,
    Other = 99
}
