using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
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
    IReadOnlyList<CoverageTopMetaRow>? TopMeta = null,
    IReadOnlyList<CoverageNotInSetRow>? NotInSet = null,
    double NotInSetWeight = 0.0)
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

        // Materialize once so we can do two passes: one to classify every
        // entity, then a second to match snapshot keys against the
        // classified set (exact-name first, front-face fallback second).
        // This guarantees snapshot weight goes to the entity that best
        // represents the printed card and doesn't get stolen by a DFC
        // mirror row that happens to iterate first.
        var entityList = entities as IList<CardEntity> ?? entities.ToList();

        var rows = new List<CoveragePerCardRow>();
        var unimplementedWeights = new Dictionary<string, int>(StringComparer.Ordinal);
        var tierByEntityIndex = new CoverageTier[entityList.Count];
        int totalWeight = 0;

        for (int i = 0; i < entityList.Count; i++)
        {
            var entity = entityList[i];
            var tier = classifier.Classify(entity);
            tierByEntityIndex[i] = tier;
            counts[tier]++;
            var w = weights is null ? 1 : weights.GetValueOrDefault(entity.Name, 0);
            if (w == 0 && weights is null) w = 1;
            weighted[tier] += w;
            totalWeight += w;

            rows.Add(new CoveragePerCardRow(entity.Name, entity.TypeLine ?? "", tier));
            if (tier == CoverageTier.Unimplemented)
            {
                unimplementedWeights[entity.Name] =
                    unimplementedWeights.GetValueOrDefault(entity.Name) + w;
            }
        }

        var perCardFreq = new Dictionary<string, (double Weight, CoverageTier Tier)>(StringComparer.Ordinal);
        var matchedSnapshotNames = new HashSet<string>(StringComparer.Ordinal);
        double totalFreqWeight = 0.0;

        if (frequencyWeights is not null)
        {
            // Build entity indices keyed by exact name + front-face.
            // Within each bucket, prefer the better (lower-numbered) tier
            // so a NamedFactory row outranks an Unimplemented mirror.
            int CompareCandidates(int a, int b) =>
                ((int)tierByEntityIndex[a]).CompareTo((int)tierByEntityIndex[b]);

            var byExactName = new Dictionary<string, int>(StringComparer.Ordinal);
            var byFrontFace = new Dictionary<string, int>(StringComparer.Ordinal);
            var byExactNameIc = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var byFrontFaceIc = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < entityList.Count; i++)
            {
                var name = entityList[i].Name;
                if (string.IsNullOrEmpty(name)) continue;
                if (!byExactName.TryGetValue(name, out var existing) || CompareCandidates(i, existing) < 0)
                {
                    byExactName[name] = i;
                }
                var front = FrontFace(name);
                if (!byFrontFace.TryGetValue(front, out var existingFront) || CompareCandidates(i, existingFront) < 0)
                {
                    byFrontFace[front] = i;
                }
                if (!byExactNameIc.TryGetValue(name, out var existingIc) || CompareCandidates(i, existingIc) < 0)
                {
                    byExactNameIc[name] = i;
                }
                if (!byFrontFaceIc.TryGetValue(front, out var existingFrontIc) || CompareCandidates(i, existingFrontIc) < 0)
                {
                    byFrontFaceIc[front] = i;
                }
            }

            // Pass 1 — exact-name matches per snapshot key (ordinal).
            foreach (var kv in frequencyWeights)
            {
                if (kv.Value <= 0 || string.IsNullOrWhiteSpace(kv.Key)) continue;
                if (byExactName.TryGetValue(kv.Key, out var idx))
                {
                    AttributeFrequency(kv.Key, kv.Value, entityList[idx].Name, tierByEntityIndex[idx]);
                }
            }

            // Pass 2 — front-face fallback for snapshot keys still unmatched.
            foreach (var kv in frequencyWeights)
            {
                if (kv.Value <= 0 || string.IsNullOrWhiteSpace(kv.Key)) continue;
                if (matchedSnapshotNames.Contains(kv.Key)) continue;
                if (byFrontFace.TryGetValue(kv.Key, out var idx))
                {
                    AttributeFrequency(kv.Key, kv.Value, entityList[idx].Name, tierByEntityIndex[idx]);
                }
            }

            // Pass 3 — case-insensitive fallback (handles casing drift in
            // hand-maintained snapshots / older imports).
            foreach (var kv in frequencyWeights)
            {
                if (kv.Value <= 0 || string.IsNullOrWhiteSpace(kv.Key)) continue;
                if (matchedSnapshotNames.Contains(kv.Key)) continue;
                if (byExactNameIc.TryGetValue(kv.Key, out var idx)
                    || byFrontFaceIc.TryGetValue(kv.Key, out idx))
                {
                    AttributeFrequency(kv.Key, kv.Value, entityList[idx].Name, tierByEntityIndex[idx]);
                }
            }

            void AttributeFrequency(string snapshotKey, double w, string entityName, CoverageTier tier)
            {
                if (!matchedSnapshotNames.Add(snapshotKey)) return;
                freqWeighted[tier] += w;
                totalFreqWeight += w;
                // Prefer to key perCardFreq by snapshot name (matches what
                // tournament reports surface to the user).
                perCardFreq[snapshotKey] = (w, tier);
            }
        }

        var topUnimpl = unimplementedWeights
            .OrderByDescending(kv => kv.Value)
            .ThenBy(kv => kv.Key, StringComparer.Ordinal)
            .Take(topUnimplemented)
            .Select(kv => new CoverageUnimplementedRow(kv.Key, kv.Value))
            .ToList();

        IReadOnlyList<CoverageTopMetaRow>? topMetaRows = null;
        IReadOnlyList<CoverageNotInSetRow>? notInSetRows = null;
        double notInSetWeight = 0.0;
        if (frequencyWeights is not null)
        {
            topMetaRows = perCardFreq
                .OrderByDescending(kv => kv.Value.Weight)
                .ThenBy(kv => kv.Key, StringComparer.Ordinal)
                .Take(topMeta)
                .Select(kv => new CoverageTopMetaRow(kv.Key, kv.Value.Weight, kv.Value.Tier))
                .ToList();

            // NotInSet — snapshot entries that never matched a classified
            // entity. Caller can use the count/weight to diagnose missing
            // factory output or format-filter exclusions (banned cards,
            // double-faced naming mismatches, etc.).
            var notInSet = new List<CoverageNotInSetRow>();
            foreach (var kv in frequencyWeights)
            {
                if (kv.Value <= 0) continue;
                if (matchedSnapshotNames.Contains(kv.Key)) continue;
                notInSet.Add(new CoverageNotInSetRow(kv.Key, kv.Value));
                notInSetWeight += kv.Value;
            }
            notInSetRows = notInSet
                .OrderByDescending(r => r.Weight)
                .ThenBy(r => r.Name, StringComparer.Ordinal)
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
            topMetaRows,
            notInSetRows,
            notInSetWeight);
    }

    /// <summary>
    /// Match an entity name against the frequency-snapshot map, trying:
    /// (1) exact ordinal hit, (2) front-face of a DFC / adventure entity
    /// (strip ` // …` suffix), (3) case-insensitive scan of remaining
    /// snapshot keys as a last resort. Returns the snapshot key that
    /// matched, so the caller can mark it as "covered" for NotInSet
    /// accounting.
    /// </summary>
    internal static bool TryGetFrequencyWeight(
        IReadOnlyDictionary<string, double> snapshot,
        string entityName,
        out double weight,
        out string matchKey)
    {
        if (snapshot.TryGetValue(entityName, out weight))
        {
            matchKey = entityName;
            return true;
        }

        var front = FrontFace(entityName);
        if (!ReferenceEquals(front, entityName)
            && snapshot.TryGetValue(front, out weight))
        {
            matchKey = front;
            return true;
        }

        // Case-insensitive fallback — handles stray casing drift between
        // a hand-maintained snapshot and the Scryfall-imported DB rows.
        foreach (var kv in snapshot)
        {
            if (string.Equals(kv.Key, entityName, StringComparison.OrdinalIgnoreCase)
                || string.Equals(kv.Key, front, StringComparison.OrdinalIgnoreCase))
            {
                weight = kv.Value;
                matchKey = kv.Key;
                return true;
            }
        }

        weight = 0.0;
        matchKey = string.Empty;
        return false;
    }

    private static readonly Regex DfcSplitRx = new(@"\s+//\s+", RegexOptions.Compiled);

    /// <summary>
    /// Extract the front-face (printed-name) portion of a Scryfall card
    /// name. Scryfall stores DFC / adventure / split / MDFC cards as
    /// "Front // Back" (e.g. "Sink into Stupor // Soporific Springs",
    /// "Mosswood Dreadknight // Dread Whispers"). Meta snapshots and
    /// decklists almost always reference cards by the front face alone.
    /// Names without ` // ` are returned unchanged.
    /// </summary>
    public static string FrontFace(string name)
    {
        if (string.IsNullOrEmpty(name)) return name;
        var idx = name.IndexOf(" // ", StringComparison.Ordinal);
        return idx < 0 ? name : name[..idx];
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

/// <summary>
/// One row in the "not in set" rollup: a tournament-frequency snapshot
/// entry whose card never made it into the classified pool. Typical
/// causes are format bans/restrictions filtering the card out before
/// classification or a name-shape mismatch between snapshot and card-db
/// (e.g. snapshot says "Grief", DB has only "Grief // Grief").
/// </summary>
public sealed record CoverageNotInSetRow(string Name, double Weight);
