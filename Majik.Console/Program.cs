using Majik.Core.CardData.Database;
using Majik.Core.CardData.Import;
using Majik.Core.CardData.Parsing;
using Microsoft.EntityFrameworkCore;
using System;
using System.Text.Json;
using DotNetEnv;

namespace Majik.Console;

/// <summary>
/// Import tool for Scryfall bulk data JSON.
/// </summary>
class Program
{
    static async Task Main(string[] args)
    {
        // Load .env file if it exists (for local API keys)
        // Search up the directory tree to find .env file
        var currentDir = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (currentDir != null)
        {
            var envPath = Path.Combine(currentDir.FullName, ".env");
            if (File.Exists(envPath))
            {
                Env.Load(envPath);
                break;
            }
            currentDir = currentDir.Parent;
        }
        
        // Check for commands
        if (args.Length > 0)
        {
            if (args[0].Equals("import", StringComparison.OrdinalIgnoreCase))
            {
                await HandleImportCommand(args);
                return;
            }
            else if (args[0].Equals("analyze-keywords", StringComparison.OrdinalIgnoreCase))
            {
                await HandleAnalyzeKeywordsCommandAsync(args);
                return;
            }
            else if (args[0].Equals("ingest-claude-results", StringComparison.OrdinalIgnoreCase))
            {
                await HandleIngestClaudeResultsCommandAsync(args);
                return;
            }
            else if (args[0].Equals("play-triggers", StringComparison.OrdinalIgnoreCase))
            {
                var scenario = args.Length > 1 ? args[1] : "all";
                TriggerPlayground.Run(scenario);
                return;
            }
            else if (args[0].Equals("fetch-bulk", StringComparison.OrdinalIgnoreCase))
            {
                await HandleFetchBulkCommandAsync(args);
                return;
            }
            else if (args[0].Equals("fetch-set", StringComparison.OrdinalIgnoreCase))
            {
                await HandleFetchSetCommandAsync(args);
                return;
            }
            else if (args[0].Equals("compare-sets", StringComparison.OrdinalIgnoreCase))
            {
                await HandleCompareSetsCommandAsync(args);
                return;
            }
        }

        System.Console.WriteLine("Usage:");
        System.Console.WriteLine("  Majik.Console import <path-to-json-file>");
        System.Console.WriteLine("  Majik.Console fetch-bulk [oracle-cards|default-cards|all-cards|unique-artwork] [--no-import]");
        System.Console.WriteLine("  Majik.Console fetch-set <set-code> [--no-import]");
        System.Console.WriteLine("  Majik.Console compare-sets [--include-digital] [--missing-only] [--partial-only] [--sync] [--dry-run] [--max N]");
        System.Console.WriteLine("  Majik.Console analyze-keywords <path-to-csv-file>");
        System.Console.WriteLine("  Majik.Console ingest-claude-results <path-to-jsonl-file>");
        System.Console.WriteLine("  Majik.Console play-triggers [etb|apnap|intervening-if|delayed|all]");
        System.Console.WriteLine();
        TriggerPlayground.PrintScenarios();
    }

    /// <summary>
    /// Handles the import command for importing Scryfall card data.
    /// Usage: Majik.Console import <path-to-json-file>
    /// </summary>
    static async Task HandleImportCommand(string[] args)
    {
        if (args.Length < 2)
        {
            System.Console.WriteLine("Usage: Majik.Console import <path-to-json-file>");
            System.Console.WriteLine("Example: Majik.Console import /home/brett/Documents/all-cards.json");
            return;
        }

        var jsonFilePath = args[1];
        
        if (!File.Exists(jsonFilePath))
        {
            System.Console.WriteLine($"Error: File not found: {jsonFilePath}");
            return;
        }

        System.Console.WriteLine($"=== Majik Card Import Tool ===\n");
        System.Console.WriteLine($"Importing cards from: {jsonFilePath}");
        
        var fileInfo = new FileInfo(jsonFilePath);
        System.Console.WriteLine($"File size: {fileInfo.Length / (1024.0 * 1024.0):F2} MB");
        System.Console.WriteLine("This may take several minutes for large files...\n");
        System.Console.WriteLine($"  Database location: {GetDatabasePath()}");

        var importer = new ScryfallJsonImporter(progress: new Progress<ImportProgress>(ReportProgress));

        try
        {
            var startTime = DateTime.Now;
            var count = await importer.ImportFromFileAsync(jsonFilePath);
            var duration = DateTime.Now - startTime;

            System.Console.WriteLine($"\n\n✓ Import complete!");
            System.Console.WriteLine($"  Total cards processed: {count}");
            System.Console.WriteLine($"  Time elapsed: {duration.TotalMinutes:F2} minutes");
        }
        catch (Exception ex)
        {
            System.Console.WriteLine($"\n✗ Error during import: {ex.Message}");
            System.Console.WriteLine($"Stack trace: {ex.StackTrace}");
        }
    }

    /// <summary>
    /// fetch-bulk [type] [--no-import]
    /// Downloads the named Scryfall bulk export to a temp file; optionally
    /// imports into the local DB. Defaults to oracle-cards (smallest set of
    /// gameplay-relevant fields, one row per unique card).
    /// </summary>
    static async Task HandleFetchBulkCommandAsync(string[] args)
    {
        var bulkType = args.Length > 1 && !args[1].StartsWith("--")
            ? args[1] : "oracle-cards";
        var skipImport = args.Any(a => a.Equals("--no-import", StringComparison.OrdinalIgnoreCase));

        var tmpDir = Path.Combine(Path.GetTempPath(), "majik-scryfall");
        Directory.CreateDirectory(tmpDir);
        var destPath = Path.Combine(tmpDir, $"{bulkType}.json");

        System.Console.WriteLine($"=== Majik Scryfall Bulk Download ===\n");
        var downloader = new ScryfallDownloader(
            log: new Progress<string>(m => System.Console.WriteLine($"  {m}")));
        try
        {
            await downloader.DownloadBulkAsync(bulkType, destPath);
        }
        catch (Exception ex)
        {
            System.Console.WriteLine($"\n✗ Download failed: {ex.Message}");
            return;
        }

        if (skipImport)
        {
            System.Console.WriteLine($"\n--no-import set; JSON left at {destPath}");
            return;
        }
        await HandleImportCommand(new[] { "import", destPath });
    }

    /// <summary>
    /// fetch-set <set-code> [--no-import]
    /// Pulls every printing from a single set via /cards/search. Useful when
    /// a new release lands and you don't want to re-pull the whole bulk file.
    /// </summary>
    static async Task HandleFetchSetCommandAsync(string[] args)
    {
        if (args.Length < 2)
        {
            System.Console.WriteLine("Usage: Majik.Console fetch-set <set-code> [--no-import]");
            return;
        }
        var setCode = args[1];
        var skipImport = args.Any(a => a.Equals("--no-import", StringComparison.OrdinalIgnoreCase));

        var tmpDir = Path.Combine(Path.GetTempPath(), "majik-scryfall");
        Directory.CreateDirectory(tmpDir);
        var destPath = Path.Combine(tmpDir, $"set-{setCode.ToLowerInvariant()}.json");

        System.Console.WriteLine($"=== Majik Scryfall Set Download: {setCode} ===\n");
        var downloader = new ScryfallDownloader(
            log: new Progress<string>(m => System.Console.WriteLine($"  {m}")));
        try
        {
            await downloader.DownloadSetAsync(setCode, destPath);
        }
        catch (Exception ex)
        {
            System.Console.WriteLine($"\n✗ Download failed: {ex.Message}");
            return;
        }

        if (skipImport)
        {
            System.Console.WriteLine($"\n--no-import set; JSON left at {destPath}");
            return;
        }
        await HandleImportCommand(new[] { "import", destPath });
    }

    /// <summary>
    /// compare-sets [--include-digital] [--missing-only] [--partial-only]
    ///              [--sync] [--dry-run] [--max NN]
    /// Lists Scryfall's known sets, compares against distinct Set codes in
    /// the local DB, prints a diff.
    ///
    /// --sync: after diff, download + import every set in the missing /
    ///   partial buckets (respects --missing-only / --partial-only).
    /// --dry-run: print the sync plan (which sets would be fetched) but
    ///   don't hit the network.
    /// --max NN: cap the sync to the first NN sets (useful for testing).
    /// </summary>
    static async Task HandleCompareSetsCommandAsync(string[] args)
    {
        var includeDigital = args.Any(a => a.Equals("--include-digital", StringComparison.OrdinalIgnoreCase));
        var missingOnly = args.Any(a => a.Equals("--missing-only", StringComparison.OrdinalIgnoreCase));
        var partialOnly = args.Any(a => a.Equals("--partial-only", StringComparison.OrdinalIgnoreCase));
        var sync = args.Any(a => a.Equals("--sync", StringComparison.OrdinalIgnoreCase));
        var dryRun = args.Any(a => a.Equals("--dry-run", StringComparison.OrdinalIgnoreCase));
        var maxIdx = Array.FindIndex(args, a => a.Equals("--max", StringComparison.OrdinalIgnoreCase));
        var maxSets = int.MaxValue;
        if (maxIdx >= 0 && maxIdx + 1 < args.Length && int.TryParse(args[maxIdx + 1], out var parsedMax))
        {
            maxSets = parsedMax;
        }

        System.Console.WriteLine($"=== Majik Set Comparison ===\n");

        var downloader = new ScryfallDownloader(
            log: new Progress<string>(m => System.Console.WriteLine($"  {m}")));

        IReadOnlyList<ScryfallSet> scryfallSets;
        try
        {
            scryfallSets = await downloader.ListSetsAsync();
        }
        catch (Exception ex)
        {
            System.Console.WriteLine($"\n✗ Set list fetch failed: {ex.Message}");
            return;
        }

        if (!includeDigital)
        {
            scryfallSets = scryfallSets.Where(s => !s.Digital).ToList();
        }

        await using var db = new CardDbContext();
        var localCounts = db.Cards
            .Where(c => c.Set != null)
            .GroupBy(c => c.Set!.ToLower())
            .Select(g => new { Code = g.Key, Count = g.Count() })
            .ToDictionary(g => g.Code, g => g.Count);

        var missing = new List<ScryfallSet>();
        var partial = new List<(ScryfallSet s, int local)>();
        var complete = 0;

        foreach (var s in scryfallSets)
        {
            var code = s.Code.ToLowerInvariant();
            if (!localCounts.TryGetValue(code, out var local))
            {
                missing.Add(s);
                continue;
            }
            if (local < s.CardCount) partial.Add((s, local));
            else complete++;
        }

        var localCodesOnly = localCounts.Keys
            .Where(c => !scryfallSets.Any(s => s.Code.Equals(c, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        var totalLocalCards = localCounts.Values.Sum();
        var totalScryfallCards = scryfallSets.Sum(s => s.CardCount);

        System.Console.WriteLine();
        System.Console.WriteLine($"Local DB:  {localCounts.Count} sets / {totalLocalCards} cards");
        System.Console.WriteLine($"Scryfall:  {scryfallSets.Count} sets / {totalScryfallCards} cards"
            + (includeDigital ? "" : " (excluding digital)"));
        System.Console.WriteLine();

        var showMissing = !partialOnly;
        var showPartial = !missingOnly;

        if (showMissing && missing.Count > 0)
        {
            System.Console.WriteLine($"Missing entirely ({missing.Count}):");
            foreach (var s in missing.OrderByDescending(s => s.ReleasedAt ?? ""))
            {
                System.Console.WriteLine($"  {s.Code,-6} {s.Name} ({s.ReleasedAt ?? "?"}) — {s.CardCount} cards [{s.SetType}]");
            }
            System.Console.WriteLine();
        }

        if (showPartial && partial.Count > 0)
        {
            System.Console.WriteLine($"Partial ({partial.Count}):");
            foreach (var (s, local) in partial.OrderByDescending(p => p.s.CardCount - p.local))
            {
                System.Console.WriteLine($"  {s.Code,-6} {s.Name} — local {local} / scryfall {s.CardCount} (-{s.CardCount - local})");
            }
            System.Console.WriteLine();
        }

        if (!missingOnly && !partialOnly)
        {
            System.Console.WriteLine($"Up-to-date: {complete} sets");
            if (localCodesOnly.Count > 0)
            {
                System.Console.WriteLine($"Local-only codes ({localCodesOnly.Count}): {string.Join(", ", localCodesOnly.Take(20))}"
                    + (localCodesOnly.Count > 20 ? $" … +{localCodesOnly.Count - 20} more" : ""));
            }
        }

        // ---- --sync: fetch + import each set in the chosen buckets ----
        if (!sync) return;

        var syncList = new List<ScryfallSet>();
        if (!partialOnly) syncList.AddRange(missing);
        if (!missingOnly) syncList.AddRange(partial.Select(p => p.s));

        if (syncList.Count == 0)
        {
            System.Console.WriteLine("\nNothing to sync.");
            return;
        }

        // Newest first — most likely to be the cards the user actually wants.
        syncList = syncList
            .OrderByDescending(s => s.ReleasedAt ?? "")
            .Take(maxSets)
            .ToList();

        System.Console.WriteLine();
        System.Console.WriteLine($"Sync plan: {syncList.Count} set(s) → /tmp/majik-scryfall/");
        foreach (var s in syncList)
        {
            System.Console.WriteLine($"  {s.Code,-6} {s.Name} ({s.ReleasedAt ?? "?"}) — {s.CardCount} cards");
        }
        if (dryRun)
        {
            System.Console.WriteLine("\n--dry-run: nothing downloaded.");
            return;
        }

        System.Console.WriteLine();
        var tmpDir = Path.Combine(Path.GetTempPath(), "majik-scryfall");
        Directory.CreateDirectory(tmpDir);

        int ok = 0, failed = 0;
        for (var i = 0; i < syncList.Count; i++)
        {
            var s = syncList[i];
            System.Console.WriteLine($"\n[{i + 1}/{syncList.Count}] {s.Code} — {s.Name}");
            var destPath = Path.Combine(tmpDir, $"set-{s.Code.ToLowerInvariant()}.json");
            try
            {
                await downloader.DownloadSetAsync(s.Code, destPath);
                await HandleImportCommand(new[] { "import", destPath });
                ok++;
            }
            catch (Exception ex)
            {
                System.Console.WriteLine($"  ✗ {s.Code} failed: {ex.Message}");
                failed++;
            }
        }

        System.Console.WriteLine($"\nSync complete: {ok} succeeded, {failed} failed.");
    }

    /// <summary>
    /// Reports import progress to the console.
    /// </summary>
    static void ReportProgress(ImportProgress progress)
    {
        var percent = progress.ProgressPercent;
        var bytesProcessedMB = progress.BytesProcessed / (1024.0 * 1024.0);
        var totalBytesMB = progress.TotalBytes / (1024.0 * 1024.0);

        System.Console.Write($"\rProgress: {percent:F1}% ({bytesProcessedMB:F1}/{totalBytesMB:F1} MB) - " +
                            $"Processed: {progress.Processed}, " +
                            $"Imported: {progress.Imported}, " +
                            $"Updated: {progress.Updated}, " +
                            $"Skipped: {progress.Skipped}");
    }

    /// <summary>
    /// Gets the database path (same logic as CardDbContext).
    /// </summary>
    static string GetDatabasePath()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var majikDir = Path.Combine(appData, "Majik");
        System.Console.WriteLine($"Database path: {Path.Combine(majikDir, "cards.db")}");
        return Path.Combine(majikDir, "cards.db");
    }

    /// <summary>
    /// Handles the analyze-keywords command for analyzing and categorizing keywords.
    /// </summary>
    static async Task HandleAnalyzeKeywordsCommandAsync(string[] args)
    {
        if (args.Length < 2)
        {
            System.Console.WriteLine("Usage: Majik.Console analyze-keywords <path-to-csv-file> [--use-claude]");
            System.Console.WriteLine("   OR: Majik.Console analyze-keywords --from-database [--use-claude]");
            System.Console.WriteLine("\nOptions:");
            System.Console.WriteLine("  --from-database  Read keywords from the database (Cards.Keywords) instead of CSV");
            System.Console.WriteLine("  --use-claude     Use Claude API to analyze unknown keywords (requires ANTHROPIC_API_KEY)");
            return;
        }

        var useClaude = args.Contains("--use-claude", StringComparer.OrdinalIgnoreCase);
        var fromDatabase = args.Contains("--from-database", StringComparer.OrdinalIgnoreCase);
        
        string? csvPath = null;
        if (!fromDatabase)
        {
            // Find the first argument that's not a flag
            csvPath = args.Skip(1).FirstOrDefault(arg => 
                !arg.StartsWith("--", StringComparison.OrdinalIgnoreCase));
            
            if (string.IsNullOrEmpty(csvPath))
            {
                System.Console.WriteLine("Error: CSV file path is required when not using --from-database");
                return;
            }
            
            if (!File.Exists(csvPath))
            {
                System.Console.WriteLine($"Error: CSV file not found: {csvPath}");
                return;
            }
        }

        System.Console.WriteLine("=== Keyword Analysis Tool ===\n");
        if (fromDatabase)
        {
            System.Console.WriteLine("Reading keywords from database...");
        }
        else
        {
            System.Console.WriteLine($"Analyzing keywords from: {csvPath}");
        }
        if (useClaude)
        {
            System.Console.WriteLine("Using Claude API for unknown keywords (this will take longer and use API credits)\n");
        }
        System.Console.WriteLine("This may take a few minutes...\n");

        try
        {
            using var dbContext = new CardDbContext();
            await dbContext.Database.EnsureCreatedAsync();

            // Ensure KeywordMetadata table exists (in case database was created before this entity)
            await EnsureKeywordMetadataTableExistsAsync(dbContext);

            // Patch schema to add IsImplemented column to Cards table
            try
            {
                await CardDataSchemaPatcher.PatchAsync(dbContext.Database.GetDbConnection(), CancellationToken.None);
            }
            catch (Exception patchEx)
            {
                System.Console.WriteLine($"Warning: Failed to patch card database schema: {patchEx.Message}");
                System.Console.WriteLine("Continuing with analysis...\n");
            }

            var analyzer = new KeywordAnalyzer();
            ClaudeKeywordAnalyzer? claudeAnalyzer = null;
            
            if (useClaude)
            {
                try
                {
                    System.Console.WriteLine("Initializing Claude API analyzer...");
                    claudeAnalyzer = new ClaudeKeywordAnalyzer(dbContext: dbContext);
                    System.Console.WriteLine("Claude API ready!\n");
                }
                catch (Exception ex)
                {
                    System.Console.WriteLine($"Warning: Could not initialize Claude API: {ex.Message}");
                    System.Console.WriteLine("Continuing without Claude analysis...\n");
                }
            }
            
            // Load card names from database to filter them out
            System.Console.WriteLine("Loading card names from database...");
            await analyzer.LoadCardNamesAsync(dbContext);
            System.Console.WriteLine($"Loaded {analyzer.GetCardNameCount()} card names\n");

            // Get keywords from CSV or database
            List<KeywordAnalysis> analyses;
            if (fromDatabase)
            {
                // Load keywords from database
                var keywords = await analyzer.LoadKeywordsFromDatabaseAsync(
                    dbContext,
                    (message) => System.Console.Write(message));
                
                if (keywords.Count == 0)
                {
                    System.Console.WriteLine("No keywords found in database. Make sure cards have been imported first.");
                    return;
                }
                
                // Analyze keywords from list
                System.Console.WriteLine("Analyzing keywords...\n");
                analyses = await analyzer.AnalyzeKeywordsFromListAsync(
                    keywords,
                    (message) => System.Console.Write(message));
            }
            else
            {
                // Analyze keywords from CSV
                System.Console.WriteLine("Analyzing keywords...\n");
                analyses = await analyzer.AnalyzeKeywordsFromCsvAsync(
                    csvPath!, 
                    (message) => System.Console.Write(message));
            }
            
            System.Console.WriteLine($"\n✓ Analysis complete: {analyses.Count} keywords analyzed\n");
            
            // Get implementation notes for official keywords if Claude is available
            Dictionary<string, ClaudeImplementationNotes>? implementationNotes = null;
            if (claudeAnalyzer != null)
            {
                // Use Claude Message Batches API (50% discount, processes all at once)
                implementationNotes = await analyzer.GetImplementationNotesForOfficialKeywordsAsync(
                    analyses,
                    claudeAnalyzer,
                    (message) => System.Console.Write(message));
            }

            // Generate report
            var report = analyzer.GenerateReport(analyses);
            System.Console.WriteLine("=== Analysis Report ===\n");
            System.Console.WriteLine($"Total Keywords: {report.TotalKeywords}");
            System.Console.WriteLine("\nBy Category:");
            foreach (var category in report.ByCategory.OrderByDescending(kvp => kvp.Value))
            {
                System.Console.WriteLine($"  {category.Key}: {category.Value}");
            }

            System.Console.WriteLine($"\nOfficial Keywords: {report.OfficialKeywords.Count}");
            System.Console.WriteLine($"Parameterized Keywords: {report.ParameterizedKeywords.Count}");
            System.Console.WriteLine($"Card Names (filtered): {report.CardNames.Count}");
            System.Console.WriteLine($"Custom Keywords: {report.CustomKeywords.Count}");
            System.Console.WriteLine($"Unknown Keywords: {report.UnknownKeywords.Count}\n");

            // Save to database
            System.Console.WriteLine("Saving results to database...");
            await analyzer.SaveAnalysesToDatabaseAsync(dbContext, analyses, implementationNotes);
            System.Console.WriteLine("Results saved to database successfully!\n");
            
            if (implementationNotes != null && implementationNotes.Count > 0)
            {
                System.Console.WriteLine($"✓ Implementation notes retrieved for {implementationNotes.Count} official keywords.");
                System.Console.WriteLine("All Claude data saved to database:");
                System.Console.WriteLine("  - Notes: Implementation guidance");
                System.Console.WriteLine("  - CodeExample: Code examples");
                System.Console.WriteLine("  - TestingNotes: Test cases");
                System.Console.WriteLine("  - CommonMistakes: Mistakes to avoid");
                System.Console.WriteLine("  - RelatedKeywords: Related keywords");
                System.Console.WriteLine("  - Complexity: Implementation complexity");
                System.Console.WriteLine("  - ClaudeRawResponse: Full API response (for reference)\n");
            }

            System.Console.WriteLine("=== Analysis Complete ===");
            
            if (claudeAnalyzer != null)
            {
                claudeAnalyzer.Dispose();
            }
        }
        catch (Exception ex)
        {
            System.Console.WriteLine($"✗ Error during analysis: {ex.Message}");
            System.Console.WriteLine($"Stack trace: {ex.StackTrace}");
        }
    }

    /// <summary>
    /// Ensures the KeywordMetadata table exists by creating it if necessary.
    /// This is needed because the database may have been created before this entity was added.
    /// </summary>
    static async Task EnsureKeywordMetadataTableExistsAsync(CardDbContext dbContext)
    {
        // Create table if it doesn't exist (IF NOT EXISTS handles this safely)
        // Note: SQLite doesn't support ALTER TABLE ADD COLUMN IF NOT EXISTS easily,
        // so we'll add columns that might be missing
        await dbContext.Database.ExecuteSqlRawAsync(@"
            CREATE TABLE IF NOT EXISTS KeywordMetadata (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Keyword TEXT NOT NULL UNIQUE,
                Category INTEGER NOT NULL,
                Confidence REAL NOT NULL,
                Reason TEXT,
                BaseKeyword TEXT,
                Parameters TEXT,
                AbilityType INTEGER,
                Layer INTEGER,
                Sublayer TEXT,
                ImplementationStatus INTEGER NOT NULL DEFAULT 0,
                Notes TEXT,
                CodeExample TEXT,
                TestingNotes TEXT,
                CommonMistakes TEXT,
                RelatedKeywords TEXT,
                Complexity TEXT,
                ClaudeRawResponse TEXT,
                CreatedAt TEXT NOT NULL,
                UpdatedAt TEXT
            );
        ");
        
        // Create ClaudeRequestCache table
        await dbContext.Database.ExecuteSqlRawAsync(@"
            CREATE TABLE IF NOT EXISTS ClaudeRequestCache (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                RequestHash TEXT NOT NULL UNIQUE,
                Keyword TEXT NOT NULL,
                RequestPrompt TEXT NOT NULL,
                ResponseText TEXT NOT NULL,
                ParsedNotes TEXT,
                Model TEXT NOT NULL,
                RequestedAt TEXT NOT NULL,
                LastAccessedAt TEXT NOT NULL
            );
        ");
        
        // Add new columns if they don't exist (SQLite doesn't support IF NOT EXISTS for ALTER TABLE)
        // Check if columns exist by querying table info, then add missing ones
        var newColumns = new[]
        {
            ("Sublayer", "TEXT"),
            ("CodeExample", "TEXT"),
            ("TestingNotes", "TEXT"),
            ("CommonMistakes", "TEXT"),
            ("RelatedKeywords", "TEXT"),
            ("Complexity", "TEXT"),
            ("ClaudeRawResponse", "TEXT")
        };
        
        // Get existing columns
        var existingColumns = new HashSet<string>();
        try
        {
            var tableInfo = await dbContext.Database.ExecuteSqlRawAsync(@"
                SELECT name FROM pragma_table_info('KeywordMetadata');
            ");
            // Note: ExecuteSqlRawAsync doesn't return results, we need to use a different approach
            // For now, just try to add columns and catch errors
        }
        catch
        {
            // Table might not exist yet, that's fine
        }
        
        // Try to add each column, ignoring errors if it already exists
        foreach (var (columnName, columnType) in newColumns)
        {
            try
            {
                // Use string interpolation for column names (safe since they're hardcoded)
                #pragma warning disable EF1002 // SQL injection warning - column names are hardcoded, safe
                await dbContext.Database.ExecuteSqlRawAsync($@"
                    ALTER TABLE KeywordMetadata ADD COLUMN {columnName} {columnType};
                ");
                #pragma warning restore EF1002
            }
            catch
            {
                // Column already exists, ignore
            }
        }
        
        // Create indexes if they don't exist
        await dbContext.Database.ExecuteSqlRawAsync(@"
            CREATE INDEX IF NOT EXISTS IX_KeywordMetadata_Keyword ON KeywordMetadata(Keyword);
            CREATE INDEX IF NOT EXISTS IX_KeywordMetadata_Category ON KeywordMetadata(Category);
            CREATE INDEX IF NOT EXISTS IX_KeywordMetadata_ImplementationStatus ON KeywordMetadata(ImplementationStatus);
            CREATE INDEX IF NOT EXISTS IX_KeywordMetadata_BaseKeyword ON KeywordMetadata(BaseKeyword);
        ");
    }

    /// <summary>
    /// Handles the ingest-claude-results command for importing manually downloaded Claude batch results.
    /// </summary>
    static async Task HandleIngestClaudeResultsCommandAsync(string[] args)
    {
        if (args.Length < 2)
        {
            System.Console.WriteLine("Usage: Majik.Console ingest-claude-results <path-to-jsonl-file>");
            System.Console.WriteLine("\nThis command imports Claude batch results from a manually downloaded JSONL file.");
            System.Console.WriteLine("The file should contain one JSON object per line with custom_id and result fields.");
            return;
        }

        var jsonlPath = args[1];
        
        if (!File.Exists(jsonlPath))
        {
            System.Console.WriteLine($"Error: JSONL file not found: {jsonlPath}");
            return;
        }

        System.Console.WriteLine("=== Claude Results Ingestion Tool ===\n");
        System.Console.WriteLine($"Reading results from: {jsonlPath}\n");

        try
        {
            using var dbContext = new CardDbContext();
            await dbContext.Database.EnsureCreatedAsync();

            // Ensure tables exist
            await EnsureKeywordMetadataTableExistsAsync(dbContext);

            // Patch schema to add IsImplemented column to Cards table
            try
            {
                await CardDataSchemaPatcher.PatchAsync(dbContext.Database.GetDbConnection(), CancellationToken.None);
            }
            catch (Exception patchEx)
            {
                System.Console.WriteLine($"Warning: Failed to patch card database schema: {patchEx.Message}");
                System.Console.WriteLine("Continuing with ingestion...\n");
            }

            var claudeAnalyzer = new ClaudeKeywordAnalyzer(dbContext: dbContext);
            
            // Read and parse JSONL file
            var lines = await File.ReadAllLinesAsync(jsonlPath);
            System.Console.WriteLine($"Found {lines.Length} lines in JSONL file\n");
            
            int processed = 0;
            int errors = 0;
            var keywordsProcessed = new HashSet<string>();
            
            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line))
                    continue;
                
                try
                {
                    // Parse the JSONL line - format is different from API response
                    var jsonDoc = JsonDocument.Parse(line);
                    var root = jsonDoc.RootElement;
                    
                    // Extract custom_id
                    if (!root.TryGetProperty("custom_id", out var customIdElement))
                    {
                        System.Console.WriteLine($"  ⚠ Skipping line: missing custom_id");
                        errors++;
                        continue;
                    }
                    
                    var customId = customIdElement.GetString();
                    if (string.IsNullOrEmpty(customId))
                    {
                        System.Console.WriteLine($"  ⚠ Skipping line: empty custom_id");
                        errors++;
                        continue;
                    }
                    
                    // Extract result
                    if (!root.TryGetProperty("result", out var resultElement))
                    {
                        System.Console.WriteLine($"  ⚠ Skipping {customId}: missing result");
                        errors++;
                        continue;
                    }
                    
                    // Check if result succeeded
                    if (!resultElement.TryGetProperty("type", out var resultTypeElement) || 
                        resultTypeElement.GetString() != "succeeded")
                    {
                        System.Console.WriteLine($"  ⚠ Skipping {customId}: result type is not 'succeeded'");
                        errors++;
                        continue;
                    }
                    
                    // Extract message content
                    if (!resultElement.TryGetProperty("message", out var messageElement) ||
                        !messageElement.TryGetProperty("content", out var contentElement) ||
                        contentElement.ValueKind != JsonValueKind.Array ||
                        contentElement.GetArrayLength() == 0)
                    {
                        System.Console.WriteLine($"  ⚠ Skipping {customId}: missing or empty content");
                        errors++;
                        continue;
                    }
                    
                    // Get text from first content item
                    var firstContent = contentElement[0];
                    if (!firstContent.TryGetProperty("text", out var textElement))
                    {
                        System.Console.WriteLine($"  ⚠ Skipping {customId}: missing text in content");
                        errors++;
                        continue;
                    }
                    
                    var responseText = textElement.GetString();
                    if (string.IsNullOrEmpty(responseText))
                    {
                        System.Console.WriteLine($"  ⚠ Skipping {customId}: empty text");
                        errors++;
                        continue;
                    }
                    
                    // Extract keyword from custom_id (format: "kw{index}-{keyword}")
                    var keywordMatch = System.Text.RegularExpressions.Regex.Match(customId, @"^kw\d+-(.+)$");
                    if (!keywordMatch.Success)
                    {
                        // Try old format
                        keywordMatch = System.Text.RegularExpressions.Regex.Match(customId, @"^keyword-\d+-(.+)$");
                    }
                    
                    string keyword;
                    if (keywordMatch.Success)
                    {
                        keyword = keywordMatch.Groups[1].Value;
                    }
                    else
                    {
                        // Fallback: use custom_id as-is (might be sanitized)
                        keyword = customId;
                        System.Console.WriteLine($"  ⚠ Could not extract keyword from custom_id '{customId}', using as-is");
                    }
                    
                    // Parse the response
                    var notes = claudeAnalyzer.ParseImplementationResponse(keyword, responseText);
                    if (notes == null)
                    {
                        System.Console.WriteLine($"  ⚠ Failed to parse response for '{keyword}'");
                        errors++;
                        continue;
                    }
                    
                    // Rebuild the prompt (we need it for caching)
                    // We'll need to get the keyword info to build the prompt
                    var keywordInfo = KeywordRegistry.GetKeywordInfo(keyword);
                    var magicRule = keywordInfo != null ? "Rule 702" : null;
                    var description = keywordInfo?.Description;
                    var prompt = claudeAnalyzer.BuildImplementationPrompt(keyword, magicRule, description);
                    
                    // Store in cache
                    await claudeAnalyzer.StoreInCacheAsync(keyword, prompt, responseText, notes, CancellationToken.None);
                    
                    // Update KeywordMetadata if it exists
                    var metadata = await dbContext.KeywordMetadata
                        .FirstOrDefaultAsync(k => k.Keyword == keyword);
                    
                    if (metadata != null)
                    {
                        metadata.Notes = notes.ImplementationNotes;
                        metadata.CodeExample = notes.CodeExample;
                        metadata.TestingNotes = notes.TestingNotes;
                        metadata.Complexity = notes.Complexity;
                        metadata.Sublayer = notes.Sublayer;
                        metadata.ClaudeRawResponse = notes.RawResponse;
                        
                        if (notes.CommonMistakes != null && notes.CommonMistakes.Count > 0)
                        {
                            metadata.CommonMistakes = System.Text.Json.JsonSerializer.Serialize(notes.CommonMistakes);
                        }
                        
                        if (notes.RelatedKeywords != null && notes.RelatedKeywords.Count > 0)
                        {
                            metadata.RelatedKeywords = System.Text.Json.JsonSerializer.Serialize(notes.RelatedKeywords);
                        }
                        
                        if (notes.Layer.HasValue)
                        {
                            metadata.Layer = notes.Layer;
                        }
                        
                        if (notes.AbilityType.HasValue)
                        {
                            metadata.AbilityType = notes.AbilityType;
                        }
                        
                        metadata.UpdatedAt = DateTime.UtcNow;
                    }
                    
                    keywordsProcessed.Add(keyword);
                    processed++;
                    
                    if (processed % 10 == 0)
                    {
                        System.Console.WriteLine($"  Processed {processed}/{lines.Length} results...\r");
                    }
                }
                catch (JsonException ex)
                {
                    System.Console.WriteLine($"  ⚠ JSON parse error: {ex.Message}");
                    errors++;
                    continue;
                }
                catch (Exception ex)
                {
                    System.Console.WriteLine($"  ⚠ Error processing line: {ex.Message}");
                    errors++;
                    continue;
                }
            }
            
            await dbContext.SaveChangesAsync();
            
            System.Console.WriteLine($"\n✓ Ingestion complete!\n");
            System.Console.WriteLine($"  Processed: {processed} results");
            System.Console.WriteLine($"  Errors: {errors}");
            System.Console.WriteLine($"  Unique keywords: {keywordsProcessed.Count}");
            
            // Verify cache
            var cacheCount = await dbContext.ClaudeRequestCache.CountAsync();
            System.Console.WriteLine($"  Cache entries: {cacheCount}\n");
        }
        catch (Exception ex)
        {
            System.Console.WriteLine($"\n✗ Error: {ex.Message}");
            System.Console.WriteLine($"Stack trace: {ex.StackTrace}");
        }
    }
}
