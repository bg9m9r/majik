namespace Majik.Core.CardData.Import;

/// <summary>
/// Progress information for card import operations.
/// </summary>
public class ImportProgress
{
    /// <summary>
    /// Total number of cards (or -1 if unknown when streaming).
    /// </summary>
    public int Total { get; set; } = -1;
    
    /// <summary>
    /// Number of cards processed.
    /// </summary>
    public int Processed { get; set; }
    
    /// <summary>
    /// Number of cards imported (new).
    /// </summary>
    public int Imported { get; set; }
    
    /// <summary>
    /// Number of cards updated (existing).
    /// </summary>
    public int Updated { get; set; }
    
    /// <summary>
    /// Number of cards skipped (errors).
    /// </summary>
    public int Skipped { get; set; }
    
    /// <summary>
    /// Bytes processed (for streaming progress).
    /// </summary>
    public long BytesProcessed { get; set; }
    
    /// <summary>
    /// Total bytes in file.
    /// </summary>
    public long TotalBytes { get; set; }
    
    /// <summary>
    /// Progress percentage (bytes-based when streaming).
    /// </summary>
    public double ProgressPercent { get; set; }
    
    /// <summary>
    /// Calculated percentage complete (uses Total if available, otherwise ProgressPercent).
    /// </summary>
    public double PercentComplete => Total > 0 
        ? (double)Processed / Total * 100 
        : ProgressPercent;
}
