using System.Text.RegularExpressions;
using Majik.Core.CardData.Database;

namespace Majik.Core.CardData.Coverage;

/// <summary>
/// Clusters Unimplemented cards by oracle-text signature. Output is a
/// ranked list of <see cref="CoverageGapCluster"/> records describing
/// each mechanic bucket — count, representative cards, suggested binder
/// name, and the canonical oracle text of one example.
///
/// Pure: no I/O, no engine state. Takes the entity set and the
/// classifier from the caller; the caller is expected to have filtered
/// to whichever pool (format / decklist / full) the report is for.
/// </summary>
public sealed class CoverageGapClusterer
{
    private readonly CoverageClassifier _classifier;
    private readonly IReadOnlyList<BinderSuggestion> _binderRegistry;

    public CoverageGapClusterer(CoverageClassifier classifier)
        : this(classifier, BinderSuggestionRegistry.Default)
    {
    }

    /// <summary>Test seam — inject the binder-name registry.</summary>
    public CoverageGapClusterer(
        CoverageClassifier classifier,
        IReadOnlyList<BinderSuggestion> binderRegistry)
    {
        _classifier = classifier ?? throw new ArgumentNullException(nameof(classifier));
        _binderRegistry = binderRegistry ?? throw new ArgumentNullException(nameof(binderRegistry));
    }

    /// <summary>
    /// Run the clusterer over <paramref name="entities"/>, returning all
    /// clusters that pass <paramref name="minClusterSize"/>, sorted
    /// descending by member count. Caller decides how many to render.
    /// </summary>
    public IReadOnlyList<CoverageGapCluster> Cluster(
        IEnumerable<CardEntity> entities,
        int minClusterSize = 5)
    {
        ArgumentNullException.ThrowIfNull(entities);
        if (minClusterSize < 1) minClusterSize = 1;

        // bucket: first-sentence signature → list of member rows.
        var buckets = new Dictionary<string, List<ClusterMember>>(StringComparer.Ordinal);

        foreach (var entity in entities)
        {
            var tier = _classifier.Classify(entity);
            if (tier != CoverageTier.Unimplemented) continue;

            var sig = OracleSignature.From(entity);
            // Drop cards whose oracle text was empty — they belong in
            // Vanilla, not in the gap report. Defensive — the classifier
            // would have already excluded them, but cluster on empty
            // string would create a giant junk bucket.
            if (string.IsNullOrEmpty(sig.FirstSentenceSignature)) continue;

            if (!buckets.TryGetValue(sig.FirstSentenceSignature, out var list))
            {
                list = new List<ClusterMember>();
                buckets[sig.FirstSentenceSignature] = list;
            }
            list.Add(new ClusterMember(entity, sig));
        }

        var clusters = new List<CoverageGapCluster>(buckets.Count);
        foreach (var (signature, members) in buckets)
        {
            if (members.Count < minClusterSize) continue;

            // Pick a canonical example deterministically: the member
            // with the alphabetically-first name. Stable across runs.
            var canonical = members
                .OrderBy(m => m.Entity.Name, StringComparer.Ordinal)
                .First();

            // Top trigger / verb across the cluster — almost always the
            // same as the canonical's since the first-sentence signature
            // is identical, but compute by majority vote so the rare
            // odd-one-out gets normalised.
            var trigger = MajorityValue(members.Select(m => m.Signature.TriggerSignature));
            var verb = MajorityValue(members.Select(m => m.Signature.EffectVerbSignature));

            var suggestion = SuggestBinder(signature, trigger, verb);

            var examples = members
                .OrderBy(m => m.Entity.Name, StringComparer.Ordinal)
                .Take(20)
                .Select(m => m.Entity.Name)
                .ToList();

            clusters.Add(new CoverageGapCluster(
                FirstSentenceSignature: signature,
                TriggerSignature: trigger,
                EffectVerbSignature: verb,
                MemberCount: members.Count,
                CanonicalCardName: canonical.Entity.Name,
                CanonicalOracleText: OracleSignature.PreviewOracle(canonical.Entity.OracleText),
                ExampleCardNames: examples,
                SuggestedBinderName: suggestion?.BinderName,
                SuggestedBinderNotes: suggestion?.Notes,
                FlaggedAsClassifierMiss: false));
        }

        // Rank descending by count, break ties by signature for
        // determinism.
        clusters.Sort((a, b) =>
        {
            var c = b.MemberCount.CompareTo(a.MemberCount);
            return c != 0 ? c : string.CompareOrdinal(a.FirstSentenceSignature, b.FirstSentenceSignature);
        });

        // Numeric-twin hint: if cluster N has a signature identical to
        // cluster N+1 with the "n" tokens removed, surface a hint so the
        // reader knows the two clusters could parametrise as one. We
        // mutate after sort because the hint compares neighbours by
        // count-bucket regardless of original ranking.
        AnnotateNumericTwins(clusters);

        return clusters;
    }

    /// <summary>
    /// Pick the most-frequent string in <paramref name="values"/>. Ties
    /// resolve to the lexicographically-smaller value (stable across runs).
    /// </summary>
    private static string MajorityValue(IEnumerable<string> values)
    {
        var groups = values
            .Where(v => v is not null)
            .GroupBy(v => v, StringComparer.Ordinal)
            .OrderByDescending(g => g.Count())
            .ThenBy(g => g.Key, StringComparer.Ordinal);
        return groups.FirstOrDefault()?.Key ?? "";
    }

    /// <summary>
    /// First binder-suggestion whose regex matches the first-sentence
    /// signature (or, failing that, the trigger / verb signature). Null
    /// when nothing in the registry covers this shape — the cluster
    /// renders with an empty suggestion column.
    /// </summary>
    public BinderSuggestion? SuggestBinder(string firstSentence, string trigger, string verb)
    {
        foreach (var s in _binderRegistry)
        {
            if (s.Match.IsMatch(firstSentence)) return s;
        }
        // Coarse fallback: match on the trigger phrase or verb alone.
        // This lets us at least bucket "etb-something" shapes into the
        // generic ETB-trigger suggestion.
        foreach (var s in _binderRegistry)
        {
            if (!string.IsNullOrEmpty(trigger) && s.Match.IsMatch(trigger)) return s;
        }
        return null;
    }

    private static readonly Regex DigitNeutraliseRx = new(@"\bn\b", RegexOptions.Compiled);

    private static void AnnotateNumericTwins(List<CoverageGapCluster> clusters)
    {
        // Group by signature-with-numbers-stripped. Where 2+ clusters
        // share a stripped form, surface the hint.
        var byStripped = clusters
            .GroupBy(c => DigitNeutraliseRx.Replace(c.FirstSentenceSignature, "*"))
            .Where(g => g.Count() > 1)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(c => c.MemberCount).First().FirstSentenceSignature);

        for (var i = 0; i < clusters.Count; i++)
        {
            var c = clusters[i];
            var stripped = DigitNeutraliseRx.Replace(c.FirstSentenceSignature, "*");
            if (byStripped.TryGetValue(stripped, out var leader) &&
                !string.Equals(leader, c.FirstSentenceSignature, StringComparison.Ordinal))
            {
                clusters[i] = c with
                {
                    NumericTwinHint = $"parametrize as N — combine with cluster \"{leader}\"",
                };
            }
        }
    }

    private readonly record struct ClusterMember(CardEntity Entity, OracleSignature Signature);
}

/// <summary>
/// One mechanic-cluster row in a coverage-gaps report.
/// </summary>
public sealed record CoverageGapCluster(
    string FirstSentenceSignature,
    string TriggerSignature,
    string EffectVerbSignature,
    int MemberCount,
    string CanonicalCardName,
    string CanonicalOracleText,
    IReadOnlyList<string> ExampleCardNames,
    string? SuggestedBinderName,
    string? SuggestedBinderNotes,
    bool FlaggedAsClassifierMiss)
{
    /// <summary>
    /// Optional sibling-cluster hint when two clusters differ only by a
    /// numeric token (e.g. "draw 2 cards" vs "draw 3 cards"). Set by
    /// <see cref="CoverageGapClusterer.Cluster"/> after ranking. Null
    /// when this cluster isn't a numeric twin.
    /// </summary>
    public string? NumericTwinHint { get; init; }
}

/// <summary>
/// Registry entry mapping a signature-regex to a proposed binder name.
/// </summary>
public sealed record BinderSuggestion(Regex Match, string BinderName, string? Notes = null);

/// <summary>
/// Initial seed registry of ~30 signature → binder mappings. Extend as
/// new high-volume clusters are spotted in the generated report.
/// </summary>
public static class BinderSuggestionRegistry
{
    public static readonly IReadOnlyList<BinderSuggestion> Default = new BinderSuggestion[]
    {
        // -------- ETB triggers --------
        new(Rx(@"^when ~ enters,? .*draw a card"),
            "EtbDrawCardTriggerBinder",
            "ETB triggered ability — draw one card"),
        new(Rx(@"^when ~ enters,? .*draw n cards?"),
            "EtbDrawNCardsTriggerBinder",
            "ETB triggered ability — draw N cards"),
        new(Rx(@"^when ~ enters,? .*deals? n damage to (any target|target creature or player|target player|target creature|each opponent|each creature)"),
            "EtbDealsDamageTriggerBinder",
            "ETB triggered ability — deals N damage"),
        new(Rx(@"^when ~ enters,? .*gains? n life"),
            "EtbGainLifeTriggerBinder",
            "ETB triggered ability — controller gains N life"),
        new(Rx(@"^when ~ enters,? .*destroy target"),
            "EtbDestroyTargetTriggerBinder",
            "ETB triggered ability — destroy on entry"),
        new(Rx(@"^when ~ enters,? .*returns? target .* to its owner's hand"),
            "EtbBounceTriggerBinder",
            "ETB triggered ability — bounce on entry"),
        new(Rx(@"^when ~ enters,? .*scry n"),
            "EtbScryTriggerBinder",
            "ETB triggered ability — scry N"),
        new(Rx(@"^when ~ enters,? .*create"),
            "EtbCreateTokenTriggerBinder",
            "ETB triggered ability — token creation"),
        new(Rx(@"^when ~ enters,? .*counter"),
            "EtbCounterTriggerBinder",
            "ETB triggered ability — put counters / counter spells"),
        new(Rx(@"^when ~ enters,?"),
            "EtbGenericTriggerBinder",
            "ETB triggered ability — catch-all"),

        // -------- LTB / dies triggers --------
        new(Rx(@"^when ~ dies,? .*returns? .* to its owner's hand"),
            "OnDiesBounceTriggerBinder",
            "Dies-trigger — bounce"),
        new(Rx(@"^when ~ dies,? .*draws? a card"),
            "OnDiesDrawCardTriggerBinder",
            "Dies-trigger — draw a card"),
        new(Rx(@"^when ~ dies,?"),
            "OnDiesGenericTriggerBinder",
            "Dies-trigger — catch-all"),
        new(Rx(@"^when ~ leaves the battlefield,?"),
            "OnLeavesGenericTriggerBinder",
            "Leaves-the-battlefield trigger"),

        // -------- Activated abilities --------
        new(Rx(@"^\{cost\}, sacrifice ~:.*deals? n damage"),
            "SacrificeForDamageActivatedBinder",
            "Activated — {cost}, sac self for damage"),
        new(Rx(@"^\{cost\}, sacrifice ~:"),
            "SacrificeForEffectActivatedBinder",
            "Activated — {cost}, sac self for effect"),
        new(Rx(@"^\{cost\}:.*deals? n damage to (any target|target creature or player|target player|target creature)"),
            "PingActivatedBinder",
            "Activated — ping for damage"),
        new(Rx(@"^\{cost\}:.*draws? a card"),
            "ActivatedDrawCardBinder",
            "Activated — draw a card"),
        new(Rx(@"^\{cost\}: target creature gets \+n/\+n until end of turn"),
            "ActivatedPumpEotBinder",
            "Activated — pump target creature EOT"),
        new(Rx(@"^\{cost\}: ~ gets \+n/\+n until end of turn"),
            "ActivatedSelfPumpBinder",
            "Activated — self pump EOT"),
        new(Rx(@"^\{cost\}: tap target"),
            "ActivatedTapTargetBinder",
            "Activated — tap target"),
        new(Rx(@"^\{cost\}:"),
            "ActivatedGenericBinder",
            "Activated ability — catch-all"),

        // -------- Periodic triggers --------
        new(Rx(@"^at the beginning of your upkeep,?.*draws? a card"),
            "UpkeepDrawCardTriggerBinder",
            "Upkeep — draw a card"),
        new(Rx(@"^at the beginning of your upkeep,?.*gains? n life"),
            "UpkeepGainLifeTriggerBinder",
            "Upkeep — gain life"),
        new(Rx(@"^at the beginning of your upkeep,?"),
            "UpkeepGenericTriggerBinder",
            "Upkeep trigger — catch-all"),
        new(Rx(@"^at the beginning of your end step,?"),
            "EndStepTriggerBinder",
            "End step trigger — catch-all"),
        new(Rx(@"^at the beginning of combat on your turn,?"),
            "BeginCombatTriggerBinder",
            "Begin combat trigger — catch-all"),

        // -------- Cast / spell triggers --------
        new(Rx(@"^whenever you cast a (creature|noncreature|instant|sorcery|spell)"),
            "OnCastSpellTriggerBinder",
            "Cast-trigger — catch-all"),
        new(Rx(@"^whenever a creature you control attacks,?"),
            "OnAttackTriggerBinder",
            "Attack-trigger — catch-all"),

        // -------- Static keyword-like --------
        new(Rx(@"^~ gets \+n/\+n"),
            "StaticSelfPumpBinder",
            "Static — self pump"),
    };

    private static Regex Rx(string pattern) =>
        new(pattern, RegexOptions.Compiled | RegexOptions.CultureInvariant);
}
