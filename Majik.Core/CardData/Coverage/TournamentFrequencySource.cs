using System.Text.Json;
using System.Text.Json.Serialization;

namespace Majik.Core.CardData.Coverage;

/// <summary>
/// Loads a tournament-frequency snapshot (a JSON file under <c>docs/</c>)
/// into a card-name → weight map for use by
/// <see cref="CoverageReportV2.Build"/>.
///
/// Snapshot shape (see <c>docs/meta-modern-snapshot.json</c>):
/// <code>
/// {
///   "format": "modern",
///   "snapshot_date": "2026-05-24",
///   "cards": [
///     { "name": "Lightning Bolt", "decks": 300, "play_rate_pct": 30.0 },
///     ...
///   ]
/// }
/// </code>
///
/// The chosen weight is <c>play_rate_pct</c> when present, else
/// <c>decks</c>, else 1. Names not in the snapshot get weight 0 — they
/// don't show up in tournaments, so they shouldn't move the headline
/// number. Pure data: no I/O outside the one <see cref="File.ReadAllText(string)"/>.
/// </summary>
public static class TournamentFrequencySource
{
    /// <summary>
    /// Read <paramref name="snapshotPath"/> and return a name → weight map.
    /// Weight units are play-rate-percent × 10 (so 30.0% → 300) when
    /// <c>play_rate_pct</c> is set, which keeps weighted-coverage in the
    /// same numeric scale across decks vs play-rate sources. Missing /
    /// duplicate names are deterministically resolved (max weight wins).
    /// Throws <see cref="FileNotFoundException"/> if the file is missing
    /// and <see cref="InvalidDataException"/> if the JSON is malformed.
    /// </summary>
    public static IDictionary<string, double> LoadFromSnapshot(string snapshotPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(snapshotPath);
        if (!File.Exists(snapshotPath))
        {
            throw new FileNotFoundException(
                $"Tournament-frequency snapshot not found: {snapshotPath}",
                snapshotPath);
        }

        var json = File.ReadAllText(snapshotPath);
        return ParseSnapshot(json);
    }

    /// <summary>
    /// Pure parse — exposed for tests / fixtures so they don't need a
    /// file on disk.
    /// </summary>
    public static IDictionary<string, double> ParseSnapshot(string json)
    {
        ArgumentNullException.ThrowIfNull(json);

        SnapshotDto? dto;
        try
        {
            dto = JsonSerializer.Deserialize<SnapshotDto>(json, JsonOpts);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException(
                "Tournament-frequency snapshot JSON is malformed.", ex);
        }

        if (dto?.Cards is null)
        {
            throw new InvalidDataException(
                "Tournament-frequency snapshot is missing the 'cards' array.");
        }

        var map = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var row in dto.Cards)
        {
            if (string.IsNullOrWhiteSpace(row.Name)) continue;
            // Prefer play_rate_pct (scaled ×10 to keep weights integer-ish),
            // fall back to raw deck count, finally 1.
            double w;
            if (row.PlayRatePct.HasValue) w = row.PlayRatePct.Value * 10.0;
            else if (row.Decks.HasValue)  w = row.Decks.Value;
            else                          w = 1.0;
            if (w <= 0) continue;

            // Dedup: take max if the snapshot lists the same name twice.
            if (!map.TryGetValue(row.Name, out var existing) || w > existing)
            {
                map[row.Name] = w;
            }
        }
        return map;
    }

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>Internal DTO mirroring the on-disk snapshot schema.</summary>
    private sealed class SnapshotDto
    {
        [JsonPropertyName("format")]      public string? Format { get; set; }
        [JsonPropertyName("snapshot_date")] public string? SnapshotDate { get; set; }
        [JsonPropertyName("cards")]       public List<CardDto>? Cards { get; set; }
    }

    private sealed class CardDto
    {
        [JsonPropertyName("name")]          public string? Name { get; set; }
        [JsonPropertyName("decks")]         public double? Decks { get; set; }
        [JsonPropertyName("play_rate_pct")] public double? PlayRatePct { get; set; }
    }
}
