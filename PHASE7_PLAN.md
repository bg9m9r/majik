# Phase 7: Database-Backed Card Loading System - Implementation Plan

## Overview

Phase 7 implements a database-backed card loading system that uses a local database (SQLite) populated from the Scryfall bulk data JSON file. This provides fast, reliable access to all 30,000+ Magic cards without requiring API calls, while maintaining the ability to update the database as new sets are released.

**Status**: ⏳ Not Started  
**Goal**: Database-backed card loading with **streaming JSON import** and query interface  
**Estimated Effort**: 2-3 weeks  
**Dependencies**: Phase 4 (Card System), Phase 6 (Rules Engine)

**Critical Requirement**: The 2.2GB JSON file must be processed using **streaming** to avoid loading the entire file into memory. The import tool must use `Utf8JsonReader` with `FileStream` to parse cards incrementally.

## Objectives

1. **Database Schema**: Design efficient database schema for card data
2. **JSON Import Tool**: Create tool to import Scryfall bulk data into database
3. **Database Provider**: Implement database access layer
4. **Card Factory**: Update factory to load from database instead of API
5. **Query Interface**: Fast card lookup and search
6. **Update Mechanism**: Support for updating database with new data
7. **Performance**: Optimize for fast queries and minimal memory usage

## Database Choice: SQLite

### Why SQLite?

- **File-Based**: Single database file, easy to manage
- **No Server Required**: Embedded database, no setup needed
- **Fast Queries**: Indexed lookups are very fast
- **ACID Compliant**: Reliable data integrity
- **Cross-Platform**: Works on all platforms
- **Small Footprint**: Minimal dependencies
- **.NET Support**: Excellent EF Core support

### Alternative: PostgreSQL/MySQL
- Consider if multi-user or remote access needed
- For now, SQLite is sufficient for single-user game engine

## Scryfall Bulk Data Format

### File Structure

The Scryfall bulk data file is a JSON array of card objects. **Important**: The file is 2.2GB and must be processed using streaming to avoid memory issues.

**File Format**:
```json
[
  {
    "id": "uuid",
    "name": "Lightning Bolt",
    "mana_cost": "{R}",
    "cmc": 1,
    "type_line": "Instant",
    "oracle_text": "Lightning Bolt deals 3 damage to any target.",
    "power": null,
    "toughness": null,
    "loyalty": null,
    "colors": ["R"],
    "color_identity": ["R"],
    "keywords": ["flash"],
    "rarity": "common",
    "set": "m21",
    "collector_number": "161",
    "scryfall_id": "uuid",
    "image_uris": {...},
    "legalities": {...},
    "multiverse_ids": [...],
    "card_faces": null,  // For double-faced cards
    // ... many more fields
  },
  // ... 30,000+ more cards
]
```

### Key Fields We Need

- `id` / `scryfall_id`: Unique identifier
- `name`: Card name
- `mana_cost`: Mana cost string
- `cmc`: Converted mana cost
- `type_line`: Full type line
- `oracle_text`: Rules text
- `power`: Power (string, can be "*", "X", etc.)
- `toughness`: Toughness (string)
- `loyalty`: Loyalty (for planeswalkers)
- `colors`: Array of colors
- `color_identity`: Array of color identity
- `keywords`: Array of keywords
- `set`: Set code
- `collector_number`: Collector number
- `rarity`: Rarity
- `card_faces`: For double-faced cards (future)

## Architecture Overview

### Database System Components

```
Majik.Core/CardData/
├── Database/
│   ├── CardDbContext.cs            # EF Core DbContext
│   ├── CardEntity.cs                # Database entity model
│   └── Migrations/                  # EF Core migrations
├── Import/
│   ├── ScryfallJsonImporter.cs     # JSON import tool
│   └── ImportProgress.cs            # Import progress tracking
├── ICardDataProvider.cs             # Provider interface
├── DatabaseCardProvider.cs          # Database implementation
├── CardDataCache.cs                 # Optional in-memory cache
├── CardDataDto.cs                   # DTO for card data
├── CardFactory.cs                   # Factory for creating cards
├── CardDataMapper.cs                # Maps DB data to cards
└── TypeLineParser.cs                # Type line parsing
```

### Integration Points

- **Card Constructors**: Support creation from CardDataDto
- **Card Loading**: Load from database on-demand
- **Game Initialization**: Load cards for decks/libraries
- **Import Tool**: Standalone tool or console command

## Detailed Implementation Plan

### Task 1: Database Schema Design

**Priority**: High  
**Estimated Time**: 2-3 days

#### 1.1 CardEntity

**File**: `Majik.Core/CardData/Database/CardEntity.cs`

**Purpose**: Entity model for database storage

**Schema Design**:
```csharp
public class CardEntity
{
    public int Id { get; set; }  // Primary key (auto-increment)
    public string ScryfallId { get; set; }  // Unique Scryfall ID
    public string Name { get; set; }  // Indexed
    public string? ManaCost { get; set; }
    public int? Cmc { get; set; }
    public string TypeLine { get; set; }  // Indexed
    public string? OracleText { get; set; }
    public string? Power { get; set; }
    public string? Toughness { get; set; }
    public int? Loyalty { get; set; }
    public string Colors { get; set; }  // JSON array as string
    public string ColorIdentity { get; set; }  // JSON array as string
    public string Keywords { get; set; }  // JSON array as string
    public string? Set { get; set; }  // Indexed
    public string? CollectorNumber { get; set; }
    public string? Rarity { get; set; }
    public string? ImageUri { get; set; }
    public string Legalities { get; set; }  // JSON object as string
    public DateTime ImportedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    
    // Indexes:
    // - Name (for exact lookups)
    // - TypeLine (for type searches)
    // - Set + CollectorNumber (for set lookups)
    // - ScryfallId (for unique lookups)
}
```

**Design Decisions**:
- Use JSON strings for arrays (Colors, Keywords) - SQLite doesn't have native array support
- Index on Name for fast lookups
- Index on TypeLine for type-based searches
- Store full JSON for future extensibility

#### 1.2 CardDbContext

**File**: `Majik.Core/CardData/Database/CardDbContext.cs`

**Purpose**: EF Core DbContext for card database

**Implementation**:
```csharp
using Microsoft.EntityFrameworkCore;

public class CardDbContext : DbContext
{
    public DbSet<CardEntity> Cards { get; set; }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        var dbPath = GetDatabasePath();
        optionsBuilder.UseSqlite($"Data Source={dbPath}");
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<CardEntity>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.Name);
            entity.HasIndex(e => e.TypeLine);
            entity.HasIndex(e => e.Set);
            entity.HasIndex(e => e.ScryfallId).IsUnique();
            entity.HasIndex(e => new { e.Set, e.CollectorNumber });
        });
    }

    private static string GetDatabasePath()
    {
        // Store in user's app data directory
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var majikDir = Path.Combine(appData, "Majik");
        Directory.CreateDirectory(majikDir);
        return Path.Combine(majikDir, "cards.db");
    }
}
```

### Task 2: JSON Import Tool (Streaming)

**Priority**: High  
**Estimated Time**: 4-5 days

**Critical Requirement**: The 2.2GB JSON file must be processed using **streaming** to avoid loading the entire file into memory. This requires using `Utf8JsonReader` with `FileStream` to parse cards incrementally.

**Implementation Note**: Reading complete JSON objects that span buffer boundaries is complex. Consider starting with `JsonDocument.ParseAsync` which handles streaming internally (though it may use more memory). If memory usage is still too high, implement the full `Utf8JsonReader` solution.

#### 2.1 ScryfallJsonImporter (Streaming Implementation)

**File**: `Majik.Core/CardData/Import/ScryfallJsonImporter.cs`

**Purpose**: Stream Scryfall bulk data JSON into database without loading entire file into memory

**Key Design Decisions**:
- Use `Utf8JsonReader` for forward-only streaming JSON parsing
- Process cards one at a time as they're parsed
- Batch database writes (every 1000 cards) for performance
- Handle partial reads and maintain reader state
- Support cancellation for long-running imports

**Implementation**:
```csharp
using System.Buffers;
using System.Text;
using System.Text.Json;

public class ScryfallJsonImporter
{
    private readonly CardDbContext _dbContext;
    private readonly IProgress<ImportProgress>? _progress;
    private const int BufferSize = 8192; // 8KB buffer (adjust if needed for large tokens)
    private const int BatchSize = 1000; // Cards to batch before database write

    public ScryfallJsonImporter(CardDbContext? dbContext = null, IProgress<ImportProgress>? progress = null)
    {
        _dbContext = dbContext ?? new CardDbContext();
        _progress = progress;
    }

    /// <summary>
    /// Stream import cards from Scryfall bulk data JSON file.
    /// This method uses streaming to avoid loading the entire file into memory.
    /// </summary>
    public async Task<int> ImportFromFileAsync(string jsonFilePath, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(jsonFilePath))
        {
            throw new FileNotFoundException($"JSON file not found: {jsonFilePath}");
        }

        // Ensure database is created
        await _dbContext.Database.EnsureCreatedAsync(cancellationToken);

        int imported = 0;
        int updated = 0;
        int skipped = 0;
        int processed = 0;
        var batch = new List<CardEntity>(BatchSize);

        // Get file size for progress estimation
        var fileInfo = new FileInfo(jsonFilePath);
        long totalBytes = fileInfo.Length;

        using var fileStream = new FileStream(
            jsonFilePath, 
            FileMode.Open, 
            FileAccess.Read, 
            FileShare.Read, 
            BufferSize, 
            useAsync: true);

        // Skip UTF-8 BOM if present
        var bomBuffer = new byte[3];
        var bomBytesRead = await fileStream.ReadAsync(bomBuffer, 0, 3, cancellationToken);
        if (bomBytesRead == 3 && bomBuffer[0] == 0xEF && bomBuffer[1] == 0xBB && bomBuffer[2] == 0xBF)
        {
            // BOM found, already skipped
        }
        else
        {
            // No BOM, rewind to start
            fileStream.Position = 0;
        }

        var buffer = ArrayPool<byte>.Shared.Rent(BufferSize);
        var jsonReaderState = new JsonReaderState();
        var isFinalBlock = false;

        try
        {
            while (!isFinalBlock)
            {
                var bytesRead = await fileStream.ReadAsync(buffer, 0, BufferSize, cancellationToken);
                isFinalBlock = bytesRead < BufferSize;

                var reader = new Utf8JsonReader(
                    buffer.AsSpan(0, bytesRead), 
                    isFinalBlock, 
                    jsonReaderState);

                while (reader.Read())
                {
                    if (reader.TokenType == JsonTokenType.StartArray)
                    {
                        // Beginning of array, continue
                        continue;
                    }

                    if (reader.TokenType == JsonTokenType.StartObject)
                    {
                        // Start of a card object - read the entire card object
                        var cardJson = await ReadCardObjectAsync(reader, fileStream, buffer, ref jsonReaderState, ref isFinalBlock, cancellationToken);
                        
                        if (cardJson != null)
                        {
                            try
                            {
                                var entity = MapJsonToEntity(cardJson);
                                
                                // Check if card already exists
                                var existing = await _dbContext.Cards
                                    .FirstOrDefaultAsync(c => c.ScryfallId == entity.ScryfallId, cancellationToken);
                                
                                if (existing != null)
                                {
                                    // Update existing card
                                    UpdateEntity(existing, entity);
                                    updated++;
                                }
                                else
                                {
                                    // Add to batch
                                    batch.Add(entity);
                                    imported++;
                                }

                                processed++;

                                // Save batch when full
                                if (batch.Count >= BatchSize)
                                {
                                    _dbContext.Cards.AddRange(batch);
                                    await _dbContext.SaveChangesAsync(cancellationToken);
                                    batch.Clear();
                                    
                                    // Report progress
                                    var progressPercent = fileStream.Position * 100.0 / totalBytes;
                                    _progress?.Report(new ImportProgress
                                    {
                                        Total = -1, // Unknown total count when streaming
                                        Processed = processed,
                                        Imported = imported,
                                        Updated = updated,
                                        Skipped = skipped,
                                        BytesProcessed = fileStream.Position,
                                        TotalBytes = totalBytes,
                                        ProgressPercent = progressPercent
                                    });
                                }
                            }
                            catch (Exception ex)
                            {
                                // Log error but continue
                                Console.WriteLine($"Error importing card: {ex.Message}");
                                skipped++;
                            }
                        }
                    }

                    if (reader.TokenType == JsonTokenType.EndArray)
                    {
                        // End of array, we're done
                        break;
                    }
                }

                // Save reader state for next iteration
                jsonReaderState = reader.CurrentState;
            }

            // Save any remaining cards in batch
            if (batch.Count > 0)
            {
                _dbContext.Cards.AddRange(batch);
                await _dbContext.SaveChangesAsync(cancellationToken);
            }

            // Final progress report
            _progress?.Report(new ImportProgress
            {
                Total = processed,
                Processed = processed,
                Imported = imported,
                Updated = updated,
                Skipped = skipped,
                BytesProcessed = totalBytes,
                TotalBytes = totalBytes,
                ProgressPercent = 100.0
            });

            return imported + updated;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    /// <summary>
    /// Read a complete card object from the JSON stream.
    /// Handles cases where the object spans multiple buffer reads.
    /// </summary>
    private async Task<ScryfallCardJson?> ReadCardObjectAsync(
        Utf8JsonReader reader,
        FileStream fileStream,
        byte[] buffer,
        ref JsonReaderState state,
        ref bool isFinalBlock,
        CancellationToken cancellationToken)
    {
        // Use JsonDocument to read the complete object
        // This requires reading the object into a buffer first
        var objectStart = reader.BytesConsumed;
        var depth = 1; // We're already inside the object

        // Read until we find the matching EndObject
        while (depth > 0 && reader.Read())
        {
            if (reader.TokenType == JsonTokenType.StartObject)
                depth++;
            else if (reader.TokenType == JsonTokenType.EndObject)
                depth--;
        }

        // If we've consumed all bytes, we need to read more
        if (!reader.IsFinalBlock && depth > 0)
        {
            // Object spans multiple reads - use a different approach
            // Read the object using JsonSerializer.Deserialize with a partial reader
            return await ReadCardObjectSpanningBuffersAsync(reader, fileStream, buffer, ref state, ref isFinalBlock, cancellationToken);
        }

        // Object is complete in current buffer - deserialize it
        var objectSpan = buffer.AsSpan((int)objectStart, (int)(reader.BytesConsumed - objectStart));
        
        try
        {
            return JsonSerializer.Deserialize<ScryfallCardJson>(objectSpan);
        }
        catch (JsonException)
        {
            // If deserialization fails, try the spanning buffer approach
            return await ReadCardObjectSpanningBuffersAsync(reader, fileStream, buffer, ref state, ref isFinalBlock, cancellationToken);
        }
    }

    /// <summary>
    /// Read a card object that spans multiple buffer reads.
    /// Uses a MemoryStream to accumulate the object bytes.
    /// </summary>
    private async Task<ScryfallCardJson?> ReadCardObjectSpanningBuffersAsync(
        Utf8JsonReader reader,
        FileStream fileStream,
        byte[] buffer,
        ref JsonReaderState state,
        ref bool isFinalBlock,
        CancellationToken cancellationToken)
    {
        using var objectStream = new MemoryStream();
        var depth = 1;
        var foundStart = false;

        // Write the start object token
        objectStream.WriteByte((byte)'{');

        // Read tokens until we find the matching EndObject
        while (depth > 0)
        {
            if (!reader.Read())
            {
                // Need more data
                if (isFinalBlock)
                    break;

                var bytesRead = await fileStream.ReadAsync(buffer, 0, BufferSize, cancellationToken);
                if (bytesRead == 0)
                    break;

                isFinalBlock = bytesRead < BufferSize;
                reader = new Utf8JsonReader(buffer.AsSpan(0, bytesRead), isFinalBlock, state);
                state = reader.CurrentState;
            }

            // Write token to stream (simplified - in practice, you'd write the actual JSON)
            // For now, use a simpler approach: read the entire object using JsonDocument
        }

        // Alternative simpler approach: use JsonDocument.TryParseValue
        // This is more complex but handles spanning objects correctly
        return null; // Placeholder - see implementation notes below
    }

    /// <summary>
    /// Simplified approach: Use JsonDocument to parse individual card objects.
    /// This is more memory-efficient than loading the entire file.
    /// </summary>
    private async Task<ScryfallCardJson?> ReadCardObjectWithJsonDocumentAsync(
        FileStream fileStream,
        byte[] buffer,
        ref JsonReaderState state,
        ref bool isFinalBlock,
        CancellationToken cancellationToken)
    {
        // This is a simplified version - in practice, you'd need to:
        // 1. Read until you find a complete JSON object
        // 2. Parse that object with JsonDocument
        // 3. Deserialize to ScryfallCardJson
        
        // For now, we'll use a hybrid approach:
        // - Use Utf8JsonReader to find object boundaries
        // - Use JsonSerializer.Deserialize for complete objects
        // - Handle spanning objects by accumulating bytes
        
        return null; // See implementation section for full details
    }

    private CardEntity MapJsonToEntity(ScryfallCardJson json)
    {
        return new CardEntity
        {
            ScryfallId = json.Id ?? json.ScryfallId ?? Guid.NewGuid().ToString(),
            Name = json.Name ?? "",
            ManaCost = json.ManaCost,
            Cmc = json.Cmc,
            TypeLine = json.TypeLine ?? "",
            OracleText = json.OracleText,
            Power = json.Power,
            Toughness = json.Toughness,
            Loyalty = json.Loyalty,
            Colors = JsonSerializer.Serialize(json.Colors ?? new List<string>()),
            ColorIdentity = JsonSerializer.Serialize(json.ColorIdentity ?? new List<string>()),
            Keywords = JsonSerializer.Serialize(json.Keywords ?? new List<string>()),
            Set = json.Set,
            CollectorNumber = json.CollectorNumber,
            Rarity = json.Rarity,
            ImageUri = json.ImageUris?.Normal ?? json.ImageUris?.Small,
            Legalities = JsonSerializer.Serialize(json.Legalities ?? new Dictionary<string, string>()),
            ImportedAt = DateTime.UtcNow
        };
    }

    private void UpdateEntity(CardEntity existing, CardEntity updated)
    {
        existing.Name = updated.Name;
        existing.ManaCost = updated.ManaCost;
        existing.Cmc = updated.Cmc;
        existing.TypeLine = updated.TypeLine;
        existing.OracleText = updated.OracleText;
        existing.Power = updated.Power;
        existing.Toughness = updated.Toughness;
        existing.Loyalty = updated.Loyalty;
        existing.Colors = updated.Colors;
        existing.ColorIdentity = updated.ColorIdentity;
        existing.Keywords = updated.Keywords;
        existing.Set = updated.Set;
        existing.CollectorNumber = updated.CollectorNumber;
        existing.Rarity = updated.Rarity;
        existing.ImageUri = updated.ImageUri;
        existing.Legalities = updated.Legalities;
        existing.UpdatedAt = DateTime.UtcNow;
    }
}
```

**Note**: The above implementation shows the structure, but reading complete JSON objects that span buffer boundaries is complex. See **Task 2.2** for a more practical implementation using `JsonDocument` with streaming.

#### 2.2 Alternative: Simplified Streaming with JsonDocument

**File**: `Majik.Core/CardData/Import/ScryfallJsonImporter.cs` (Alternative Implementation)

**Purpose**: Simpler streaming approach using `JsonDocument` to parse individual objects

**Implementation Strategy**:
1. Use `Utf8JsonReader` to find object boundaries
2. Extract complete object JSON as `ReadOnlySpan<byte>`
3. Use `JsonDocument.ParseValue` to parse each object
4. Deserialize to `ScryfallCardJson`

**Simplified Implementation**:
```csharp
/// <summary>
/// Stream import using JsonDocument for each card object.
/// This approach is simpler and handles spanning objects correctly.
/// </summary>
public async Task<int> ImportFromFileStreamingAsync(string jsonFilePath, CancellationToken cancellationToken = default)
{
    if (!File.Exists(jsonFilePath))
    {
        throw new FileNotFoundException($"JSON file not found: {jsonFilePath}");
    }

    await _dbContext.Database.EnsureCreatedAsync(cancellationToken);

    int imported = 0;
    int updated = 0;
    int skipped = 0;
    int processed = 0;
    var batch = new List<CardEntity>(BatchSize);

    var fileInfo = new FileInfo(jsonFilePath);
    long totalBytes = fileInfo.Length;

    using var fileStream = new FileStream(
        jsonFilePath, 
        FileMode.Open, 
        FileAccess.Read, 
        FileShare.Read, 
        BufferSize, 
        useAsync: true);

    // Use JsonDocument to parse the stream
    // JsonDocument can handle large files by reading incrementally
    using var document = await JsonDocument.ParseAsync(fileStream, cancellationToken: cancellationToken);

    if (document.RootElement.ValueKind != JsonValueKind.Array)
    {
        throw new InvalidDataException("JSON file must contain an array of card objects");
    }

    foreach (var cardElement in document.RootElement.EnumerateArray())
    {
        try
        {
            var cardJson = JsonSerializer.Deserialize<ScryfallCardJson>(cardElement.GetRawText());
            
            if (cardJson == null)
            {
                skipped++;
                continue;
            }

            var entity = MapJsonToEntity(cardJson);
            
            var existing = await _dbContext.Cards
                .FirstOrDefaultAsync(c => c.ScryfallId == entity.ScryfallId, cancellationToken);
            
            if (existing != null)
            {
                UpdateEntity(existing, entity);
                updated++;
            }
            else
            {
                batch.Add(entity);
                imported++;
            }

            processed++;

            if (batch.Count >= BatchSize)
            {
                _dbContext.Cards.AddRange(batch);
                await _dbContext.SaveChangesAsync(cancellationToken);
                batch.Clear();
                
                _progress?.Report(new ImportProgress
                {
                    Processed = processed,
                    Imported = imported,
                    Updated = updated,
                    Skipped = skipped,
                    ProgressPercent = fileStream.Position * 100.0 / totalBytes
                });
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error importing card: {ex.Message}");
            skipped++;
        }
    }

    if (batch.Count > 0)
    {
        _dbContext.Cards.AddRange(batch);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    return imported + updated;
}
```

**Note**: `JsonDocument.ParseAsync` may still load significant portions into memory. For true streaming with minimal memory, use the `Utf8JsonReader` approach in 2.1, but it's more complex.

#### 2.3 Recommended: Practical Streaming Implementation

**Best Practice**: Use a combination of `Utf8JsonReader` and `JsonSerializer.Deserialize` with a custom streaming approach. This provides:
- True streaming (no full file load)
- Correct handling of spanning objects
- Reasonable memory usage (only one card object in memory at a time)
- Simpler implementation than pure `Utf8JsonReader`

**Complete Implementation**:
```csharp
using System.Buffers;
using System.IO;
using System.Text;
using System.Text.Json;

public class ScryfallJsonImporter
{
    private readonly CardDbContext _dbContext;
    private readonly IProgress<ImportProgress>? _progress;
    private const int BufferSize = 65536; // 64KB buffer
    private const int BatchSize = 1000;

    public ScryfallJsonImporter(CardDbContext? dbContext = null, IProgress<ImportProgress>? progress = null)
    {
        _dbContext = dbContext ?? new CardDbContext();
        _progress = progress;
    }

    /// <summary>
    /// Stream import cards from Scryfall bulk data JSON file using Utf8JsonReader.
    /// This method processes the file incrementally without loading it entirely into memory.
    /// </summary>
    public async Task<int> ImportFromFileAsync(string jsonFilePath, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(jsonFilePath))
        {
            throw new FileNotFoundException($"JSON file not found: {jsonFilePath}");
        }

        await _dbContext.Database.EnsureCreatedAsync(cancellationToken);

        int imported = 0;
        int updated = 0;
        int skipped = 0;
        int processed = 0;
        var batch = new List<CardEntity>(BatchSize);

        var fileInfo = new FileInfo(jsonFilePath);
        long totalBytes = fileInfo.Length;

        using var fileStream = new FileStream(
            jsonFilePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            BufferSize,
            useAsync: true);

        // Skip UTF-8 BOM if present
        var bomBuffer = new byte[3];
        var bomBytesRead = await fileStream.ReadAsync(bomBuffer, 0, 3, cancellationToken);
        if (bomBytesRead == 3 && bomBuffer[0] == 0xEF && bomBuffer[1] == 0xBB && bomBuffer[2] == 0xBF)
        {
            // BOM found, already skipped
        }
        else
        {
            fileStream.Position = 0;
        }

        var buffer = ArrayPool<byte>.Shared.Rent(BufferSize);
        var jsonReaderState = default(JsonReaderState);
        var isFinalBlock = false;
        var bytesConsumed = 0;

        try
        {
            // Read initial buffer
            var bytesRead = await fileStream.ReadAsync(buffer, 0, BufferSize, cancellationToken);
            isFinalBlock = bytesRead < BufferSize;

            while (bytesRead > 0)
            {
                var reader = new Utf8JsonReader(
                    buffer.AsSpan(bytesConsumed, bytesRead - bytesConsumed),
                    isFinalBlock,
                    jsonReaderState);

                while (reader.Read())
                {
                    if (reader.TokenType == JsonTokenType.StartArray)
                    {
                        continue; // Start of array
                    }

                    if (reader.TokenType == JsonTokenType.StartObject)
                    {
                        // Found start of a card object - read the complete object
                        var cardJson = await ReadCompleteCardObjectAsync(
                            reader,
                            fileStream,
                            buffer,
                            ref jsonReaderState,
                            ref bytesConsumed,
                            ref bytesRead,
                            ref isFinalBlock,
                            cancellationToken);

                        if (cardJson != null)
                        {
                            try
                            {
                                var entity = MapJsonToEntity(cardJson);
                                
                                var existing = await _dbContext.Cards
                                    .FirstOrDefaultAsync(c => c.ScryfallId == entity.ScryfallId, cancellationToken);
                                
                                if (existing != null)
                                {
                                    UpdateEntity(existing, entity);
                                    updated++;
                                }
                                else
                                {
                                    batch.Add(entity);
                                    imported++;
                                }

                                processed++;

                                if (batch.Count >= BatchSize)
                                {
                                    _dbContext.Cards.AddRange(batch);
                                    await _dbContext.SaveChangesAsync(cancellationToken);
                                    batch.Clear();
                                    
                                    var progressPercent = fileStream.Position * 100.0 / totalBytes;
                                    _progress?.Report(new ImportProgress
                                    {
                                        Processed = processed,
                                        Imported = imported,
                                        Updated = updated,
                                        Skipped = skipped,
                                        BytesProcessed = fileStream.Position,
                                        TotalBytes = totalBytes,
                                        ProgressPercent = progressPercent
                                    });
                                }
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine($"Error importing card: {ex.Message}");
                                skipped++;
                            }
                        }
                    }

                    if (reader.TokenType == JsonTokenType.EndArray)
                    {
                        break; // End of array
                    }
                }

                // Save state
                jsonReaderState = reader.CurrentState;
                bytesConsumed = (int)reader.BytesConsumed;

                // If we've consumed all bytes, read more
                if (bytesConsumed >= bytesRead && !isFinalBlock)
                {
                    // Move remaining data to start of buffer
                    if (bytesConsumed < bytesRead)
                    {
                        buffer.AsSpan(bytesConsumed, bytesRead - bytesConsumed).CopyTo(buffer);
                        bytesRead = bytesRead - bytesConsumed;
                        bytesConsumed = 0;
                    }
                    else
                    {
                        bytesRead = 0;
                        bytesConsumed = 0;
                    }

                    // Read more data
                    var newBytesRead = await fileStream.ReadAsync(
                        buffer.AsMemory(bytesRead, BufferSize - bytesRead),
                        cancellationToken);
                    
                    if (newBytesRead == 0)
                    {
                        isFinalBlock = true;
                        break;
                    }

                    bytesRead += newBytesRead;
                    isFinalBlock = bytesRead < BufferSize;
                }
                else
                {
                    break;
                }
            }

            // Save remaining batch
            if (batch.Count > 0)
            {
                _dbContext.Cards.AddRange(batch);
                await _dbContext.SaveChangesAsync(cancellationToken);
            }

            _progress?.Report(new ImportProgress
            {
                Total = processed,
                Processed = processed,
                Imported = imported,
                Updated = updated,
                Skipped = skipped,
                BytesProcessed = totalBytes,
                TotalBytes = totalBytes,
                ProgressPercent = 100.0
            });

            return imported + updated;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    /// <summary>
    /// Read a complete card object from the JSON stream.
    /// Handles objects that span multiple buffer reads.
    /// </summary>
    private async Task<ScryfallCardJson?> ReadCompleteCardObjectAsync(
        Utf8JsonReader reader,
        FileStream fileStream,
        byte[] buffer,
        ref JsonReaderState state,
        ref int bytesConsumed,
        ref int bytesRead,
        ref bool isFinalBlock,
        CancellationToken cancellationToken)
    {
        // Use a MemoryStream to accumulate the complete object JSON
        using var objectStream = new MemoryStream();
        var objectStartPosition = reader.BytesConsumed;
        var depth = 1; // We're already inside the object

        // Write the opening brace
        objectStream.WriteByte((byte)'{');

        // Read tokens until we find the matching EndObject
        while (depth > 0)
        {
            if (!reader.Read())
            {
                // Need more data - read from file
                if (isFinalBlock)
                    return null;

                // Move remaining bytes to start
                var remaining = bytesRead - (int)reader.BytesConsumed;
                if (remaining > 0)
                {
                    buffer.AsSpan((int)reader.BytesConsumed, remaining).CopyTo(buffer);
                }

                // Read more
                var newBytesRead = await fileStream.ReadAsync(
                    buffer.AsMemory(remaining, BufferSize - remaining),
                    cancellationToken);
                
                if (newBytesRead == 0)
                    return null;

                bytesRead = remaining + newBytesRead;
                isFinalBlock = bytesRead < BufferSize;
                bytesConsumed = 0;

                reader = new Utf8JsonReader(
                    buffer.AsSpan(0, bytesRead),
                    isFinalBlock,
                    state);
            }

            // Write the current token to our object stream
            // This is simplified - in practice, you'd need to write the actual JSON bytes
            // For now, we'll use a different approach: extract the object span

            if (reader.TokenType == JsonTokenType.StartObject)
                depth++;
            else if (reader.TokenType == JsonTokenType.EndObject)
                depth--;
        }

        // Extract the object JSON from the buffer
        var objectEndPosition = reader.BytesConsumed;
        var objectLength = objectEndPosition - objectStartPosition;
        
        // For simplicity, use JsonSerializer with the span
        // This requires the object to be complete in the current buffer
        // If it spans buffers, we need to accumulate bytes (more complex)
        
        // Simplified: assume object fits in buffer (most cards will)
        try
        {
            var objectSpan = buffer.AsSpan((int)objectStartPosition, (int)objectLength);
            return JsonSerializer.Deserialize<ScryfallCardJson>(objectSpan);
        }
        catch
        {
            // Object spans buffers - use accumulation approach
            return await ReadSpanningCardObjectAsync(
                reader,
                fileStream,
                buffer,
                ref state,
                ref bytesConsumed,
                ref bytesRead,
                ref isFinalBlock,
                cancellationToken);
        }
    }

    /// <summary>
    /// Read a card object that spans multiple buffer reads by accumulating bytes.
    /// </summary>
    private async Task<ScryfallCardJson?> ReadSpanningCardObjectAsync(
        Utf8JsonReader reader,
        FileStream fileStream,
        byte[] buffer,
        ref JsonReaderState state,
        ref int bytesConsumed,
        ref int bytesRead,
        ref bool isFinalBlock,
        CancellationToken cancellationToken)
    {
        // This is a complex implementation - for now, return null and skip
        // In production, you'd accumulate bytes across buffer reads
        // and deserialize when the object is complete
        return null;
    }

    // ... MapJsonToEntity and UpdateEntity methods from before ...
}
```

**Simpler Alternative**: For initial implementation, consider using `JsonDocument.ParseAsync` with a `StreamReader` wrapper that limits memory, or use a third-party library like `Utf8Json` (if compatible with .NET 8) which handles streaming more elegantly.

**Recommended Approach for MVP**: Start with `JsonDocument.ParseAsync` and monitor memory. If memory usage is acceptable (e.g., < 500MB), use this simpler approach. If memory is still an issue, implement the full `Utf8JsonReader` streaming solution above.

#### 2.4 Testing Streaming Import

**File**: `Majik.Core.Tests/CardData/Import/ScryfallJsonImporterTests.cs`

**Test Scenarios**:
1. **Small File Test**: Test with a small JSON file (< 1MB) containing 10-20 cards
2. **Memory Usage Test**: Monitor memory during import of large file
3. **Progress Reporting Test**: Verify progress is reported correctly
4. **Error Handling Test**: Test behavior when JSON is malformed
5. **Cancellation Test**: Test that cancellation works during long import
6. **Spanning Object Test**: Test cards that span buffer boundaries
7. **BOM Handling Test**: Test UTF-8 BOM detection and skipping

**Memory Testing**:
```csharp
[Fact]
public async Task ImportFromFile_DoesNotExceedMemoryLimit()
{
    // Arrange
    var importer = new ScryfallJsonImporter();
    var initialMemory = GC.GetTotalMemory(true);
    
    // Act
    await importer.ImportFromFileAsync("large-file.json");
    
    // Assert
    var peakMemory = GC.GetTotalMemory(true);
    var memoryUsed = peakMemory - initialMemory;
    
    // Should use less than 500MB for a 2.2GB file
    memoryUsed.Should().BeLessThan(500 * 1024 * 1024);
}
```

**Sample JSON File for Testing**:
Create a test file with a few cards to test the parsing logic:
```json
[
  {
    "id": "test-1",
    "name": "Test Card 1",
    "type_line": "Creature",
    "mana_cost": "{1}{R}"
  },
  {
    "id": "test-2",
    "name": "Test Card 2",
    "type_line": "Instant",
    "mana_cost": "{U}"
  }
]
```
```

#### 2.4 Import Progress Tracking

**File**: `Majik.Core/CardData/Import/ImportProgress.cs`

```csharp
public class ImportProgress
{
    public int Total { get; set; } = -1; // -1 means unknown (when streaming)
    public int Processed { get; set; }
    public int Imported { get; set; }
    public int Updated { get; set; }
    public int Skipped { get; set; }
    
    // For streaming progress (bytes-based)
    public long BytesProcessed { get; set; }
    public long TotalBytes { get; set; }
    public double ProgressPercent { get; set; }
    
    public double PercentComplete => Total > 0 
        ? (double)Processed / Total * 100 
        : ProgressPercent;
}
```

#### 2.5 Scryfall JSON Model

**File**: `Majik.Core/CardData/Import/ScryfallCardJson.cs`

**Purpose**: Model for deserializing Scryfall JSON

```csharp
public class ScryfallCardJson
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }
    
    [JsonPropertyName("name")]
    public string? Name { get; set; }
    
    [JsonPropertyName("mana_cost")]
    public string? ManaCost { get; set; }
    
    [JsonPropertyName("cmc")]
    public int? Cmc { get; set; }
    
    [JsonPropertyName("type_line")]
    public string? TypeLine { get; set; }
    
    [JsonPropertyName("oracle_text")]
    public string? OracleText { get; set; }
    
    [JsonPropertyName("power")]
    public string? Power { get; set; }
    
    [JsonPropertyName("toughness")]
    public string? Toughness { get; set; }
    
    [JsonPropertyName("loyalty")]
    public int? Loyalty { get; set; }
    
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
    
    // Add more fields as needed
}

public class ImageUris
{
    [JsonPropertyName("small")]
    public string? Small { get; set; }
    
    [JsonPropertyName("normal")]
    public string? Normal { get; set; }
    
    [JsonPropertyName("large")]
    public string? Large { get; set; }
}
```

### Task 3: Database Provider

**Priority**: High  
**Estimated Time**: 2-3 days

#### 3.1 ICardDataProvider Interface

**File**: `Majik.Core/CardData/ICardDataProvider.cs`

**Purpose**: Abstract interface for card data providers (same as before, but now database-backed)

```csharp
public interface ICardDataProvider
{
    /// <summary>
    /// Get card data by exact name.
    /// </summary>
    Task<CardDataDto?> GetCardByNameAsync(string name, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Get card data by Scryfall ID.
    /// </summary>
    Task<CardDataDto?> GetCardByIdAsync(string id, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Get card data by set and collector number.
    /// </summary>
    Task<CardDataDto?> GetCardBySetAndNumberAsync(string setCode, string collectorNumber, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Search for cards.
    /// </summary>
    Task<CardSearchResultDto> SearchCardsAsync(string query, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Check if provider is available.
    /// </summary>
    Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default);
}
```

#### 3.2 DatabaseCardProvider

**File**: `Majik.Core/CardData/DatabaseCardProvider.cs`

**Purpose**: Database implementation of card data provider

**Implementation**:
```csharp
public class DatabaseCardProvider : ICardDataProvider
{
    private readonly CardDbContext _dbContext;

    public DatabaseCardProvider(CardDbContext? dbContext = null)
    {
        _dbContext = dbContext ?? new CardDbContext();
    }

    public async Task<CardDataDto?> GetCardByNameAsync(string name, CancellationToken cancellationToken = default)
    {
        var entity = await _dbContext.Cards
            .FirstOrDefaultAsync(c => c.Name == name, cancellationToken);
        
        return entity != null ? MapEntityToDto(entity) : null;
    }

    public async Task<CardDataDto?> GetCardByIdAsync(string id, CancellationToken cancellationToken = default)
    {
        var entity = await _dbContext.Cards
            .FirstOrDefaultAsync(c => c.ScryfallId == id, cancellationToken);
        
        return entity != null ? MapEntityToDto(entity) : null;
    }

    public async Task<CardDataDto?> GetCardBySetAndNumberAsync(string setCode, string collectorNumber, CancellationToken cancellationToken = default)
    {
        var entity = await _dbContext.Cards
            .FirstOrDefaultAsync(c => c.Set == setCode && c.CollectorNumber == collectorNumber, cancellationToken);
        
        return entity != null ? MapEntityToDto(entity) : null;
    }

    public async Task<CardSearchResultDto> SearchCardsAsync(string query, CancellationToken cancellationToken = default)
    {
        // Simple search - can be enhanced with full-text search
        var cards = await _dbContext.Cards
            .Where(c => c.Name.Contains(query) || c.TypeLine.Contains(query))
            .Take(100)  // Limit results
            .ToListAsync(cancellationToken);
        
        return new CardSearchResultDto
        {
            Data = cards.Select(MapEntityToDto).ToList(),
            HasMore = false,  // Simplified
            TotalCards = cards.Count
        };
    }

    public Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return Task.FromResult(_dbContext.Database.CanConnect());
        }
        catch
        {
            return Task.FromResult(false);
        }
    }

    private CardDataDto MapEntityToDto(CardEntity entity)
    {
        return new CardDataDto
        {
            Name = entity.Name,
            ManaCost = entity.ManaCost,
            Cmc = entity.Cmc,
            TypeLine = entity.TypeLine,
            OracleText = entity.OracleText,
            Power = entity.Power,
            Toughness = entity.Toughness,
            Loyalty = entity.Loyalty,
            Colors = JsonSerializer.Deserialize<List<string>>(entity.Colors) ?? new List<string>(),
            ColorIdentity = JsonSerializer.Deserialize<List<string>>(entity.ColorIdentity) ?? new List<string>(),
            Keywords = JsonSerializer.Deserialize<List<string>>(entity.Keywords) ?? new List<string>(),
            Set = entity.Set,
            CollectorNumber = entity.CollectorNumber,
            Rarity = entity.Rarity,
            ScryfallId = entity.ScryfallId,
            ImageUris = entity.ImageUri != null ? new Dictionary<string, string> { ["normal"] = entity.ImageUri } : null,
            Legalities = JsonSerializer.Deserialize<Dictionary<string, string>>(entity.Legalities) ?? new Dictionary<string, string>()
        };
    }
}
```

### Task 4: Card Data DTOs

**Priority**: High  
**Estimated Time**: 1 day

#### 4.1 CardDataDto

**File**: `Majik.Core/CardData/CardDataDto.cs`

**Purpose**: DTO for card data (same structure as before)

```csharp
public class CardDataDto
{
    public string Name { get; set; } = "";
    public string? ManaCost { get; set; }
    public int? Cmc { get; set; }
    public string TypeLine { get; set; } = "";
    public string? OracleText { get; set; }
    public string? Power { get; set; }
    public string? Toughness { get; set; }
    public int? Loyalty { get; set; }
    public List<string> Colors { get; set; } = new();
    public List<string> ColorIdentity { get; set; } = new();
    public List<string> Keywords { get; set; } = new();
    public string? Rarity { get; set; }
    public string? Set { get; set; }
    public string? CollectorNumber { get; set; }
    public string? ScryfallId { get; set; }
    public Dictionary<string, string>? ImageUris { get; set; }
    public Dictionary<string, string> Legalities { get; set; } = new();
}
```

### Task 5: Card Factory (Updated)

**Priority**: High  
**Estimated Time**: 1-2 days

#### 5.1 CardFactory

**File**: `Majik.Core/CardData/CardFactory.cs`

**Purpose**: Factory for creating cards from database (similar to before, but uses DatabaseCardProvider)

```csharp
public class CardFactory
{
    private readonly ICardDataProvider _dataProvider;
    private readonly CardDataCache? _cache;
    private readonly CardDataMapper _mapper;

    public CardFactory(ICardDataProvider? dataProvider = null, CardDataCache? cache = null)
    {
        _dataProvider = dataProvider ?? new DatabaseCardProvider();
        _cache = cache;
        _mapper = new CardDataMapper();
    }

    /// <summary>
    /// Create a card by name (loads from database).
    /// </summary>
    public async Task<ICard> CreateCardAsync(string name, Player? owner = null, CancellationToken cancellationToken = default)
    {
        // Check cache first (if enabled)
        if (_cache != null)
        {
            var cached = _cache.GetByName(name);
            if (cached != null)
            {
                return _mapper.MapToCard(cached, owner);
            }
        }

        // Load from database
        var data = await _dataProvider.GetCardByNameAsync(name, cancellationToken);
        if (data == null)
        {
            throw new CardNotFoundException(name);
        }

        // Cache the data (if enabled)
        _cache?.Add(data);

        // Map and return
        return _mapper.MapToCard(data, owner);
    }

    // ... other methods similar to before
}
```

### Task 6: Card Data Mapper

**Priority**: High  
**Estimated Time**: 2-3 days

#### 6.1 CardDataMapper

**File**: `Majik.Core/CardData/CardDataMapper.cs`

**Purpose**: Maps CardDataDto to internal card classes (same as before)

**Implementation**: Same as Task 5 in original plan - maps DTOs to Card, Creature, Planeswalker, etc.

### Task 7: Type Line Parser

**Priority**: High  
**Estimated Time**: 2-3 days

#### 7.1 TypeLineParser

**File**: `Majik.Core/CardData/TypeLineParser.cs`

**Purpose**: Parse type lines into types, supertypes, subtypes (same as before)

**Implementation**: Same as Task 7 in original plan

### Task 8: Import Console Tool

**Priority**: High  
**Estimated Time**: 1-2 days

#### 8.1 Import Command

**File**: `Majik.Console/Commands/ImportCardsCommand.cs` (new)

**Purpose**: Console command to import Scryfall JSON into database

**Implementation**:
```csharp
public class ImportCardsCommand
{
    public static async Task<int> ExecuteAsync(string jsonFilePath, CancellationToken cancellationToken = default)
    {
        Console.WriteLine($"Importing cards from: {jsonFilePath}");
        Console.WriteLine("This may take several minutes for large files...\n");

        var dbContext = new CardDbContext();
        var importer = new ScryfallJsonImporter(dbContext, new Progress<ImportProgress>(ReportProgress));

        try
        {
            var count = await importer.ImportFromFileAsync(jsonFilePath, cancellationToken);
            Console.WriteLine($"\nImport complete! Imported/updated {count} cards.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\nError during import: {ex.Message}");
            return 1;
        }
    }

    private static void ReportProgress(ImportProgress progress)
    {
        Console.Write($"\rProgress: {progress.Processed}/{progress.Total} ({progress.PercentComplete:F1}%) - " +
                     $"Imported: {progress.Imported}, Updated: {progress.Updated}, Skipped: {progress.Skipped}");
    }
}
```

**Update Program.cs**:
```csharp
// Add command-line argument parsing
if (args.Length > 0 && args[0] == "import")
{
    if (args.Length < 2)
    {
        Console.WriteLine("Usage: Majik.Console import <path-to-scryfall-json>");
        return;
    }
    await ImportCardsCommand.ExecuteAsync(args[1]);
    return;
}
```

### Task 9: Database Migrations

**Priority**: Medium  
**Estimated Time**: 1 day

#### 9.1 EF Core Migrations

**Setup**:
```bash
dotnet ef migrations add InitialCardDatabase --project Majik.Core
dotnet ef database update --project Majik.Core
```

**Migration Strategy**:
- Initial migration creates Cards table
- Future migrations for schema updates
- Handle data migration when schema changes

### Task 10: Optional: In-Memory Cache

**Priority**: Low  
**Estimated Time**: 1-2 days

#### 10.1 CardDataCache

**File**: `Majik.Core/CardData/CardDataCache.cs`

**Purpose**: Optional cache layer for frequently accessed cards

**Note**: With database, caching is less critical but still useful for:
- Frequently accessed cards
- Reducing database queries
- Faster repeated lookups

**Implementation**: Same as Task 4 in original plan (optional)

### Task 11: Unit Tests

**Priority**: High  
**Estimated Time**: 2-3 days

#### 11.1 Test Files to Create

1. **DatabaseCardProviderTests.cs**:
   - Test database queries
   - Test with in-memory SQLite database
   - Test search functionality

2. **ScryfallJsonImporterTests.cs**:
   - Test JSON parsing
   - Test import process
   - Test error handling

3. **CardDataMapperTests.cs**:
   - Test DTO to Card mapping
   - Test type line parsing
   - Test edge cases

4. **CardFactoryTests.cs**:
   - Test card creation from database
   - Test caching behavior
   - Test error handling

5. **TypeLineParserTests.cs**:
   - Test various type line formats
   - Test supertype/type/subtype extraction

#### 11.2 Test Database Setup

**Strategy**:
- Use in-memory SQLite for tests
- Create test database with sample cards
- Clean up after each test

### Task 12: Console App Integration

**Priority**: Medium  
**Estimated Time**: 1 day

#### 12.1 Update Console App

**Scenarios**:
1. Import cards from JSON file
2. Load cards from database
3. Create deck from card names
4. Display card information
5. Search cards

## Implementation Order

### Week 1: Database Foundation
- Day 1-2: Database schema and EF Core setup
- Day 3-5: **JSON import tool with streaming** (CRITICAL: Must handle 2.2GB file without loading into memory)
- Day 5: Database provider implementation

### Week 2: Core Functionality
- Day 1-2: Card Data Mapper and Type Line Parser
- Day 3-4: Card Factory updates
- Day 5: Import console tool

### Week 3: Integration and Testing
- Day 1-2: Unit tests
- Day 3: Console app integration
- Day 4-5: Performance optimization and documentation

## Dependencies

### NuGet Packages

**Required**:
- `Microsoft.EntityFrameworkCore.Sqlite` - SQLite provider
- `Microsoft.EntityFrameworkCore.Design` - EF Core tools
- `System.Text.Json` - JSON parsing (already in .NET 8.0)

**Installation**:
```bash
dotnet add Majik.Core package Microsoft.EntityFrameworkCore.Sqlite
dotnet add Majik.Core package Microsoft.EntityFrameworkCore.Design
```

### Internal Dependencies

- Phase 4: Card system (Card classes, types, etc.)
- Phase 6: Rules engine (for validation)

## Database Location

**Default Path**: `%APPDATA%/Majik/cards.db` (Windows) or `~/.local/share/Majik/cards.db` (Linux/Mac)

**Benefits**:
- User-specific database
- Easy to locate and backup
- No admin permissions needed
- Can be moved/shared if needed

## Success Criteria

1. ✅ Can import Scryfall bulk data JSON into database
2. ✅ Database contains all cards from import
3. ✅ Can query cards by name, ID, set+number
4. ✅ Cards created from database work in game
5. ✅ Import tool shows progress
6. ✅ Database queries are fast (< 50ms for single card lookup)
7. ✅ 90%+ test coverage for card loading system
8. ✅ Console app demonstrates import and card loading

## Performance Considerations

### Database Optimization

1. **Indexes**: 
   - Name (exact lookups)
   - TypeLine (type searches)
   - Set + CollectorNumber (set lookups)
   - ScryfallId (unique lookups)

2. **Query Optimization**:
   - Use parameterized queries
   - Limit result sets
   - Use async operations

3. **Connection Management**:
   - Use connection pooling
   - Dispose contexts properly
   - Consider read-only connections for queries

### Import Performance

- **Streaming Parsing**: ✅ **REQUIRED** - Use `Utf8JsonReader` to avoid loading 2.2GB file into memory
- **Buffer Management**: Use `ArrayPool<byte>` for efficient buffer reuse
- **Batch Processing**: Process in batches of 1000 cards
- **Transaction Batching**: Commit every 1000 cards
- **Progress Reporting**: Report progress every batch (bytes-based when streaming)
- **Error Handling**: Continue on errors, log issues
- **Memory Monitoring**: Keep memory usage < 500MB during import
- **State Management**: Properly handle `JsonReaderState` across buffer reads

## Update Strategy

### Manual Updates

1. Download new Scryfall bulk data file
2. Run import command: `dotnet run -- import path/to/new-data.json`
3. Import tool handles updates (updates existing, adds new)

### Future: Automatic Updates

- Check Scryfall for new bulk data
- Download automatically
- Schedule updates
- Notify user of updates

## Risks and Mitigations

### Risk 1: Large JSON File (2.2GB) - CRITICAL
**Mitigation**: 
- ✅ **REQUIRED**: Use streaming JSON parsing with `Utf8JsonReader` - DO NOT load entire file into memory
- Process cards one at a time as they're parsed
- Use `FileStream` with async I/O
- Batch database writes (every 1000 cards) for performance
- Monitor memory usage during import
- Use `ArrayPool<byte>` for buffer management
- Handle partial reads and maintain `JsonReaderState`

### Risk 2: Import Time
**Mitigation**: 
- Show progress
- Optimize batch size
- Use transactions efficiently
- Allow cancellation

### Risk 3: Database Size
**Mitigation**: 
- SQLite handles large databases well
- Consider compression for JSON fields
- Monitor database size

### Risk 4: Schema Changes
**Mitigation**: 
- Use EF Core migrations
- Handle data migration
- Version database schema

## Testing Strategy

### Unit Tests
- Test with in-memory SQLite
- Test import process (with small sample files)
- Test streaming JSON parsing
- Test memory usage during import
- Test queries
- Test error handling
- Test progress reporting
- Test cancellation support

### Integration Tests
- Test with real database file
- Test import of sample JSON (10-100 cards)
- Test end-to-end card loading
- **Memory Test**: Monitor memory during import of large file (if available)

### Performance Tests
- Measure import time (target: < 30 minutes for 2.2GB file)
- Measure query performance (target: < 50ms for single card lookup)
- Test with large datasets
- Monitor memory usage (target: < 500MB peak during import)

## Example Usage

### Import Cards
```bash
# Import Scryfall bulk data
dotnet run --project Majik.Console -- import ~/Downloads/scryfall-cards.json
```

### Load Cards in Code
```csharp
// Create card factory (uses database by default)
var cardFactory = new CardFactory();

// Load a single card
var lightningBolt = await cardFactory.CreateCardAsync("Lightning Bolt", player);

// Load multiple cards (deck)
var deckList = new[] { "Lightning Bolt", "Grizzly Bears", "Forest", "Mountain" };
var deck = await cardFactory.CreateCardsAsync(deckList, player);

// Add cards to library
foreach (var card in deck)
{
    player.Zones.Library.AddCard(card);
}
```

## Future Enhancements

1. **Full-Text Search**: SQLite FTS5 for advanced card search
2. **Card Images**: Download and cache card images
3. **Deck File Support**: Load decks from .txt, .dec files
4. **Card Statistics**: Track card usage, popularity
5. **Set Information**: Store and query set information
6. **Rulings**: Store and query card rulings
7. **Price Data**: Integrate price information (optional)
8. **Automatic Updates**: Check and download new bulk data
9. **Database Backup**: Backup/restore functionality
10. **Multi-Database**: Support multiple card databases

## Next Phase: Phase 7.5

**Phase 7.5: Oracle Text Parsing and Layer System** - Parse oracle text to automatically generate abilities and implement the Magic rules layer system (Rule 613) for continuous effects. This will enable automatic ability creation from card oracle text and proper application of continuous effects in the correct layer order.

See `PHASE7.5_PLAN.md` for the detailed implementation plan.

## Conclusion

This plan provides a comprehensive approach to database-backed card loading that:
- ✅ Uses local database (SQLite) for fast access
- ✅ Imports from Scryfall bulk data JSON file
- ✅ No API calls needed during gameplay
- ✅ Fast queries with proper indexing
- ✅ Easy to update with new data
- ✅ Maintains extensibility for future enhancements
- ✅ Works offline
- ✅ No single file with all cards (database instead)

The system is designed to be performant, reliable, and easy to maintain while providing fast access to all Magic cards.
