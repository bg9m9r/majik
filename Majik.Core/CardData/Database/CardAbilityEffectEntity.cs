namespace Majik.Core.CardData.Database;

/// <summary>
/// Join table entity linking card abilities to their effects.
/// </summary>
public class CardAbilityEffectEntity
{
    public int Id { get; set; }
    
    /// <summary>
    /// Foreign key to CardAbilityEntity.
    /// </summary>
    public int CardAbilityId { get; set; }
    
    /// <summary>
    /// Navigation property to the card ability.
    /// </summary>
    public CardAbilityEntity CardAbility { get; set; } = null!;
    
    /// <summary>
    /// Foreign key to EffectReferenceEntity.
    /// </summary>
    public int EffectReferenceId { get; set; }
    
    /// <summary>
    /// Navigation property to the effect reference.
    /// </summary>
    public EffectReferenceEntity EffectReference { get; set; } = null!;
    
    /// <summary>
    /// Order of this effect in the ability (0-based).
    /// </summary>
    public int EffectOrder { get; set; }
    
    /// <summary>
    /// Actual parameters for this effect instance (JSON object).
    /// </summary>
    public string EffectParameters { get; set; } = "{}";
}
