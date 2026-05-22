using Majik.Core.CardData;
using Majik.Core.CardData.Database;
using Majik.Core.CardData.SpellTemplates;
using Majik.Core.Players;
using Microsoft.EntityFrameworkCore;
using SysConsole = System.Console;

namespace Majik.Console.Commands;

/// <summary>
/// `coverage-report [--format &lt;fmt&gt;]` — walks every instant/sorcery in the
/// SQLite catalog (optionally restricted to a format-legal subset via the
/// <c>CardLegalities</c> side table), runs OracleSpellBinder.Registry against
/// each, prints matched %, per-template hit counts, and the first 20 unmatched
/// names. Gives a concrete answer to "what % of the card pool can the engine
/// actually run?" plus a steady backlog of new templates worth adding.
///
/// <para><c>--format modern</c> restricts to cards currently legal in Modern;
/// any Scryfall format key (modern, standard, pioneer, …) works. Omit the flag
/// for full-pool coverage.</para>
/// </summary>
public static class CoverageReportCommand
{
    public static async Task<int> RunAsync(string[] args)
    {
        var format = ParseFormat(args);
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

        var report = CoverageReport.Build(entities, OracleSpellBinder.Registry, new Player("Synth", 20));

        var scope = format is null ? "full pool" : $"format={format}";
        SysConsole.WriteLine($"Coverage scope: {scope}");
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
        return 0;
    }

    private static string? ParseFormat(string[] args)
    {
        var idx = Array.FindIndex(args, a => a.Equals("--format", StringComparison.OrdinalIgnoreCase));
        if (idx < 0 || idx + 1 >= args.Length) return null;
        var v = args[idx + 1];
        return string.IsNullOrWhiteSpace(v) ? null : v;
    }
}
