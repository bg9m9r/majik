using Majik.Core.CardData;
using Majik.Core.CardData.Database;
using Majik.Core.CardData.SpellTemplates;
using Majik.Core.Players;
using Microsoft.EntityFrameworkCore;
using SysConsole = System.Console;

namespace Majik.Console.Commands;

/// <summary>
/// `coverage-report` — walks every instant/sorcery in the SQLite
/// catalog, runs OracleSpellBinder.Registry against each, prints
/// matched %, per-template hit counts, and the first 20 unmatched
/// names. Gives a concrete answer to "what % of the card pool can
/// the engine actually run?" plus a steady backlog of new templates
/// worth adding.
/// </summary>
public static class CoverageReportCommand
{
    public static async Task<int> RunAsync()
    {
        await using var db = new CardDbContext();
        var entities = await db.Cards.AsNoTracking().ToListAsync();
        var report = CoverageReport.Build(entities, OracleSpellBinder.Registry, new Player("Synth", 20));

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
}
