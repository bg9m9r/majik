namespace Majik.Core.CardData.Database;

/// <summary>
/// Entity model for storing card data in the database.
/// </summary>
public class CardEntity
{
    public int Id { get; set; }  // Primary key (auto-increment)
    
    /// <summary>
    /// Unique Scryfall ID for the card.
    /// </summary>
    public string ScryfallId { get; set; } = "";
    
    /// <summary>
    /// Card name (indexed for fast lookups).
    /// </summary>
    public string Name { get; set; } = "";
    
    /// <summary>
    /// Mana cost string (e.g., "{1}{R}").
    /// </summary>
    public string? ManaCost { get; set; }
    
    /// <summary>
    /// Converted mana cost.
    /// </summary>
    public int? Cmc { get; set; }
    
    /// <summary>
    /// Full type line (e.g., "Creature — Human Wizard").
    /// </summary>
    public string TypeLine { get; set; } = "";
    
    /// <summary>
    /// Oracle text (rules text).
    /// </summary>
    public string? OracleText { get; set; }
    
    /// <summary>
    /// Power (for creatures, can be "*", "X", etc.).
    /// </summary>
    public string? Power { get; set; }
    
    /// <summary>
    /// Toughness (for creatures, can be "*", "X", etc.).
    /// </summary>
    public string? Toughness { get; set; }
    
    /// <summary>
    /// Loyalty (for planeswalkers).
    /// </summary>
    public int? Loyalty { get; set; }
    
    /// <summary>
    /// Colors as JSON array string (e.g., "[\"R\"]").
    /// </summary>
    public string Colors { get; set; } = "[]";
    
    /// <summary>
    /// Color identity as JSON array string.
    /// </summary>
    public string ColorIdentity { get; set; } = "[]";
    
    /// <summary>
    /// Keywords as JSON array string.
    /// </summary>
    public string Keywords { get; set; } = "[]";
    
    /// <summary>
    /// Set code (e.g., "m21").
    /// </summary>
    public string? Set { get; set; }
    
    /// <summary>
    /// Collector number.
    /// </summary>
    public string? CollectorNumber { get; set; }
    
    /// <summary>
    /// Rarity (e.g., "common", "uncommon", "rare", "mythic").
    /// </summary>
    public string? Rarity { get; set; }
    
    /// <summary>
    /// Image URI (normal size).
    /// </summary>
    public string? ImageUri { get; set; }
    
    /// <summary>
    /// Legalities as JSON object string.
    /// </summary>
    public string Legalities { get; set; } = "{}";
    
    /// <summary>
    /// Timestamp when card was imported.
    /// </summary>
    public DateTime ImportedAt { get; set; }
    
    /// <summary>
    /// Timestamp when card was last updated.
    /// </summary>
    public DateTime? UpdatedAt { get; set; }

    /// <summary>
    /// Flag indicating whether the card's abilities have been implemented in the engine.
    /// </summary>
    public bool IsImplemented { get; set; }

    /// <summary>
    /// Per-format legality rows. One entry per format key Scryfall reports
    /// (<c>modern</c>, <c>standard</c>, <c>pioneer</c>, …) with the raw status
    /// string (<c>legal | not_legal | banned | restricted</c>). Source of truth
    /// is the <see cref="Legalities"/> JSON blob; this navigation property is
    /// the denormalized index populated at import time so coverage queries can
    /// filter by format without scanning JSON.
    /// </summary>
    public List<CardLegalityEntity> FormatLegalities { get; set; } = new();
}
