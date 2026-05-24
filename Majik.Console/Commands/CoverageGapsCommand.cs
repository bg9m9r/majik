using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Majik.Core.Cards;
using Majik.Core.CardData;
using Majik.Core.CardData.Coverage;
using Majik.Core.CardData.Database;
using Majik.Core.Players;
using Microsoft.EntityFrameworkCore;
using SysConsole = System.Console;

namespace Majik.Console.Commands;

/// <summary>
/// <c>coverage-gaps</c> — mines the Unimplemented tier of the engine
/// coverage report and produces a ranked mechanic-cluster backlog. Each
/// cluster is "pattern X covers N cards — build binder Y, unlock all N",
/// turning the 76% Unimplemented number into a concrete work list.
///
/// Format filters mirror the <c>coverage</c> subcommand. Output is a
/// table on stdout plus optional JSON / markdown sidecars.
/// </summary>
public static class CoverageGapsCommand
{
    private static readonly (string Flag, string Format)[] FormatShortcuts =
    {
        ("--modern",    MtgFormat.Modern),
        ("--legacy",    MtgFormat.Legacy),
        ("--vintage",   MtgFormat.Vintage),
        ("--commander", MtgFormat.Commander),
        ("--standard",  MtgFormat.Standard),
        ("--pioneer",   MtgFormat.Pioneer),
        ("--pauper",    MtgFormat.Pauper),
    };

    public static async Task<int> RunAsync(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);

        var format = ResolveFormat(args);
        var jsonOut = ParseFlagValue(args, "--json-out");
        var mdOut = ParseFlagValue(args, "--md-out");
        var topN = ParseIntFlag(args, "--top", defaultValue: 50);
        var minCluster = ParseIntFlag(args, "--min-cluster", defaultValue: 5);

        await using var db = new CardDbContext();
        await db.Database.EnsureCreatedAsync();
        await CardDataSchemaPatcher.PatchAsync(db.Database.GetDbConnection(), CancellationToken.None);

        SysConsole.WriteLine("Loading cards…");
        var entities = await LoadEntitiesAsync(db, format);

        // Dedup by name — gap-mining cares about distinct mechanic
        // shapes, not per-printing counts.
        entities = entities
            .GroupBy(c => c.Name, StringComparer.Ordinal)
            .Select(g => g.OrderByDescending(c => c.IsImplemented).First())
            .ToList();

        SysConsole.WriteLine($"  Pool size: {entities.Count} distinct cards");

        var repo = new DictionaryCardRepository(entities);
        var factory = new ScryfallCardFactory(repo);
        var stubCaster = new Player("Synth", 20);
        var classifier = new CoverageClassifier(factory, stubCaster);

        SysConsole.WriteLine("Classifying + clustering…");
        var clusterer = new CoverageGapClusterer(classifier);
        var allClusters = clusterer.Cluster(entities, minClusterSize: minCluster);

        // Count total Unimplemented separately so the report shows the
        // long-tail percentage. Cheap second pass.
        int totalUnimpl = 0;
        foreach (var e in entities)
        {
            if (classifier.Classify(e) == CoverageTier.Unimplemented) totalUnimpl++;
        }

        var cardsAboveThreshold = allClusters.Sum(c => c.MemberCount);
        var discarded = totalUnimpl - cardsAboveThreshold;
        var topClusters = allClusters.Take(topN).ToList();

        var scope = BuildScopeLabel(format);
        var report = new CoverageGapsReport(
            Scope: scope,
            TotalUnimplemented: totalUnimpl,
            MinClusterSize: minCluster,
            ClustersDiscarded: discarded,
            Clusters: topClusters);

        PrintConsoleSummary(report, allClusters.Count, cardsAboveThreshold);

        if (jsonOut is not null) await WriteJsonAsync(jsonOut, report, allClusters.Count, cardsAboveThreshold);
        if (mdOut is not null) await WriteMarkdownAsync(mdOut, report, allClusters.Count, cardsAboveThreshold);

        return 0;
    }

    private static string? ResolveFormat(string[] args)
    {
        var explicitFmt = ParseFlagValue(args, "--format");
        if (!string.IsNullOrWhiteSpace(explicitFmt)) return explicitFmt.ToLowerInvariant();
        foreach (var (flag, fmt) in FormatShortcuts)
        {
            if (args.Any(a => a.Equals(flag, StringComparison.OrdinalIgnoreCase))) return fmt;
        }
        return null;
    }

    private static async Task<List<CardEntity>> LoadEntitiesAsync(CardDbContext db, string? format)
    {
        IQueryable<CardEntity> query = db.Cards.AsNoTracking();
        if (format is not null)
        {
            var formatKey = format.ToLowerInvariant();
            query =
                from c in query
                join l in db.CardLegalities.AsNoTracking()
                    on c.Id equals l.CardId
                where l.Format == formatKey && l.Status == MtgLegalityStatus.Legal
                select c;
        }
        return await query.ToListAsync();
    }

    private static string BuildScopeLabel(string? format)
    {
        return format is not null ? $"format={format}, dedup-by-name" : "full-pool, dedup-by-name";
    }

    private static void PrintConsoleSummary(CoverageGapsReport report, int totalClusters, int cardsAboveThreshold)
    {
        SysConsole.WriteLine();
        SysConsole.WriteLine($"Coverage gaps ({report.Scope}):");
        SysConsole.WriteLine($"  Unimplemented total: {report.TotalUnimplemented}");
        SysConsole.WriteLine($"  Clusters (≥{report.MinClusterSize} members): {totalClusters}");
        var aboveThresholdPct = report.TotalUnimplemented == 0 ? 0.0
            : 100.0 * cardsAboveThreshold / report.TotalUnimplemented;
        SysConsole.WriteLine($"  Cards captured by above-threshold clusters: {cardsAboveThreshold} ({aboveThresholdPct:F1}%)");
        SysConsole.WriteLine($"  Cards rendered in top-{report.Clusters.Count}: {report.CoveredByClusters} ({report.CoveredPercent:F1}%)");
        SysConsole.WriteLine($"  Long-tail (below threshold): {report.ClustersDiscarded}");
        SysConsole.WriteLine();

        var headerCount = Math.Min(20, report.Clusters.Count);
        if (headerCount == 0) return;
        SysConsole.WriteLine($"Top mechanic clusters (showing {headerCount} of {report.Clusters.Count}):");
        SysConsole.WriteLine();

        for (var i = 0; i < headerCount; i++)
        {
            var c = report.Clusters[i];
            var sigDisplay = OracleSignature.ToDisplay(c.FirstSentenceSignature, maxLen: 96);
            SysConsole.WriteLine($"{i + 1,3}. {c.MemberCount,5} cards  \"{sigDisplay}\"");
            var examples = string.Join(", ", c.ExampleCardNames.Take(5));
            SysConsole.WriteLine($"     Examples: {examples}{(c.ExampleCardNames.Count > 5 ? ", …" : "")}");
            if (!string.IsNullOrEmpty(c.SuggestedBinderName))
            {
                SysConsole.WriteLine($"     Suggest:  {c.SuggestedBinderName}{(c.SuggestedBinderNotes is null ? "" : $" — {c.SuggestedBinderNotes}")}");
            }
            if (!string.IsNullOrEmpty(c.NumericTwinHint))
            {
                SysConsole.WriteLine($"     Hint:     {c.NumericTwinHint}");
            }
            SysConsole.WriteLine();
        }
    }

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };

    private static async Task WriteJsonAsync(string path, CoverageGapsReport report, int totalClusters, int cardsAboveThreshold)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        var payload = new
        {
            scope = report.Scope,
            generated_utc = DateTime.UtcNow.ToString("o"),
            total_unimplemented = report.TotalUnimplemented,
            min_cluster_size = report.MinClusterSize,
            clusters_above_threshold = totalClusters,
            cards_above_threshold = cardsAboveThreshold,
            cards_below_threshold = report.ClustersDiscarded,
            cards_rendered_in_top = report.CoveredByClusters,
            cards_rendered_percent = Math.Round(report.CoveredPercent, 2),
            clusters = report.Clusters.Select(c => new
            {
                first_sentence_signature = c.FirstSentenceSignature,
                trigger_signature = c.TriggerSignature,
                effect_verb_signature = c.EffectVerbSignature,
                member_count = c.MemberCount,
                canonical_card_name = c.CanonicalCardName,
                canonical_oracle_text = c.CanonicalOracleText,
                example_card_names = c.ExampleCardNames,
                suggested_binder_name = c.SuggestedBinderName,
                suggested_binder_notes = c.SuggestedBinderNotes,
                numeric_twin_hint = c.NumericTwinHint,
                flagged_as_classifier_miss = c.FlaggedAsClassifierMiss,
            }).ToList(),
        };

        await using var fs = File.Create(path);
        await JsonSerializer.SerializeAsync(fs, payload, JsonOpts);
        SysConsole.WriteLine();
        SysConsole.WriteLine($"Wrote JSON → {Path.GetFullPath(path)}");
    }

    private static async Task WriteMarkdownAsync(string path, CoverageGapsReport report, int totalClusters, int cardsAboveThreshold)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        var sb = new StringBuilder();
        sb.AppendLine("# Coverage gaps — mechanic-cluster backlog");
        sb.AppendLine();
        sb.AppendLine($"- **Scope:** {report.Scope}");
        sb.AppendLine($"- **Generated:** {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC");
        sb.AppendLine($"- **Unimplemented total:** {report.TotalUnimplemented}");
        sb.AppendLine($"- **Min cluster size:** {report.MinClusterSize}");
        sb.AppendLine($"- **Clusters ≥ threshold:** {totalClusters} (rendering top {report.Clusters.Count})");
        var aboveThresholdPct = report.TotalUnimplemented == 0 ? 0.0
            : 100.0 * cardsAboveThreshold / report.TotalUnimplemented;
        sb.AppendLine($"- **Cards in above-threshold clusters:** {cardsAboveThreshold} ({aboveThresholdPct:F1}% of unimplemented)");
        sb.AppendLine($"- **Cards in rendered top-{report.Clusters.Count}:** {report.CoveredByClusters} ({report.CoveredPercent:F1}% of unimplemented)");
        sb.AppendLine($"- **Long-tail cards (below threshold):** {report.ClustersDiscarded}");
        sb.AppendLine();
        sb.AppendLine("## Ranked clusters");
        sb.AppendLine();
        sb.AppendLine("| Rank | Count | Suggested binder | Signature |");
        sb.AppendLine("|---:|---:|---|---|");
        for (var i = 0; i < report.Clusters.Count; i++)
        {
            var c = report.Clusters[i];
            var sigCell = MarkdownInlineEscape(OracleSignature.ToDisplay(c.FirstSentenceSignature, maxLen: 80));
            var binderCell = c.SuggestedBinderName ?? "_(none)_";
            sb.AppendLine($"| {i + 1} | {c.MemberCount} | {binderCell} | `{sigCell}` |");
        }

        sb.AppendLine();
        sb.AppendLine("## Cluster detail");
        sb.AppendLine();
        for (var i = 0; i < report.Clusters.Count; i++)
        {
            var c = report.Clusters[i];
            sb.AppendLine($"### {i + 1}. {c.MemberCount} cards — `{MarkdownInlineEscape(OracleSignature.ToDisplay(c.FirstSentenceSignature, maxLen: 200))}`");
            sb.AppendLine();
            if (!string.IsNullOrEmpty(c.SuggestedBinderName))
            {
                sb.AppendLine($"- **Suggested binder:** `{c.SuggestedBinderName}`" +
                              (string.IsNullOrEmpty(c.SuggestedBinderNotes) ? "" : $" — {c.SuggestedBinderNotes}"));
            }
            else
            {
                sb.AppendLine("- **Suggested binder:** _(no registry hit — add a new template)_");
            }
            if (!string.IsNullOrEmpty(c.TriggerSignature))
            {
                sb.AppendLine($"- **Trigger signature:** `{MarkdownInlineEscape(c.TriggerSignature)}`");
            }
            if (!string.IsNullOrEmpty(c.EffectVerbSignature))
            {
                sb.AppendLine($"- **Effect verb:** `{c.EffectVerbSignature}`");
            }
            if (!string.IsNullOrEmpty(c.NumericTwinHint))
            {
                sb.AppendLine($"- **Hint:** {c.NumericTwinHint}");
            }
            sb.AppendLine($"- **Canonical example:** {c.CanonicalCardName}");
            sb.AppendLine();
            sb.AppendLine($"  > {EscapeForBlockquote(c.CanonicalOracleText)}");
            sb.AppendLine();
            sb.AppendLine("- **Example cards (up to 20):**");
            foreach (var name in c.ExampleCardNames)
            {
                sb.AppendLine($"  - {name}");
            }
            sb.AppendLine();
        }

        sb.AppendLine("---");
        sb.AppendLine();
        sb.AppendLine("Generated by `dotnet run --project Majik.Console -- coverage-gaps`. " +
                      "Clusterer source: `Majik.Core/CardData/Coverage/CoverageGapClusterer.cs`.");

        var utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        await File.WriteAllTextAsync(path, sb.ToString(), utf8NoBom);
        SysConsole.WriteLine($"Wrote Markdown → {Path.GetFullPath(path)}");
    }

    private static string MarkdownInlineEscape(string s) =>
        s.Replace("|", "\\|").Replace("`", "'");

    private static string EscapeForBlockquote(string s) =>
        s.Replace("\n", " ").Replace("\r", " ").Trim();

    private static string? ParseFlagValue(string[] args, string flag)
    {
        var idx = Array.FindIndex(args, a => a.Equals(flag, StringComparison.OrdinalIgnoreCase));
        if (idx < 0 || idx + 1 >= args.Length) return null;
        var v = args[idx + 1];
        return string.IsNullOrWhiteSpace(v) ? null : v;
    }

    private static int ParseIntFlag(string[] args, string flag, int defaultValue)
    {
        var raw = ParseFlagValue(args, flag);
        return raw is not null && int.TryParse(raw, out var n) ? n : defaultValue;
    }

    /// <summary>
    /// Same in-memory <see cref="ICardRepository"/> trick as the
    /// <see cref="CoverageCommand"/>. Duplicated locally rather than
    /// extracted because the two subcommands evolve independently and
    /// the shared shape is tiny.
    /// </summary>
    private sealed class DictionaryCardRepository : ICardRepository
    {
        private readonly Dictionary<string, CardEntity> _by;
        public DictionaryCardRepository(IEnumerable<CardEntity> entities)
        {
            _by = new Dictionary<string, CardEntity>(StringComparer.Ordinal);
            foreach (var e in entities)
            {
                if (!_by.TryGetValue(e.Name, out var existing) || (!existing.IsImplemented && e.IsImplemented))
                {
                    _by[e.Name] = e;
                }
            }
        }

        public CardEntity? GetByName(string name) =>
            !string.IsNullOrWhiteSpace(name) && _by.TryGetValue(name, out var e) ? e : null;

        public IReadOnlyList<CardEntity> GetByNames(IEnumerable<string> names) =>
            names.Select(GetByName).OfType<CardEntity>().ToList();

        public IReadOnlyList<CardEntity> Search(
            string? q, bool implementedOnly, int limit,
            IReadOnlyList<string>? colors = null,
            IReadOnlyList<string>? types = null,
            IReadOnlyList<int>? cmcBuckets = null) =>
            throw new NotSupportedException("DictionaryCardRepository is read-only.");

        public bool IsImplemented(string name) =>
            GetByName(name)?.IsImplemented ?? false;

        public void SetImplemented(string name, bool value) =>
            throw new NotSupportedException("DictionaryCardRepository is read-only.");
    }
}
