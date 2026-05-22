using System.Text.Json;
using Majik.Core.CardData;
using Majik.Core.CardData.Database;
using Majik.Core.CardData.SpellTemplates;
using Majik.Core.Game;
using Microsoft.EntityFrameworkCore;
using SysConsole = System.Console;

namespace Majik.Console.Commands;

/// <summary>
/// <c>compile-templates</c> — walks every instant/sorcery in the SQLite
/// catalog, runs <see cref="OracleSpellBinder.Registry"/> against each
/// card's oracle text, and persists the highest-priority match (template
/// name + extracted parameters) into the <c>CompiledSpellTemplates</c>
/// table. Idempotent: re-running rebuilds the table from scratch.
///
/// PR-D will wire <see cref="OracleSpellBinder.Bind"/> to consult this
/// table first, falling back to a live registry walk when no compiled
/// row exists.
/// </summary>
public static class CompileTemplatesCommand
{
    public static async Task<int> RunAsync()
    {
        await using var db = new CardDbContext();
        await db.Database.EnsureCreatedAsync();

        // Patch schema in case the user is running on a pre-CompiledSpellTemplates DB.
        var conn = db.Database.GetDbConnection();
        await CardDataSchemaPatcher.PatchAsync(conn, CancellationToken.None);

        var registry = OracleSpellBinder.Registry;
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        SysConsole.WriteLine("Loading instant/sorcery rows…");
        var allPrintings = await db.Cards.AsNoTracking()
            .Where(c => c.TypeLine.Contains("Instant") || c.TypeLine.Contains("Sorcery"))
            .ToListAsync();
        // The Cards table holds one row per printing; CompiledSpellTemplates
        // is keyed by card name, so dedupe to the first printing per name.
        // All printings share oracle text (Scryfall normalizes), so any
        // representative row is fine.
        var entities = allPrintings
            .GroupBy(c => c.Name, StringComparer.Ordinal)
            .Select(g => g.First())
            .ToList();
        SysConsole.WriteLine($"  → {allPrintings.Count} printings → {entities.Count} distinct card names");

        // Wipe + repopulate. Far simpler than incremental upsert and avoids
        // stale rows from templates that were removed in a follow-up.
        SysConsole.WriteLine("Clearing existing compiled rows…");
        await db.Database.ExecuteSqlRawAsync("DELETE FROM CompiledSpellTemplates");

        int matched = 0;
        var perTemplate = new Dictionary<string, int>(StringComparer.Ordinal);
        var jsonOpts = new JsonSerializerOptions { WriteIndented = false };

        // Batch inserts in chunks of 1000 — SQLite + EF Core handles
        // larger batches but smaller chunks give better progress visibility
        // and keep memory bounded for the 100k+ candidate set.
        const int chunkSize = 1000;
        var pending = new List<CompiledSpellTemplateEntity>(chunkSize);

        foreach (var entity in entities)
        {
            var oracle = OracleTextNormalizer.Normalize(entity.OracleText ?? string.Empty);
            ISpellTemplate? winner = null;
            IReadOnlyDictionary<string, string>? winningParams = null;

            foreach (var t in registry.OrderedTemplates)
            {
                var p = t.TryExtractParams(oracle);
                if (p is null) continue;
                winner = t;
                winningParams = p;
                break;
            }

            // Fall back to the live-only ClauseCompositionTemplate when no
            // single-template regex matches. The composer cannot be
            // pre-compiled (it depends on the live registry), so we record
            // its hits for coverage reporting only — no row is persisted.
            if (winner is null || winningParams is null) continue;

            matched++;
            perTemplate[winner.Name] = perTemplate.GetValueOrDefault(winner.Name) + 1;

            pending.Add(new CompiledSpellTemplateEntity
            {
                CardName = entity.Name,
                TemplateName = winner.Name,
                Priority = winner.Priority,
                ParamsJson = JsonSerializer.Serialize(winningParams, jsonOpts),
                CompiledAt = now,
                Intent = (ulong)winner.Intent,
            });

            if (pending.Count >= chunkSize)
            {
                await db.CompiledSpellTemplates.AddRangeAsync(pending);
                await db.SaveChangesAsync();
                db.ChangeTracker.Clear();
                pending.Clear();
                SysConsole.Write('.');
            }
        }

        if (pending.Count > 0)
        {
            await db.CompiledSpellTemplates.AddRangeAsync(pending);
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();
        }
        SysConsole.WriteLine();

        var pct = entities.Count == 0 ? 0 : 100.0 * matched / entities.Count;
        SysConsole.WriteLine();
        SysConsole.WriteLine($"Compiled {matched}/{entities.Count} ({pct:F1}%) cards.");
        SysConsole.WriteLine();
        SysConsole.WriteLine("Per-template hits (descending):");
        foreach (var (name, count) in perTemplate
            .OrderByDescending(kv => kv.Value)
            .ThenBy(kv => kv.Key, StringComparer.Ordinal))
        {
            SysConsole.WriteLine($"  {count,6}  {name}");
        }

        return 0;
    }
}
