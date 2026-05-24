using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Majik.Core.CardData.MechanicDeps;
using SysConsole = System.Console;

namespace Majik.Console.Commands;

/// <summary>
/// <c>mechanic-deps</c> — scans every <c>*Factory.cs</c> under
/// <c>Majik.Core/CardData/Factories/</c> for "deferred rider" comments,
/// clusters them onto canonical engine primitives, and emits a ranked
/// "build primitive X → unblocks N factories" priority queue.
///
/// Console output is a compact top-N table. <c>--json-out</c> /
/// <c>--md-out</c> sidecars carry the full per-cluster detail
/// (mention list, source spans, implementation hints).
/// </summary>
public static class MechanicDepsCommand
{
    public static async Task<int> RunAsync(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);

        var topN = ParseIntFlag(args, "--top", defaultValue: 10);
        var jsonOut = ParseFlagValue(args, "--json-out");
        var mdOut = ParseFlagValue(args, "--md-out");
        var factoriesDir = ParseFlagValue(args, "--factories-dir")
                           ?? ResolveDefaultFactoriesDir();

        SysConsole.WriteLine($"Scanning factories: {factoriesDir}");
        var scanner = new DeferralScanner();
        var mentions = scanner.ScanDirectory(factoriesDir);
        SysConsole.WriteLine($"  Found {mentions.Count} deferral mention(s) across "
                             + $"{mentions.Select(m => m.FactoryName).Distinct().Count()} factories.");

        var clusterer = new MechanicDependencyClusterer();
        var report = clusterer.Cluster(mentions);

        PrintConsole(report, topN);

        if (jsonOut is not null) await WriteJsonAsync(jsonOut, report, factoriesDir);
        if (mdOut is not null) await WriteMarkdownAsync(mdOut, report, factoriesDir);

        return 0;
    }

    private static string ResolveDefaultFactoriesDir()
    {
        // Walk up looking for the Majik.Core directory.
        var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "Majik.Core", "CardData", "Factories");
            if (Directory.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        // Fall back to the relative path — best-effort.
        return Path.Combine("Majik.Core", "CardData", "Factories");
    }

    private static void PrintConsole(MechanicDependencyReport report, int topN)
    {
        SysConsole.WriteLine();
        SysConsole.WriteLine("Mechanic-promotion priority queue (factory blockage impact):");
        SysConsole.WriteLine();

        var rendered = Math.Min(topN, report.Clusters.Count);
        for (var i = 0; i < rendered; i++)
        {
            var c = report.Clusters[i];
            SysConsole.WriteLine($"{i + 1,3}. {c.DisplayName}");
            SysConsole.WriteLine($"     Blocks: {c.FactoryCount} factories"
                                 + $" ({c.MentionCount} mentions)");
            var examples = string.Join(", ", c.Factories.Take(5));
            if (c.Factories.Count > 0)
            {
                SysConsole.WriteLine($"     Examples: {examples}"
                                     + (c.Factories.Count > 5 ? $", … (+{c.Factories.Count - 5})" : ""));
            }
            if (!string.IsNullOrEmpty(c.ImplementationHint))
            {
                SysConsole.WriteLine($"     Hint:     {c.ImplementationHint}");
            }
            SysConsole.WriteLine();
        }

        if (report.Clusters.Count > rendered)
        {
            SysConsole.WriteLine($"  … +{report.Clusters.Count - rendered} more clusters (see --md-out / --json-out).");
            SysConsole.WriteLine();
        }
        SysConsole.WriteLine($"Unclustered mentions (need human review): {report.Unclustered.Count}");
    }

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };

    private static async Task WriteJsonAsync(string path, MechanicDependencyReport report, string scannedDir)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        var payload = new
        {
            generated_utc = DateTime.UtcNow.ToString("o"),
            scanned_dir = scannedDir,
            total_mentions = report.TotalMentions,
            distinct_factories = report.Clusters.SelectMany(c => c.Factories).Distinct().Count()
                                 + report.Unclustered.Select(m => m.FactoryName).Distinct().Count(),
            cluster_count = report.Clusters.Count,
            unclustered_count = report.Unclustered.Count,
            clusters = report.Clusters.Select(c => new
            {
                primitive_id = c.PrimitiveId,
                display_name = c.DisplayName,
                comp_rules_citation = c.CompRulesCitation,
                implementation_hint = c.ImplementationHint,
                factory_count = c.FactoryCount,
                mention_count = c.MentionCount,
                factories = c.Factories,
                mentions = c.Mentions.Select(m => new
                {
                    factory = m.FactoryName,
                    file = RelativePath(scannedDir, m.FactoryFile),
                    line = m.LineNumber,
                    sentence = m.Sentence,
                    cr = m.CompRulesCitation,
                }).ToList(),
            }).ToList(),
            unclustered = report.Unclustered.Select(m => new
            {
                factory = m.FactoryName,
                file = RelativePath(scannedDir, m.FactoryFile),
                line = m.LineNumber,
                sentence = m.Sentence,
                cr = m.CompRulesCitation,
            }).ToList(),
        };

        await using var fs = File.Create(path);
        await JsonSerializer.SerializeAsync(fs, payload, JsonOpts);
        SysConsole.WriteLine();
        SysConsole.WriteLine($"Wrote JSON → {Path.GetFullPath(path)}");
    }

    private static async Task WriteMarkdownAsync(string path, MechanicDependencyReport report, string scannedDir)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        var sb = new StringBuilder();
        sb.AppendLine("# Mechanic-dependency DAG");
        sb.AppendLine();
        sb.AppendLine("Scanner output: every `*Factory.cs` xmldoc / inline comment mentioning");
        sb.AppendLine("`deferred` / `DEFERRED` / `blocked on` / `same gap`, clustered by canonical");
        sb.AppendLine("engine primitive. Each row answers: \"if we ship primitive _X_, which factory");
        sb.AppendLine("xmldocs flagged that they're blocked on it?\"");
        sb.AppendLine();
        sb.AppendLine($"- **Generated:** {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC");
        sb.AppendLine($"- **Scanned dir:** `{RelativePath(Directory.GetCurrentDirectory(), scannedDir)}`");
        sb.AppendLine($"- **Total mentions:** {report.TotalMentions}");
        sb.AppendLine($"- **Clusters:** {report.Clusters.Count}");
        sb.AppendLine($"- **Unclustered (need new registry pattern):** {report.Unclustered.Count}");
        sb.AppendLine();
        sb.AppendLine("Regenerate with `dotnet run --project Majik.Console -- mechanic-deps"
                      + " --md-out docs/MECHANIC_DEPS.md --json-out docs/mechanic-deps.json`.");
        sb.AppendLine();

        sb.AppendLine("## Priority queue");
        sb.AppendLine();
        sb.AppendLine("| Rank | Primitive | CR | Factories | Mentions |");
        sb.AppendLine("|---:|---|---|---:|---:|");
        for (var i = 0; i < report.Clusters.Count; i++)
        {
            var c = report.Clusters[i];
            sb.AppendLine($"| {i + 1} | {c.DisplayName} | {c.CompRulesCitation ?? "—"} | {c.FactoryCount} | {c.MentionCount} |");
        }
        sb.AppendLine();

        sb.AppendLine("## Cluster detail");
        sb.AppendLine();
        for (var i = 0; i < report.Clusters.Count; i++)
        {
            var c = report.Clusters[i];
            sb.AppendLine($"### {i + 1}. {c.DisplayName}");
            sb.AppendLine();
            if (!string.IsNullOrEmpty(c.CompRulesCitation))
            {
                sb.AppendLine($"- **CR citation:** {c.CompRulesCitation}");
            }
            sb.AppendLine($"- **Blocks:** {c.FactoryCount} factories ({c.MentionCount} mentions)");
            if (!string.IsNullOrEmpty(c.ImplementationHint))
            {
                sb.AppendLine($"- **Implementation hint:** {c.ImplementationHint}");
            }
            sb.AppendLine();
            sb.AppendLine("Mentions:");
            sb.AppendLine();
            foreach (var m in c.Mentions)
            {
                var rel = RelativePath(scannedDir, m.FactoryFile);
                sb.AppendLine($"- `{m.FactoryName}` (`{rel}:{m.LineNumber}`)");
                sb.AppendLine($"  > {EscapeForBlockquote(m.Sentence)}");
            }
            sb.AppendLine();
        }

        if (report.Unclustered.Count > 0)
        {
            sb.AppendLine("## Unclustered (need new registry pattern)");
            sb.AppendLine();
            foreach (var m in report.Unclustered)
            {
                var rel = RelativePath(scannedDir, m.FactoryFile);
                sb.AppendLine($"- `{m.FactoryName}` (`{rel}:{m.LineNumber}`)");
                sb.AppendLine($"  > {EscapeForBlockquote(m.Sentence)}");
            }
            sb.AppendLine();
        }

        sb.AppendLine("---");
        sb.AppendLine();
        sb.AppendLine("Source: `Majik.Core/CardData/MechanicDeps/`. Registry: `MechanicPrimitive.cs`.");

        var utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        await File.WriteAllTextAsync(path, sb.ToString(), utf8NoBom);
        SysConsole.WriteLine($"Wrote Markdown → {Path.GetFullPath(path)}");
    }

    private static string EscapeForBlockquote(string s) =>
        s.Replace("\n", " ").Replace("\r", " ").Trim();

    private static string RelativePath(string baseDir, string filePath)
    {
        try
        {
            return Path.GetRelativePath(baseDir, filePath);
        }
        catch
        {
            return filePath;
        }
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
}
