using System.Text.RegularExpressions;
using Majik.Core.CardData;
using Majik.Core.CardData.Database;
using Majik.Core.CardData.SpellTemplates;
using Microsoft.EntityFrameworkCore;
using SysConsole = System.Console;

namespace Majik.Console.Commands;

/// <summary>
/// Walks every instant/sorcery card. For cards composer fails on with
/// 2+ clauses, groups the unmatched clause(s) and reports the top
/// shapes — focusing on cards where exactly ONE clause is unmatched
/// (i.e. adding one template/noop unlocks the whole card via composer).
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
        var composer = registry.OrderedTemplates.First(t => t.Name == "ClauseComposition");

        var failedCards = new List<(string name, List<string> unmatched, List<string> matched)>();

        foreach (var e in entities)
        {
            var oracle = e.OracleText ?? "";
            if (composer.TryExtractParams(oracle) is not null) continue; // composer binds — skip

            // Also skip cards that any single-template already binds —
            // they're already counted in coverage; what we want is cards
            // that bind via NOTHING today but are one clause away.
            bool anySingleMatch = false;
            foreach (var t in registry.OrderedTemplates)
            {
                if (t.Name == "ClauseComposition") continue;
                if (t.TryExtractParams(oracle) is not null) { anySingleMatch = true; break; }
            }
            if (anySingleMatch) continue;

            var cleaned = ReminderText.Replace(oracle, " ");
            cleaned = cleaned.Replace('\n', ' ');
            cleaned = Whitespace.Replace(cleaned, " ").Trim();
            var clauses = cleaned.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (clauses.Length < 2) continue;

            var unmatched = new List<string>();
            var matched = new List<string>();
            foreach (var c in clauses)
            {
                var clause = c.Trim();
                if (clause.Length < 3) continue;
                bool ok = false;
                foreach (var t in registry.OrderedTemplates)
                {
                    if (t.Name == "ClauseComposition") continue;
                    if (t.TryExtractParams(clause + ".") is not null) { ok = true; matched.Add(t.Name); break; }
                }
                if (!ok) unmatched.Add(clause);
            }

            failedCards.Add((e.Name, unmatched, matched));
        }

        var oneOff = failedCards
            .Where(t => t.unmatched.Count == 1 && t.matched.Count >= 1)
            .ToList();

        SysConsole.WriteLine($"Total cards composer fails (2+ clauses): {failedCards.Count}");
        SysConsole.WriteLine($"One-clause-away cards: {oneOff.Count}");
        SysConsole.WriteLine();

        var byKey = oneOff
            .GroupBy(t => Normalize(t.unmatched[0]), StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(g => g.Count())
            .Take(top);

        SysConsole.WriteLine($"Top {top} unmatched clauses where one template would unlock the card:");
        foreach (var g in byKey)
        {
            var sample = g.First();
            SysConsole.WriteLine($"  {g.Count(),5}  {g.Key}");
            SysConsole.WriteLine($"        e.g. {sample.name}");
        }
    }

    private static readonly Regex NumberRx = new(@"\b\d+\b", RegexOptions.Compiled);
    private static string Normalize(string clause)
    {
        var lower = clause.ToLowerInvariant();
        var n = NumberRx.Replace(lower, "N");
        if (n.Length > 100) n = n.Substring(0, 100) + "…";
        return n;
    }
}
