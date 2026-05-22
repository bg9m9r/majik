using System.Text;
using Majik.Core.CardData;
using Majik.Core.CardData.Database;
using Majik.Core.CardData.SpellTemplates;
using Majik.Core.Players;
using Microsoft.EntityFrameworkCore;
using SysConsole = System.Console;

namespace Majik.Console.Commands;

/// <summary>
/// `coverage-report [--format &lt;fmt&gt;] [--dedup-by-name] [--dump-unmatched &lt;path&gt;]`
/// — walks every instant/sorcery in the SQLite catalog (optionally restricted to
/// a format-legal subset via the <c>CardLegalities</c> side table), runs
/// OracleSpellBinder.Registry against each, prints matched %, per-template hit
/// counts, and the first 20 unmatched names. Gives a concrete answer to "what
/// % of the card pool can the engine actually run?" plus a steady backlog of
/// new templates worth adding.
///
/// <para><c>--format modern</c> restricts to cards currently legal in Modern;
/// any Scryfall format key (modern, standard, pioneer, …) works. Omit the flag
/// for full-pool coverage.</para>
///
/// <para><c>--dedup-by-name</c> collapses multiple printings of the same oracle
/// into one row before running the binder. All printings share oracle text, so
/// this gives a per-card-name coverage percentage rather than per-printing.</para>
///
/// <para><c>--dump-unmatched &lt;path&gt;</c> writes every unmatched card name to
/// the given file (sorted, UTF-8, no BOM, with a header). Enables targeted
/// template-expansion work against the residue.</para>
/// </summary>
public static class CoverageReportCommand
{
    public static async Task<int> RunAsync(string[] args)
    {
        var format = ParseFormat(args);
        var dedupByName = args.Any(a => a.Equals("--dedup-by-name", StringComparison.OrdinalIgnoreCase));
        var dumpUnmatchedPath = ParseFlagValue(args, "--dump-unmatched");

        await using var db = new CardDbContext();
        await db.Database.EnsureCreatedAsync();

        // Apply schema patches (incl. CardLegalities + backfill) so older user
        // databases work without forcing a re-import.
        await CardDataSchemaPatcher.PatchAsync(db.Database.GetDbConnection(), CancellationToken.None);

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
        var entities = await query.ToListAsync();

        if (dedupByName)
        {
            // All printings of the same name share oracle text — keep one
            // representative per Name. Stable ordering for reproducibility.
            entities = entities
                .GroupBy(c => c.Name, StringComparer.Ordinal)
                .Select(g => g.First())
                .ToList();
        }

        var report = CoverageReport.Build(entities, OracleSpellBinder.Registry, new Player("Synth", 20));

        var scope = format is null ? "full pool" : $"format={format}";
        var unit = dedupByName ? "distinct oracle names" : "printings";
        SysConsole.WriteLine($"Coverage scope: {scope} ({unit})");
        SysConsole.WriteLine($"Total instants/sorceries: {report.Total}");
        var pct = report.Total == 0 ? 0 : 100.0 * report.Matched / report.Total;
        SysConsole.WriteLine($"Matched at least one template: {report.Matched} ({pct:F1}%)");
        SysConsole.WriteLine();
        SysConsole.WriteLine("Per-template hits (descending):");
        foreach (var (name, count) in report.PerTemplateHits.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key, StringComparer.Ordinal))
        {
            SysConsole.WriteLine($"  {count,6}  {name}");
        }
        SysConsole.WriteLine();
        SysConsole.WriteLine($"Unmatched ({report.UnmatchedNames.Count}). First 20:");
        foreach (var name in report.UnmatchedNames.Take(20))
        {
            SysConsole.WriteLine($"  - {name}");
        }

        if (dumpUnmatchedPath is not null)
        {
            await DumpUnmatchedAsync(dumpUnmatchedPath, report.UnmatchedNames, scope, dedupByName);
            SysConsole.WriteLine();
            SysConsole.WriteLine($"✓ Wrote {report.UnmatchedNames.Count} unmatched names → {Path.GetFullPath(dumpUnmatchedPath)}");
        }

        return 0;
    }

    private static async Task DumpUnmatchedAsync(string path, IReadOnlyList<string> names, string scope, bool dedupByName)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        var sb = new StringBuilder();
        sb.Append("# Unmatched instants/sorceries\n");
        sb.Append($"# Scope: {scope}\n");
        sb.Append($"# Unit:  {(dedupByName ? "distinct oracle names" : "printings")}\n");
        sb.Append($"# Generated: {DateTime.UtcNow:yyyy-MM-dd}\n");
        sb.Append($"# Count: {names.Count}\n");
        foreach (var n in names) sb.Append(n).Append('\n');

        var utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        await File.WriteAllTextAsync(path, sb.ToString(), utf8NoBom);
    }

    private static string? ParseFormat(string[] args) => ParseFlagValue(args, "--format");

    private static string? ParseFlagValue(string[] args, string flag)
    {
        var idx = Array.FindIndex(args, a => a.Equals(flag, StringComparison.OrdinalIgnoreCase));
        if (idx < 0 || idx + 1 >= args.Length) return null;
        var v = args[idx + 1];
        return string.IsNullOrWhiteSpace(v) ? null : v;
    }
}
