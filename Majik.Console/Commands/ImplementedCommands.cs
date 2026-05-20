using Majik.Core.CardData;
using Majik.Core.CardData.Database;
using Microsoft.EntityFrameworkCore;
using SysConsole = System.Console;

namespace Majik.Console.Commands;

/// <summary>Console handlers for the IsImplemented flag.</summary>
public static class ImplementedCommands
{
    public static async Task<int> MarkAsync(string[] args, bool value)
    {
        if (args.Length < 2)
        {
            SysConsole.Error.WriteLine("Usage: mark-implemented \"<card name>\" | mark-unimplemented \"<card name>\"");
            return 2;
        }
        var name = args[1];

        await using var db = NewDbContext();
        await CardDataSchemaPatcher.PatchAsync(db.Database.GetDbConnection(), CancellationToken.None);
        var repo = new DbCardRepository(db);

        try
        {
            repo.SetImplemented(name, value);
            SysConsole.WriteLine($"{(value ? "marked" : "unmarked")}: {name}");
            return 0;
        }
        catch (ArgumentException)
        {
            SysConsole.Error.WriteLine($"card not found: {name}");
            return 1;
        }
    }

    public static async Task<int> ListAsync(string[] args)
    {
        var limit = 50;
        for (var i = 1; i < args.Length; i++)
        {
            if (args[i] == "--limit" && i + 1 < args.Length && int.TryParse(args[i + 1], out var n))
            {
                limit = Math.Clamp(n, 1, 200);
                i++;
            }
        }

        await using var db = NewDbContext();
        await CardDataSchemaPatcher.PatchAsync(db.Database.GetDbConnection(), CancellationToken.None);
        var repo = new DbCardRepository(db);

        var rows = repo.Search(q: null, implementedOnly: true, limit);
        foreach (var c in rows.OrderBy(c => c.Name))
        {
            SysConsole.WriteLine(c.Name);
        }
        return 0;
    }

    public static async Task<int> SeedAsync(string[] args)
    {
        await using var db = NewDbContext();
        await CardDataSchemaPatcher.PatchAsync(db.Database.GetDbConnection(), CancellationToken.None);
        var repo = new DbCardRepository(db);

        var flagged = 0;
        var skipped = 0;
        foreach (var name in SeedImplementedCards.Names)
        {
            try
            {
                repo.SetImplemented(name, true);
                flagged++;
            }
            catch (ArgumentException)
            {
                skipped++;
            }
        }
        SysConsole.WriteLine($"Flagged: {flagged} implemented; skipped: {skipped} (not in DB)");
        return 0;
    }

    private static CardDbContext NewDbContext() => new();
}
