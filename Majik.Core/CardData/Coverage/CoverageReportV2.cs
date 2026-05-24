using System.Text.Json.Serialization;
using Majik.Core.CardData.Database;

namespace Majik.Core.CardData.Coverage;

/// <summary>
/// Aggregate coverage snapshot across a card pool. One row per input
/// <see cref="CardEntity"/> (after dedup-by-name when requested by the
/// caller) plus per-tier rollups.
///
/// Pure data — no I/O, no mutation of engine state. Built by
/// <see cref="CoverageReportV2.Build"/>; consumed by the
/// <c>coverage</c> console subcommand for console / JSON / markdown output.
/// </summary>
public sealed record CoverageReportV2(
    string Scope,
    int TotalCards,
    IReadOnlyDictionary<CoverageTier, int> CountsByTier,
    IReadOnlyDictionary<CoverageTier, int> WeightedByTier,
    int TotalWeight,
    IReadOnlyList<CoveragePerCardRow> PerCard,
    IReadOnlyList<CoverageUnimplementedRow> TopUnimplemented,
    IReadOnlyDictionary<CoverageTier, double>? FrequencyWeightedByTier = null,
    double FrequencyTotalWeight = 0.0,
    IReadOnlyList<CoverageTopMetaRow>? TopMeta = null)
{
    /// <summary>
    /// Classify every entity in <paramref name="entities"/>. When
    /// <paramref name="weights"/> is non-null, multiplies each card's
    /// contribution to the weighted rollups by the supplied weight
    /// (decklist-mode: number of copies in the list). Counts-by-tier
    /// stays one-per-card regardless of weights.
    ///
    /// <paramref name="frequencyWeights"/> is an optional second weight
    /// map (e.g. tournament play-rate %); when supplied the report also
    /// populates <see cref="FrequencyWeightedByTier"/> and the
    /// <see cref="TopMeta"/> headline. Names absent from the map get
    /// weight 0 (long-tail cards don't count toward the headline %).
    /// </summary>
    public static CoverageReportV2 Build(
        string scope,
        IEnumerable<CardEntity> entities,
        CoverageClassifier classifier,
        IReadOnlyDictionary<string, int>? weights = null,
        int topUnimplemented = 20,
        IReadOnlyDictionary<string, double>? frequencyWeights = null,
        int topMeta = 20)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(entities);
        ArgumentNullException.ThrowIfNull(classifier);

        var counts = new Dictionary<CoverageTier, int>();
        var weighted = new Dictionary<CoverageTier, int>();
        var freqWeighted = new Dictionary<CoverageTier, double>();
        foreach (CoverageTier t in Enum.GetValues<CoverageTier>())
        {
            counts[t] = 0;
            weighted[t] = 0;
            freqWeighted[t] = 0.0;
        }

        var rows = new List<CoveragePerCardRow>();
        var unimplementedWeights = new Dictionary<string, int>(StringComparer.Ordinal);
        var perCardFreq = new Dictionary<string, (double Weight, CoverageTier Tier)>(StringComparer.Ordinal);
        int totalWeight = 0;
        double totalFreqWeight = 0.0;

        foreach (var entity in entities)
        {
            var tier = classifier.Classify(entity);
            counts[tier]++;
            var w = weights is null ? 1 : weights.GetValueOrDefault(entity.Name, 0);
            if (w == 0 && weights is null) w = 1;
            weighted[tier] += w;
            totalWeight += w;

            if (frequencyWeights is not null
                && frequencyWeights.TryGetValue(entity.Name, out var fw)
                && fw > 0)
            {
                freqWeighted[tier] += fw;
                totalFreqWeight += fw;
                perCardFreq[entity.Name] = (fw, tier);
            }

            rows.Add(new CoveragePerCardRow(entity.Name, entity.TypeLine ?? "", tier));
            if (tier == CoverageTier.Unimplemented)
            {
                unimplementedWeights[entity.Name] =
                    unimplementedWeights.GetValueOrDefault(entity.Name) + w;
            }
        }

        var topUnimpl = unimplementedWeights
            .OrderByDescending(kv => kv.Value)
            .ThenBy(kv => kv.Key, StringComparer.Ordinal)
            .Take(topUnimplemented)
            .Select(kv => new CoverageUnimplementedRow(kv.Key, kv.Value))
            .ToList();

        IReadOnlyList<CoverageTopMetaRow>? topMetaRows = null;
        if (frequencyWeights is not null)
        {
            topMetaRows = perCardFreq
                .OrderByDescending(kv => kv.Value.Weight)
                .ThenBy(kv => kv.Key, StringComparer.Ordinal)
                .Take(topMeta)
                .Select(kv => new CoverageTopMetaRow(kv.Key, kv.Value.Weight, kv.Value.Tier))
                .ToList();
        }

        return new CoverageReportV2(
            scope,
            rows.Count,
            counts,
            weighted,
            totalWeight,
            rows,
            topUnimpl,
            frequencyWeights is null ? null : freqWeighted,
            totalFreqWeight,
            topMetaRows);
    }

    /// <summary>"Covered" = anything other than Unimplemented.</summary>
    [JsonIgnore]
    public int CoveredCards =>
        TotalCards - CountsByTier.GetValueOrDefault(CoverageTier.Unimplemented);

    /// <summary>Covered % (cards). Returns 0 when the pool is empty.</summary>
    [JsonIgnore]
    public double CoveredPercent =>
        TotalCards == 0 ? 0.0 : 100.0 * CoveredCards / TotalCards;

    [JsonIgnore]
    public int WeightedCovered =>
        TotalWeight - WeightedByTier.GetValueOrDefault(CoverageTier.Unimplemented);

    [JsonIgnore]
    public double WeightedCoveredPercent =>
        TotalWeight == 0 ? 0.0 : 100.0 * WeightedCovered / TotalWeight;

    /// <summary>
    /// Tournament-frequency-weighted "covered" total — sum of frequency
    /// weights across every non-Unimplemented tier. 0 when no frequency
    /// snapshot was supplied.
    /// </summary>
    [JsonIgnore]
    public double FrequencyWeightedCovered =>
        FrequencyWeightedByTier is null
            ? 0.0
            : FrequencyTotalWeight
              - FrequencyWeightedByTier.GetValueOrDefault(CoverageTier.Unimplemented);

    /// <summary>
    /// Tournament-frequency-weighted covered %. 0 when no snapshot was
    /// supplied or the matched-weight total is 0 (no overlap between
    /// the pool and the snapshot).
    /// </summary>
    [JsonIgnore]
    public double FrequencyWeightedCoveredPercent =>
        FrequencyTotalWeight <= 0.0
            ? 0.0
            : 100.0 * FrequencyWeightedCovered / FrequencyTotalWeight;

    /// <summary>How many of the top-N most-played cards are covered.</summary>
    [JsonIgnore]
    public int TopMetaCovered =>
        TopMeta is null ? 0 : TopMeta.Count(r => r.Tier != CoverageTier.Unimplemented);

    /// <summary>Total entries in the top-N most-played slice.</summary>
    [JsonIgnore]
    public int TopMetaTotal => TopMeta?.Count ?? 0;
}

/// <summary>
/// One row in the "top-N most-played" headline. <see cref="Weight"/> is the
/// raw frequency weight (e.g. play-rate% × 10) from the snapshot.
/// </summary>
public sealed record CoverageTopMetaRow(string Name, double Weight, CoverageTier Tier);

/// <summary>Per-card classification row for JSON output.</summary>
public sealed record CoveragePerCardRow(string Name, string TypeLine, CoverageTier Tier);

/// <summary>
/// One row in the "top unimplemented" rollup. <see cref="Weight"/> is the
/// copy count (decklist mode) or simply 1 (engine-wide / format mode).
/// </summary>
public sealed record CoverageUnimplementedRow(string Name, int Weight);
