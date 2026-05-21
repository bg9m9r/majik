using System.Text.RegularExpressions;
using Majik.Core.CardData;
using Majik.Core.CardData.Database;
using Majik.Core.CardData.SpellTemplates;
using Microsoft.EntityFrameworkCore;
using SysConsole = System.Console;

namespace Majik.Console.Commands;

/// <summary>
/// One-off diagnostic — walks every instant/sorcery card, splits its
/// oracle text into clauses (same split rules as
/// <see cref="ClauseCompositionTemplate"/>), and reports the top
/// unmatched clauses (no template's TryExtractParams accepts them).
/// Use the output to pick the next high-yield template to add.
/// </summary>
internal static class DiagnoseClausesCommand
{
    private static readonly Regex ReminderText = new(@"\([^)]*\)", RegexOptions.Compiled);
    private static readonly Regex Whitespace = new(@"\s+", RegexOptions.Compiled);

    public static async Task RunAsync(int top = 40)
    {
        SysConsole.WriteLine("== Clause diagnostic ==");
        using var db = new CardDbContext();
        await db.Database.EnsureCreatedAsync();

        var rows = await db.Cards.AsNoTracking()
            .Where(c => c.TypeLine.Contains("Instant") || c.TypeLine.Contains("Sorcery"))
            .ToListAsync();
        var entities = rows.GroupBy(c => c.Name, StringComparer.Ordinal)
            .Select(g => g.First()).ToList();

        var registry = OracleSpellBinder.Registry;
        var unmatched = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var e in entities)
        {
            var oracle = e.OracleText ?? "";
            var cleaned = ReminderText.Replace(oracle, " ");
            cleaned = cleaned.Replace('\n', ' ');
            cleaned = Whitespace.Replace(cleaned, " ").Trim();
            var clauses = cleaned.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (clauses.Length < 2) continue;

            foreach (var raw in clauses)
            {
                var c = raw.Trim();
                if (c.Length < 4) continue;

                bool matched = false;
                foreach (var t in registry.OrderedTemplates)
                {
                    if (t.Name == "ClauseComposition") continue;
                    if (t.TryExtractParams(c + ".") is not null) { matched = true; break; }
                }
                if (matched) continue;

                // Normalize for grouping — keep only the first 80 chars,
                // collapse numbers/specific tokens to placeholders.
                var key = Normalize(c);
                unmatched[key] = unmatched.GetValueOrDefault(key) + 1;
            }
        }

        SysConsole.WriteLine($"Distinct unmatched clause forms: {unmatched.Count}");
        SysConsole.WriteLine($"Top {top} by frequency:");
        foreach (var kv in unmatched.OrderByDescending(k => k.Value).Take(top))
        {
            SysConsole.WriteLine($"  {kv.Value,5}  {kv.Key}");
        }
    }

    // Collapse numerics and quoted card names so similar clauses group.
    private static readonly Regex NumberRx = new(@"\b\d+\b", RegexOptions.Compiled);
    private static string Normalize(string clause)
    {
        var lower = clause.ToLowerInvariant();
        var n = NumberRx.Replace(lower, "N");
        if (n.Length > 100) n = n.Substring(0, 100) + "…";
        return n;
    }
}
