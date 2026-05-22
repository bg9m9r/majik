using System.Text.Json;
using System.Text.Json.Serialization;
using Majik.Core.CardData.Database;
using Microsoft.EntityFrameworkCore;

namespace Majik.Core.CardData.Import;

/// <summary>
/// Imports Scryfall bulk data JSON into the database using streaming to avoid loading the entire file into memory.
/// Uses Utf8JsonReader for true streaming of large files.
/// </summary>
public class ScryfallJsonImporter
{
    private readonly CardDbContext _dbContext;
    private readonly IProgress<ImportProgress>? _progress;
    private const int BufferSize = 65536; // 64KB buffer
    private const int BatchSize = 1000; // Cards to batch before database write

    public ScryfallJsonImporter(CardDbContext? dbContext = null, IProgress<ImportProgress>? progress = null)
    {
        _dbContext = dbContext ?? new CardDbContext();
        _progress = progress;
    }

    /// <summary>
    /// Stream import cards from Scryfall bulk data JSON file using JsonSerializer.DeserializeAsyncEnumerable.
    /// This method processes the file incrementally without loading it entirely into memory.
    /// Uses .NET 8's built-in streaming JSON deserialization for optimal performance.
    /// </summary>
    public async Task<int> ImportFromFileAsync(string jsonFilePath, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(jsonFilePath))
        {
            throw new FileNotFoundException($"JSON file not found: {jsonFilePath}");
        }

        // Ensure database is created
        await _dbContext.Database.EnsureCreatedAsync(cancellationToken);

        // Patch schema to add IsImplemented column to Cards table
        try
        {
            await CardDataSchemaPatcher.PatchAsync(_dbContext.Database.GetDbConnection(), cancellationToken);
        }
        catch (Exception patchEx)
        {
            // Log but don't crash on patch failure — the column might already exist
            System.Console.WriteLine($"Note: Card schema patch did not complete: {patchEx.Message}");
        }

        var fileInfo = new FileInfo(jsonFilePath);
        long totalBytes = fileInfo.Length;

        // Use async file stream for DeserializeAsyncEnumerable
        await using var fileStream = new FileStream(
            jsonFilePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            BufferSize,
            useAsync: true);

        // Configure JSON options
        var jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = false,
            AllowTrailingCommas = false,
            ReadCommentHandling = JsonCommentHandling.Skip
        };

        int imported = 0;
        int updated = 0;
        int skipped = 0;
        int processed = 0;
        var batch = new List<CardEntity>(BatchSize);

        // Stream deserialize the JSON array - this handles large files efficiently
        await foreach (var cardJson in JsonSerializer.DeserializeAsyncEnumerable<ScryfallCardJson>(
            fileStream, 
            jsonOptions, 
            cancellationToken))
        {
            if (cardJson == null)
            {
                skipped++;
                continue;
            }

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
                // Log error but continue - skip problematic cards
                if (processed % 1000 == 0 || ex is JsonException)
                {
                    Console.WriteLine($"Error importing card (skipping): {ex.Message}");
                }
                skipped++;
            }
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
            TotalBytes = totalBytes,
            ProgressPercent = 100.0
        });

        return imported + updated;
    }

    /// <summary>
    /// Maps Scryfall JSON card data to database entity.
    /// </summary>
    private CardEntity MapJsonToEntity(ScryfallCardJson json)
    {
        var entity = new CardEntity
        {
            ScryfallId = json.Id ?? Guid.NewGuid().ToString(),
            Name = json.Name ?? "",
            ManaCost = json.ManaCost,
            Cmc = json.Cmc.HasValue ? (int?)Math.Round(json.Cmc.Value) : null,
            TypeLine = json.TypeLine ?? "",
            OracleText = json.OracleText,
            Power = json.Power,
            Toughness = json.Toughness,
            Loyalty = json.Loyalty.HasValue ? (int?)Math.Round(json.Loyalty.Value) : null,
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
        entity.FormatLegalities = BuildLegalityRows(json.Legalities);
        return entity;
    }

    /// <summary>
    /// Materializes the per-format <see cref="CardLegalityEntity"/> rows from
    /// the Scryfall <c>legalities</c> dict. <see cref="CardLegalityEntity.CardId"/>
    /// is left at 0 so EF Core fills it from the parent on insert.
    /// </summary>
    private static List<CardLegalityEntity> BuildLegalityRows(Dictionary<string, string>? legalities)
    {
        if (legalities is null || legalities.Count == 0) return new();
        var rows = new List<CardLegalityEntity>(legalities.Count);
        foreach (var kv in legalities)
        {
            if (string.IsNullOrEmpty(kv.Key) || string.IsNullOrEmpty(kv.Value)) continue;
            rows.Add(new CardLegalityEntity { Format = kv.Key, Status = kv.Value });
        }
        return rows;
    }

    /// <summary>
    /// Updates an existing entity with new data.
    /// </summary>
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
        // Replace per-format rows so formats that disappeared from Scryfall
        // don't linger on the existing entity.
        existing.FormatLegalities.Clear();
        foreach (var row in updated.FormatLegalities)
        {
            existing.FormatLegalities.Add(new CardLegalityEntity
            {
                Format = row.Format,
                Status = row.Status,
            });
        }
    }
}
