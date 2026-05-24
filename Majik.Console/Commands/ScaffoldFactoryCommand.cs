using Majik.Core.CardData;
using Majik.Core.CardData.Coverage;
using Majik.Core.CardData.Database;
using Majik.Core.Players;
using Microsoft.EntityFrameworkCore;
using SysConsole = System.Console;

namespace Majik.Console.Commands;

/// <summary>
/// <c>scaffold-factory &lt;Card Name&gt; [--out &lt;path&gt;] [--force]</c>
///
/// Generates a starter <c>*Factory.cs</c> file pre-filled with the
/// boilerplate (oracle-text docstring, <c>[CardName]</c> attribute, ctor
/// + supertypes / subtypes / power / toughness pulled from Scryfall data).
/// Author opens the file, fills in <c>// TODO: resolve body</c>.
///
/// Lookup order:
/// <list type="number">
///   <item>Local <see cref="ICardRepository.GetByName"/> against cards.db.</item>
///   <item>Fallback: <c>https://api.scryfall.com/cards/named?exact=…</c></item>
/// </list>
///
/// Coverage tier is reported up front: if the card already classifies as
/// <see cref="CoverageTier.NamedFactory"/> / <see cref="CoverageTier.SpellBound"/> /
/// <see cref="CoverageTier.KeywordOnly"/>, the operator is prompted before a
/// bespoke factory is written (skip the prompt with <c>--force</c>).
/// </summary>
public static class ScaffoldFactoryCommand
{
    public static async Task<int> RunAsync(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);

        if (args.Length < 2 || string.IsNullOrWhiteSpace(args[1]) || args[1].StartsWith("--"))
        {
            PrintUsage();
            return 2;
        }

        // First non-flag positional is the card name. Quoted by the shell.
        var cardName = args[1];
        var force = args.Any(a => a.Equals("--force", StringComparison.OrdinalIgnoreCase));
        var outPath = ParseFlagValue(args, "--out");

        // --------- 1. Resolve CardEntity -----------------------------------
        SysConsole.WriteLine($"=== scaffold-factory: {cardName} ===");
        SysConsole.WriteLine();

        await using var db = new CardDbContext();
        await db.Database.EnsureCreatedAsync();
        await CardDataSchemaPatcher.PatchAsync(db.Database.GetDbConnection(), CancellationToken.None);
        var repo = new DbCardRepository(db);

        CardEntity? entity = repo.GetByName(cardName);
        var sourceLabel = entity != null ? "local cards.db" : null;

        if (entity is null)
        {
            SysConsole.WriteLine($"Card not in local DB; trying Scryfall API…");
            entity = await ScryfallNamedLookup.FetchAsync(cardName);
            if (entity != null) sourceLabel = "Scryfall API";
        }

        if (entity is null)
        {
            SysConsole.WriteLine($"Error: card '{cardName}' not found in local DB or via Scryfall.");
            return 1;
        }

        SysConsole.WriteLine($"Resolved '{entity.Name}' via {sourceLabel}.");
        SysConsole.WriteLine($"  Type line: {entity.TypeLine}");
        if (!string.IsNullOrEmpty(entity.ManaCost)) SysConsole.WriteLine($"  Mana cost: {entity.ManaCost}");
        SysConsole.WriteLine();

        // --------- 2. Coverage tier ----------------------------------------
        var tier = ClassifyTier(entity);
        SysConsole.WriteLine($"Coverage tier: {tier}");
        if (tier is CoverageTier.NamedFactory or CoverageTier.SpellBound or CoverageTier.KeywordOnly)
        {
            SysConsole.WriteLine($"Card '{entity.Name}' already classified as {tier} (template/factory-covered).");
            if (!force)
            {
                SysConsole.Write("Are you sure you want to write a bespoke factory? [y/N] ");
                var input = SysConsole.ReadLine()?.Trim();
                if (!string.Equals(input, "y", StringComparison.OrdinalIgnoreCase))
                {
                    SysConsole.WriteLine("Aborted.");
                    return 0;
                }
            }
            else
            {
                SysConsole.WriteLine("(--force set, skipping confirmation prompt.)");
            }
        }
        SysConsole.WriteLine();

        // --------- 3. Generate ---------------------------------------------
        var result = ScaffoldFactoryGenerator.Generate(entity);

        // --------- 4. Resolve target path ----------------------------------
        string targetPath;
        if (!string.IsNullOrWhiteSpace(outPath))
        {
            targetPath = Path.GetFullPath(outPath);
        }
        else
        {
            var factoriesDir = LocateFactoriesDirectory(Directory.GetCurrentDirectory());
            if (factoriesDir is null)
            {
                SysConsole.WriteLine("Error: could not locate 'Majik.Core/CardData/Factories' relative to the working directory. Pass --out <path> explicitly.");
                return 1;
            }
            targetPath = Path.Combine(factoriesDir, result.FileName);
        }

        // --------- 5. Overwrite gate ---------------------------------------
        if (File.Exists(targetPath) && !force)
        {
            SysConsole.WriteLine($"Factory already exists at {targetPath}. Use --force to overwrite.");
            return 1;
        }

        var parent = Path.GetDirectoryName(targetPath);
        if (!string.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent);
        await File.WriteAllTextAsync(targetPath, result.SourceText);

        // --------- 6. Next steps -------------------------------------------
        SysConsole.WriteLine($"Wrote {targetPath}");
        SysConsole.WriteLine();
        SysConsole.WriteLine("Next steps:");
        SysConsole.WriteLine($"  1. Edit {targetPath} to fill in `// TODO` blocks.");
        SysConsole.WriteLine($"  2. Add tests at `Majik.Core.Tests/CardData/Factories/{result.Slug}FactoryTests.cs`.");
        SysConsole.WriteLine("  3. Run `dotnet build Majik.sln` to verify the source-generator picks up the [CardName] attribute.");

        return 0;
    }

    private static void PrintUsage()
    {
        SysConsole.WriteLine("Usage: Majik.Console scaffold-factory <Card Name> [--out <path>] [--force]");
        SysConsole.WriteLine();
        SysConsole.WriteLine("Generates a starter factory file from Scryfall data. Pulls the card via");
        SysConsole.WriteLine("ICardRepository.GetByName against cards.db; falls back to Scryfall's");
        SysConsole.WriteLine("/cards/named?exact API if not present locally.");
    }

    /// <summary>
    /// Classify <paramref name="entity"/> against the production factory
    /// pipeline. Builds a one-off <see cref="ScryfallCardFactory"/> backed
    /// by an in-memory single-row repo so the classifier sees the same
    /// dispatch surface a real game would.
    /// </summary>
    public static CoverageTier ClassifyTier(CardEntity entity)
    {
        var repo = new SingleEntityRepository(entity);
        var factory = new ScryfallCardFactory(repo);
        var classifier = new CoverageClassifier(factory, new Player("scaffold-stub", 20));
        try
        {
            return classifier.Classify(entity);
        }
        catch
        {
            // Defensive: classification should never crash the scaffolder.
            return CoverageTier.Unimplemented;
        }
    }

    /// <summary>
    /// Walk up from <paramref name="startDir"/> looking for the
    /// <c>Majik.Core/CardData/Factories</c> directory. Returns null when we
    /// hit the filesystem root without finding it (e.g. invoked from outside
    /// the repo).
    /// </summary>
    public static string? LocateFactoriesDirectory(string startDir)
    {
        var dir = new DirectoryInfo(startDir);
        while (dir != null)
        {
            var candidate = Path.Combine(dir.FullName, "Majik.Core", "CardData", "Factories");
            if (Directory.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        return null;
    }

    private static string? ParseFlagValue(string[] args, string flag)
    {
        var idx = Array.FindIndex(args, a => a.Equals(flag, StringComparison.OrdinalIgnoreCase));
        if (idx < 0 || idx + 1 >= args.Length) return null;
        var v = args[idx + 1];
        return string.IsNullOrWhiteSpace(v) ? null : v;
    }

    /// <summary>
    /// One-row in-memory <see cref="ICardRepository"/> used to feed the
    /// coverage classifier when the card was sourced from Scryfall (no DB
    /// row) or when we want classification to see only the just-resolved
    /// entity.
    /// </summary>
    private sealed class SingleEntityRepository : ICardRepository
    {
        private readonly CardEntity _entity;
        public SingleEntityRepository(CardEntity entity) { _entity = entity; }

        public CardEntity? GetByName(string name) =>
            string.Equals(name, _entity.Name, StringComparison.Ordinal) ? _entity : null;

        public IReadOnlyList<CardEntity> GetByNames(IEnumerable<string> names) =>
            names.Select(GetByName).OfType<CardEntity>().ToList();

        public IReadOnlyList<CardEntity> Search(string? q, bool implementedOnly, int limit,
            IReadOnlyList<string>? colors = null,
            IReadOnlyList<string>? types = null,
            IReadOnlyList<int>? cmcBuckets = null) =>
            throw new NotSupportedException("scaffold-factory does not search.");

        public bool IsImplemented(string name) => _entity.IsImplemented;
        public void SetImplemented(string name, bool value) =>
            throw new NotSupportedException("scaffold-factory is read-only.");
    }
}
