namespace Majik.Core.CardData.Database;

/// <summary>
/// Entity for caching Claude API requests and responses to avoid duplicate API calls.
/// Uses a hash of the request prompt to identify duplicates.
/// </summary>
public class ClaudeRequestCacheEntity
{
    public int Id { get; set; }
    
    /// <summary>
    /// SHA256 hash of the request prompt (for fast lookup).
    /// </summary>
    public string RequestHash { get; set; } = "";
    
    /// <summary>
    /// The keyword this request is for (for easy querying).
    /// </summary>
    public string Keyword { get; set; } = "";
    
    /// <summary>
    /// The full request prompt sent to Claude.
    /// </summary>
    public string RequestPrompt { get; set; } = "";
    
    /// <summary>
    /// The full raw response from Claude API.
    /// </summary>
    public string ResponseText { get; set; } = "";
    
    /// <summary>
    /// Parsed implementation notes (JSON).
    /// </summary>
    public string? ParsedNotes { get; set; }
    
    /// <summary>
    /// Claude model used for this request.
    /// </summary>
    public string Model { get; set; } = "";
    
    /// <summary>
    /// When this request was made.
    /// </summary>
    public DateTime RequestedAt { get; set; }
    
    /// <summary>
    /// When this cache entry was last accessed (for cache management).
    /// </summary>
    public DateTime LastAccessedAt { get; set; }
}
