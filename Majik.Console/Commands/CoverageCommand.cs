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
/// <c>coverage</c> — data-driven engine-coverage classifier. Runs every
/// Scryfall card row through the production
/// <see cref="ScryfallCardFactory"/> pipeline, classifies each into a
/// <see cref="CoverageTier"/>, and prints / writes aggregate metrics.
///
/// Replaces the hand-maintained <c>MODERN_COVERAGE.md</c> archetype
/// rollups with auto-computed numbers. See spec in
/// <c>feat/coverage-bot-script</c> PR for the full contract.
///
/// Flags:
/// <list type="bullet">
///   <item><c>--modern</c> / <c>--legacy</c> / <c>--vintage</c> /
///   <c>--commander</c> — filter to that format's legal pool (uses the
///   <c>CardLegalities</c> side table).</item>
///   <item><c>--format &lt;key&gt;</c> — generic format filter; any
///   Scryfall format key works (pioneer, pauper, …).</item>
///   <item><c>--decklist &lt;path&gt;</c> — read a plain-text decklist
///   and compute copy-weighted coverage.</item>
///   <item><c>--json-out &lt;path&gt;</c> — emit per-card classification
///   + aggregate as structured JSON.</item>
///   <item><c>--md-out &lt;path&gt;</c> — emit a markdown report (tier
///   counts + top-N unimplemented cards).</item>
///   <item><c>--no-dedup</c> — count every Scryfall printing instead of
///   deduplicating to one row per oracle name. Default is dedup-by-name.</item>
///   <item><c>--top &lt;n&gt;</c> — number of unimplemented rows to
///   include in the markdown / console output (default 20).</item>
/// </list>
/// </summary>
public static class CoverageCommand
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
        var decklistPath = ParseFlagValue(args, "--decklist");
        var jsonOut = ParseFlagValue(args, "--json-out");
        var mdOut = ParseFlagValue(args, "--md-out");
        var dedupByName = !args.Any(a => a.Equals("--no-dedup", StringComparison.OrdinalIgnoreCase));
        var topN = ParseIntFlag(args, "--top", defaultValue: 20);

        // Load decklist first — drives both the scope label and the name filter.
        IReadOnlyDictionary<string, int>? weights = null;
        if (decklistPath is not null)
        {
            if (!File.Exists(decklistPath))
            {
                SysConsole.Error.WriteLine($"Decklist not found: {decklistPath}");
                return 1;
            }
            var text = await File.ReadAllTextAsync(decklistPath);
            weights = DecklistParser.Parse(text);
            if (weights.Count == 0)
            {
                SysConsole.Error.WriteLine($"No cards parsed from {decklistPath}.");
                return 1;
            }
        }

        await using var db = new CardDbContext();
        await db.Database.EnsureCreatedAsync();
        await CardDataSchemaPatcher.PatchAsync(db.Database.GetDbConnection(), CancellationToken.None);

        // Pull the entity set this run cares about.
        var entities = await LoadEntitiesAsync(db, format, weights);
        if (dedupByName)
        {
            entities = entities
                .GroupBy(c => c.Name, StringComparer.Ordinal)
                .Select(g => g.OrderByDescending(c => c.IsImplemented).First())
                .ToList();
        }

        // Build an in-memory repo so the factory doesn't roundtrip the DB
        // on every classify call. The loaded entity set is the whole
        // working set for this run.
        var repo = new DictionaryCardRepository(entities);
        var factory = new ScryfallCardFactory(repo);
        var stubCaster = new Player("Synth", 20);
        var classifier = new CoverageClassifier(factory, stubCaster);

        var scope = BuildScopeLabel(format, decklistPath, dedupByName);
        var report = CoverageReportV2.Build(scope, entities, classifier, weights, topUnimplemented: topN);

        PrintConsoleSummary(report, weights is not null);

        if (jsonOut is not null) await WriteJsonAsync(jsonOut, report);
        if (mdOut is not null) await WriteMarkdownAsync(mdOut, report, weights is not null);

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

    private static async Task<List<CardEntity>> LoadEntitiesAsync(
        CardDbContext db, string? format, IReadOnlyDictionary<string, int>? deckWeights)
    {
        IQueryable<CardEntity> query = db.Cards.AsNoTracking();

        if (deckWeights is not null)
        {
            // Decklist mode — narrow to just the deck's names. The
            // filter is small (<100 names), so an in-clause is fine.
            var names = deckWeights.Keys.ToHashSet(StringComparer.Ordinal);
            return await query.Where(c => names.Contains(c.Name)).ToListAsync();
        }

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

    private static string BuildScopeLabel(string? format, string? decklistPath, bool dedup)
    {
        var parts = new List<string>();
        if (decklistPath is not null) parts.Add($"decklist={Path.GetFileName(decklistPath)}");
        else if (format is not null) parts.Add($"format={format}");
        else parts.Add("full-pool");
        parts.Add(dedup ? "dedup-by-name" : "per-printing");
        return string.Join(", ", parts);
    }

    private static void PrintConsoleSummary(CoverageReportV2 report, bool decklistMode)
    {
        SysConsole.WriteLine($"Engine coverage ({report.Scope}):");
        if (decklistMode)
        {
            SysConsole.WriteLine(
                $"  Weighted: {report.WeightedCoveredPercent,5:F1}% ({report.WeightedCovered} / {report.TotalWeight} card copies)");
            SysConsole.WriteLine(
                $"  Distinct: {report.CoveredPercent,5:F1}% ({report.CoveredCards} / {report.TotalCards} unique names)");
        }
        else
        {
            SysConsole.WriteLine(
                $"  Coverage: {report.CoveredPercent,5:F1}% ({report.CoveredCards} / {report.TotalCards} cards)");
        }

        SysConsole.WriteLine();
        foreach (var tier in Enum.GetValues<CoverageTier>())
        {
            var n = report.CountsByTier[tier];
            var pct = report.TotalCards == 0 ? 0.0 : 100.0 * n / report.TotalCards;
            SysConsole.WriteLine($"  {tier,-14} {n,6} ({pct,5:F1}%)");
        }

        if (report.TopUnimplemented.Count == 0) return;
        SysConsole.WriteLine();
        SysConsole.WriteLine($"Top unimplemented (showing {report.TopUnimplemented.Count}):");
        foreach (var row in report.TopUnimplemented)
        {
            var prefix = decklistMode ? $"  {row.Weight,3}x  " : "  - ";
            SysConsole.WriteLine($"{prefix}{row.Name}");
        }
    }

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };

    private static async Task WriteJsonAsync(string path, CoverageReportV2 report)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        // Project to a stable schema (avoid leaking enum integers / EF
        // proxies). Per-card list is the bulk of the file.
        var payload = new
        {
            scope = report.Scope,
            generated_utc = DateTime.UtcNow.ToString("o"),
            total_cards = report.TotalCards,
            total_weight = report.TotalWeight,
            covered_cards = report.CoveredCards,
            covered_percent = Math.Round(report.CoveredPercent, 2),
            weighted_covered = report.WeightedCovered,
            weighted_covered_percent = Math.Round(report.WeightedCoveredPercent, 2),
            counts_by_tier = report.CountsByTier.ToDictionary(kv => kv.Key.ToString(), kv => kv.Value),
            weighted_by_tier = report.WeightedByTier.ToDictionary(kv => kv.Key.ToString(), kv => kv.Value),
            top_unimplemented = report.TopUnimplemented
                .Select(r => new { name = r.Name, weight = r.Weight })
                .ToList(),
            per_card = report.PerCard
                .OrderBy(r => r.Name, StringComparer.Ordinal)
                .Select(r => new { name = r.Name, type_line = r.TypeLine, tier = r.Tier.ToString() })
                .ToList(),
        };

        await using var fs = File.Create(path);
        await JsonSerializer.SerializeAsync(fs, payload, JsonOpts);
        SysConsole.WriteLine();
        SysConsole.WriteLine($"Wrote JSON → {Path.GetFullPath(path)}");
    }

    private static async Task WriteMarkdownAsync(string path, CoverageReportV2 report, bool decklistMode)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        var sb = new StringBuilder();
        sb.AppendLine("# Engine coverage report");
        sb.AppendLine();
        sb.AppendLine($"- **Scope:** {report.Scope}");
        sb.AppendLine($"- **Generated:** {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC");
        sb.AppendLine($"- **Total cards:** {report.TotalCards}");
        if (decklistMode)
        {
            sb.AppendLine($"- **Weighted copies:** {report.TotalWeight}");
            sb.AppendLine($"- **Weighted coverage:** {report.WeightedCoveredPercent:F1}% ({report.WeightedCovered} / {report.TotalWeight})");
        }
        sb.AppendLine($"- **Distinct coverage:** {report.CoveredPercent:F1}% ({report.CoveredCards} / {report.TotalCards})");
        sb.AppendLine();
        sb.AppendLine("## Tier breakdown");
        sb.AppendLine();
        sb.AppendLine("| Tier | Count | Share |");
        sb.AppendLine("|---|---:|---:|");
        foreach (var tier in Enum.GetValues<CoverageTier>())
        {
            var n = report.CountsByTier[tier];
            var pct = report.TotalCards == 0 ? 0.0 : 100.0 * n / report.TotalCards;
            sb.AppendLine($"| {tier} | {n} | {pct:F1}% |");
        }

        if (report.TopUnimplemented.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine($"## Top unimplemented ({report.TopUnimplemented.Count})");
            sb.AppendLine();
            if (decklistMode)
            {
                sb.AppendLine("| Copies | Card |");
                sb.AppendLine("|---:|---|");
                foreach (var row in report.TopUnimplemented)
                {
                    sb.AppendLine($"| {row.Weight} | {row.Name} |");
                }
            }
            else
            {
                foreach (var row in report.TopUnimplemented)
                {
                    sb.AppendLine($"- {row.Name}");
                }
            }
        }

        sb.AppendLine();
        sb.AppendLine("---");
        sb.AppendLine();
        sb.AppendLine("Generated by `dotnet run --project Majik.Console -- coverage`. " +
                      "See `Majik.Core/CardData/Coverage/` for the classifier source.");

        var utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        await File.WriteAllTextAsync(path, sb.ToString(), utf8NoBom);
        SysConsole.WriteLine($"Wrote Markdown → {Path.GetFullPath(path)}");
    }

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
    /// Lightweight in-memory <see cref="ICardRepository"/>. Used so the
    /// classifier doesn't roundtrip the DB per name when sweeping 30k+
    /// rows. Only the methods <see cref="ScryfallCardFactory"/> exercises
    /// are real — bulk lookup is supported, mutation throws.
    /// </summary>
    private sealed class DictionaryCardRepository : ICardRepository
    {
        private readonly Dictionary<string, CardEntity> _by;
        public DictionaryCardRepository(IEnumerable<CardEntity> entities)
        {
            _by = new Dictionary<string, CardEntity>(StringComparer.Ordinal);
            foreach (var e in entities)
            {
                // Prefer implemented printing when collisions occur (mirrors
                // the DB repo's OrderByDescending(IsImplemented)).
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
