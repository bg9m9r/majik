namespace Majik.Core.CardData.MechanicDeps;

/// <summary>
/// Aggregate output for one canonical primitive — N factories all
/// blocked on the same engine work item.
/// </summary>
public sealed record MechanicDependencyCluster(
    string PrimitiveId,
    string DisplayName,
    string? CompRulesCitation,
    string? ImplementationHint,
    IReadOnlyList<DeferralMention> Mentions)
{
    /// <summary>Distinct factories mentioning the primitive — the impact axis.</summary>
    public IReadOnlyList<string> Factories =>
        Mentions.Select(m => m.FactoryName).Distinct(StringComparer.Ordinal)
                .OrderBy(s => s, StringComparer.Ordinal).ToList();

    public int FactoryCount => Factories.Count;
    public int MentionCount => Mentions.Count;
}

/// <summary>
/// Final report — primitives ranked by impact + an "Other" bucket for
/// unclustered mentions surfaced for human review.
/// </summary>
public sealed record MechanicDependencyReport(
    IReadOnlyList<MechanicDependencyCluster> Clusters,
    IReadOnlyList<DeferralMention> Unclustered)
{
    public int TotalMentions => Clusters.Sum(c => c.MentionCount) + Unclustered.Count;
}

/// <summary>
/// Maps <see cref="DeferralMention"/> records onto canonical
/// <see cref="MechanicPrimitive"/> rows using the registry, then ranks
/// the result clusters by the number of distinct factories blocked.
/// </summary>
public sealed class MechanicDependencyClusterer
{
    /// <summary>
    /// Cluster the supplied mentions. Mentions matching no registered
    /// primitive land in <see cref="MechanicDependencyReport.Unclustered"/>.
    /// Clusters with zero mentions are dropped. Result is sorted by:
    /// <list type="number">
    ///   <item>distinct factory count, descending</item>
    ///   <item>mention count, descending</item>
    ///   <item>primitive ID, ascending (stable tiebreak)</item>
    /// </list>
    /// </summary>
    public MechanicDependencyReport Cluster(IReadOnlyList<DeferralMention> mentions)
    {
        ArgumentNullException.ThrowIfNull(mentions);

        var buckets = new Dictionary<string, List<DeferralMention>>(StringComparer.Ordinal);
        var unclustered = new List<DeferralMention>();

        foreach (var m in mentions)
        {
            var primitive = MechanicPrimitiveRegistry.Match(m.Sentence);
            if (primitive is null)
            {
                unclustered.Add(m);
                continue;
            }
            if (!buckets.TryGetValue(primitive.Id, out var list))
            {
                buckets[primitive.Id] = list = new List<DeferralMention>();
            }
            list.Add(m);
        }

        var clusters = new List<MechanicDependencyCluster>();
        foreach (var p in MechanicPrimitiveRegistry.All)
        {
            if (!buckets.TryGetValue(p.Id, out var list) || list.Count == 0) continue;
            clusters.Add(new MechanicDependencyCluster(
                PrimitiveId: p.Id,
                DisplayName: p.DisplayName,
                CompRulesCitation: p.CompRulesCitation,
                ImplementationHint: p.ImplementationHint,
                Mentions: list));
        }

        clusters.Sort((a, b) =>
        {
            var byFactories = b.FactoryCount.CompareTo(a.FactoryCount);
            if (byFactories != 0) return byFactories;
            var byMentions = b.MentionCount.CompareTo(a.MentionCount);
            if (byMentions != 0) return byMentions;
            return string.CompareOrdinal(a.PrimitiveId, b.PrimitiveId);
        });

        return new MechanicDependencyReport(clusters, unclustered);
    }
}
