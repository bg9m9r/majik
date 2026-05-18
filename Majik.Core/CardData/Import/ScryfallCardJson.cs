using System.Text.Json.Serialization;

namespace Majik.Core.CardData.Import;

/// <summary>
/// Model for deserializing Scryfall card JSON data.
/// </summary>
public class ScryfallCardJson
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }
    
    [JsonPropertyName("name")]
    public string? Name { get; set; }
    
    [JsonPropertyName("mana_cost")]
    public string? ManaCost { get; set; }
    
    [JsonPropertyName("cmc")]
    [JsonConverter(typeof(FlexibleDoubleConverter))]
    public double? Cmc { get; set; }
    
    [JsonPropertyName("type_line")]
    public string? TypeLine { get; set; }
    
    [JsonPropertyName("oracle_text")]
    public string? OracleText { get; set; }
    
    [JsonPropertyName("power")]
    public string? Power { get; set; }
    
    [JsonPropertyName("toughness")]
    public string? Toughness { get; set; }
    
    [JsonPropertyName("loyalty")]
    [JsonConverter(typeof(FlexibleDoubleConverter))]
    public double? Loyalty { get; set; }
    
    [JsonPropertyName("colors")]
    public List<string>? Colors { get; set; }
    
    [JsonPropertyName("color_identity")]
    public List<string>? ColorIdentity { get; set; }
    
    [JsonPropertyName("keywords")]
    public List<string>? Keywords { get; set; }
    
    [JsonPropertyName("set")]
    public string? Set { get; set; }
    
    [JsonPropertyName("collector_number")]
    public string? CollectorNumber { get; set; }
    
    [JsonPropertyName("rarity")]
    public string? Rarity { get; set; }
    
    [JsonPropertyName("image_uris")]
    public ImageUris? ImageUris { get; set; }
    
    [JsonPropertyName("legalities")]
    public Dictionary<string, string>? Legalities { get; set; }
}

/// <summary>
/// Image URIs for a card.
/// </summary>
public class ImageUris
{
    [JsonPropertyName("small")]
    public string? Small { get; set; }
    
    [JsonPropertyName("normal")]
    public string? Normal { get; set; }
    
    [JsonPropertyName("large")]
    public string? Large { get; set; }
}
