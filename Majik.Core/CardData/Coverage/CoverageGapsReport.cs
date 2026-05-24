namespace Majik.Core.CardData.Coverage;

/// <summary>
/// Aggregate output of the <c>coverage-gaps</c> classifier. Pure data —
/// no I/O. Built by <see cref="CoverageGapClusterer.Cluster"/>; consumed
/// by the console subcommand for tabular / JSON / markdown rendering.
/// </summary>
public sealed record CoverageGapsReport(
    string Scope,
    int TotalUnimplemented,
    int MinClusterSize,
    int ClustersDiscarded,
    IReadOnlyList<CoverageGapCluster> Clusters)
{
    /// <summary>
    /// Total unimplemented cards captured by the rendered clusters.
    /// Difference vs <see cref="TotalUnimplemented"/> = the long tail
    /// of below-threshold clusters.
    /// </summary>
    public int CoveredByClusters => Clusters.Sum(c => c.MemberCount);

    public double CoveredPercent =>
        TotalUnimplemented == 0 ? 0.0 : 100.0 * CoveredByClusters / TotalUnimplemented;
}
