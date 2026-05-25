using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using SysConsole = System.Console;

namespace Majik.Console.Commands;

/// <summary>
/// <c>dashboard</c> — consolidates the four standalone coverage reports
/// (raw / weighted tiers, gap clusters, mechanic-dep DAG, tournament-meta
/// snapshot) into a single auto-generated status page at
/// <c>docs/DASHBOARD.md</c>.
///
/// Pure-IO logic lives in <see cref="DashboardRenderer"/>; this command
/// is a thin shim that loads the JSON sidecars from disk, shells out to
/// <c>git log</c> for shipping-velocity rollup, and writes the rendered
/// markdown.
///
/// Usage:
/// <code>
/// dotnet run --project Majik.Console -- dashboard
///     [--out &lt;path&gt;] [--modern|--full]
/// </code>
/// </summary>
public static class DashboardCommand
{
    public static async Task<int> RunAsync(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);

        var full = args.Contains("--full", StringComparer.OrdinalIgnoreCase);
        var outPath = ParseFlagValue(args, "--out");

        var repoRoot = ResolveRepoRoot() ?? Directory.GetCurrentDirectory();
        var docsDir = Path.Combine(repoRoot, "docs");

        var coverageFile = full ? "coverage-full.json" : "coverage-modern.json";
        var gapsFile = full ? "coverage-gaps-full.json" : "coverage-gaps-modern.json";
        const string DepsFile = "mechanic-deps.json";
        const string MetaFile = "meta-modern-snapshot.json";

        outPath ??= Path.Combine(docsDir, "DASHBOARD.md");

        SysConsole.WriteLine("=== Coverage Dashboard ===");
        SysConsole.WriteLine($"Mode:   {(full ? "full" : "modern")}");
        SysConsole.WriteLine($"Out:    {outPath}");
        SysConsole.WriteLine();

        var coverage = await LoadJsonAsync(Path.Combine(docsDir, coverageFile));
        var gaps = await LoadJsonAsync(Path.Combine(docsDir, gapsFile));
        var deps = await LoadJsonAsync(Path.Combine(docsDir, DepsFile));
        var meta = await LoadJsonAsync(Path.Combine(docsDir, MetaFile));

        SysConsole.WriteLine($"Loaded coverage:      {(coverage is null ? "<missing>" : "ok")}");
        SysConsole.WriteLine($"Loaded gaps:          {(gaps is null ? "<missing>" : "ok")}");
        SysConsole.WriteLine($"Loaded mechanic-deps: {(deps is null ? "<missing>" : "ok")}");
        SysConsole.WriteLine($"Loaded meta-snapshot: {(meta is null ? "<missing>" : "ok")}");

        var velocity = TryRunGitLogVelocity(repoRoot);
        SysConsole.WriteLine($"Velocity rows:        {velocity.Count}");

        var archetypes = TryParseArchetypeRollups(Path.Combine(repoRoot, "MODERN_COVERAGE.md"));
        SysConsole.WriteLine($"Archetype rows:       {archetypes.Count}");

        var rendered = DashboardRenderer.Render(new DashboardInput
        {
            Mode = full ? "Full" : "Modern",
            GeneratedUtc = DateTime.UtcNow,
            Coverage = coverage,
            Gaps = gaps,
            MechanicDeps = deps,
            MetaSnapshot = meta,
            ShippingVelocity = velocity,
            ArchetypeRollups = archetypes,
        });

        var outDir = Path.GetDirectoryName(Path.GetFullPath(outPath));
        if (!string.IsNullOrEmpty(outDir)) Directory.CreateDirectory(outDir);
        await File.WriteAllTextAsync(outPath, rendered, new UTF8Encoding(false));

        SysConsole.WriteLine();
        SysConsole.WriteLine($"Wrote → {Path.GetFullPath(outPath)}");
        return 0;
    }

    // ----------------------- helpers ---------------------------------------

    private static string? ResolveRepoRoot()
    {
        var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Majik.sln"))) return dir.FullName;
            dir = dir.Parent;
        }
        return null;
    }

    private static async Task<JsonElement?> LoadJsonAsync(string path)
    {
        if (!File.Exists(path)) return null;
        try
        {
            await using var fs = File.OpenRead(path);
            using var doc = await JsonDocument.ParseAsync(fs);
            return doc.RootElement.Clone();
        }
        catch (Exception ex)
        {
            SysConsole.WriteLine($"  ! failed to parse {path}: {ex.Message}");
            return null;
        }
    }

    private static List<VelocityRow> TryRunGitLogVelocity(string repoRoot)
    {
        try
        {
            var psi = new ProcessStartInfo("git")
            {
                WorkingDirectory = repoRoot,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            psi.ArgumentList.Add("log");
            psi.ArgumentList.Add("-E");
            psi.ArgumentList.Add(@"--grep=^feat\(");
            psi.ArgumentList.Add("--since=7 days ago");
            psi.ArgumentList.Add("--pretty=format:%h|%ci|%s");

            using var p = Process.Start(psi);
            if (p is null) return new List<VelocityRow>();
            var stdout = p.StandardOutput.ReadToEnd();
            p.WaitForExit(10_000);
            return DashboardRenderer.ParseVelocity(stdout);
        }
        catch
        {
            return new List<VelocityRow>();
        }
    }

    private static List<ArchetypeRow> TryParseArchetypeRollups(string mdPath)
    {
        if (!File.Exists(mdPath)) return new List<ArchetypeRow>();
        try
        {
            var text = File.ReadAllText(mdPath);
            return DashboardRenderer.ParseArchetypeRollups(text);
        }
        catch
        {
            return new List<ArchetypeRow>();
        }
    }

    private static string? ParseFlagValue(string[] args, string flag)
    {
        var idx = Array.FindIndex(args, a => a.Equals(flag, StringComparison.OrdinalIgnoreCase));
        if (idx < 0 || idx + 1 >= args.Length) return null;
        var v = args[idx + 1];
        return string.IsNullOrWhiteSpace(v) ? null : v;
    }
}

/// <summary>
/// Strongly-typed bundle handed to <see cref="DashboardRenderer.Render"/>.
/// Each JSON source is optional — the renderer emits an empty "no data"
/// note for any missing section so the dashboard remains stable when a
/// sidecar hasn't been regenerated yet.
/// </summary>
public sealed class DashboardInput
{
    public string Mode { get; init; } = "Modern";
    public DateTime GeneratedUtc { get; init; } = DateTime.UtcNow;
    public JsonElement? Coverage { get; init; }
    public JsonElement? Gaps { get; init; }
    public JsonElement? MechanicDeps { get; init; }
    public JsonElement? MetaSnapshot { get; init; }
    public IReadOnlyList<VelocityRow> ShippingVelocity { get; init; } = Array.Empty<VelocityRow>();
    public IReadOnlyList<ArchetypeRow> ArchetypeRollups { get; init; } = Array.Empty<ArchetypeRow>();
}

public sealed record VelocityRow(DateOnly Date, int Prs, int Cards, int Primitives);

public sealed record ArchetypeRow(string Name, string Coverage);

/// <summary>
/// Pure rendering — no IO. All inputs come in via <see cref="DashboardInput"/>;
/// the function returns a deterministic markdown string. This is what the
/// unit tests in <c>Majik.Core.Tests/Console/DashboardCommandTests.cs</c>
/// exercise directly.
/// </summary>
public static class DashboardRenderer
{
    public static string Render(DashboardInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        var sb = new StringBuilder();
        sb.Append("# Majik Coverage Dashboard").Append('\n').Append('\n');
        sb.Append("**Last generated:** ")
          .Append(input.GeneratedUtc.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture))
          .Append(" UTC  ").Append('\n');
        sb.Append("**Mode:** ").Append(input.Mode).Append('\n').Append('\n');
        sb.Append("> Auto-generated by `dotnet run --project Majik.Console -- dashboard`. ")
          .Append("Do not hand-edit — regenerate after refreshing any of: ")
          .Append("`docs/coverage-modern.json`, `docs/coverage-gaps-modern.json`, ")
          .Append("`docs/mechanic-deps.json`, `docs/meta-modern-snapshot.json`.\n\n");

        RenderHeadline(sb, input);
        RenderMechanicDeps(sb, input);
        RenderGapClusters(sb, input);
        RenderTopUnimplementedWeighted(sb, input);
        RenderShippingVelocity(sb, input);
        RenderArchetypes(sb, input);

        sb.Append("---\n");
        sb.Append("Sources: `docs/coverage-modern.json`, `docs/coverage-gaps-modern.json`, ")
          .Append("`docs/mechanic-deps.json`, `docs/meta-modern-snapshot.json`, ")
          .Append("`git log --grep='^feat('`, `MODERN_COVERAGE.md` archetype rollups.\n");

        return sb.ToString();
    }

    // ---------------------------- Sections ---------------------------------

    private static void RenderHeadline(StringBuilder sb, DashboardInput input)
    {
        sb.Append("## Headline\n\n");
        if (input.Coverage is null)
        {
            sb.Append("_No coverage data — run `coverage --modern --json-out docs/coverage-modern.json` first._\n\n");
            return;
        }
        var c = input.Coverage.Value;

        var totalCards = GetInt(c, "total_cards");
        var covered = GetInt(c, "covered_cards");
        var rawPct = GetDouble(c, "covered_percent");
        var weightedPct = GetDouble(c, "frequency_weighted_covered_percent");
        var topMetaCov = GetInt(c, "top_meta_covered");
        var topMetaTot = GetInt(c, "top_meta_total");
        var counts = TryGetObject(c, "counts_by_tier");
        var named = counts is not null ? GetInt(counts.Value, "NamedFactory") : 0;
        var spellBound = counts is not null ? GetInt(counts.Value, "SpellBound") : 0;
        var keywordOnly = counts is not null ? GetInt(counts.Value, "KeywordOnly") : 0;
        var vanilla = counts is not null ? GetInt(counts.Value, "Vanilla") : 0;

        // Top-100 covered = sum of non-Unimplemented tiers in the top-100
        // most-played slice. We approximate from `top_meta` (top-N list)
        // when present; falls back to "—" otherwise.
        var (top100Cov, top100Tot) = ComputeTopNCovered(c, 100);

        sb.Append("| Metric                       | Value |\n");
        sb.Append("|------------------------------|------:|\n");
        sb.Append("| Raw coverage                 | ").Append(FormatPct(rawPct, covered, totalCards)).Append(" |\n");
        sb.Append("| Tournament-weighted          | ").Append(FormatPct(weightedPct, null, null)).Append(" |\n");
        if (topMetaTot > 0)
        {
            sb.Append("| Top-20 most-played covered   | ")
              .Append(topMetaCov).Append('/').Append(topMetaTot).Append(" |\n");
        }
        if (top100Tot > 0)
        {
            sb.Append("| Top-100 most-played covered  | ")
              .Append(top100Cov).Append('/').Append(top100Tot).Append(" |\n");
        }
        sb.Append("| Named factories              | ").Append(named).Append(" |\n");
        sb.Append("| Spell templates              | ").Append(spellBound).Append(" |\n");
        sb.Append("| Keyword-only                 | ").Append(keywordOnly).Append(" |\n");
        sb.Append("| Vanilla                      | ").Append(vanilla).Append(" |\n");
        sb.Append("| Total cards (").Append(input.Mode).Append(") | ").Append(totalCards).Append(" |\n");
        sb.Append('\n');
    }

    private static void RenderMechanicDeps(StringBuilder sb, DashboardInput input)
    {
        sb.Append("## Top open mechanic-deps clusters\n\n");
        if (input.MechanicDeps is null)
        {
            sb.Append("_No mechanic-deps data._\n\n");
            return;
        }
        var clusters = TryGetArray(input.MechanicDeps.Value, "clusters");
        if (clusters is null || clusters.Value.GetArrayLength() == 0)
        {
            sb.Append("_No clusters above threshold._\n\n");
            return;
        }
        sb.Append("| Rank | Primitive | Factories blocked | Mentions |\n");
        sb.Append("|-----:|-----------|------------------:|---------:|\n");
        var rank = 1;
        foreach (var cluster in clusters.Value.EnumerateArray().Take(10))
        {
            var name = GetString(cluster, "display_name") ?? GetString(cluster, "primitive_id") ?? "(unnamed)";
            var fc = GetInt(cluster, "factory_count");
            var mc = GetInt(cluster, "mention_count");
            sb.Append("| ").Append(rank).Append(" | ").Append(EscapeCell(name))
              .Append(" | ").Append(fc).Append(" | ").Append(mc).Append(" |\n");
            rank++;
        }
        sb.Append('\n');
    }

    private static void RenderGapClusters(StringBuilder sb, DashboardInput input)
    {
        sb.Append("## Top unimplemented mechanic patterns\n\n");
        if (input.Gaps is null)
        {
            sb.Append("_No gap-cluster data._\n\n");
            return;
        }
        var clusters = TryGetArray(input.Gaps.Value, "clusters");
        if (clusters is null || clusters.Value.GetArrayLength() == 0)
        {
            sb.Append("_No clusters above threshold._\n\n");
            return;
        }
        sb.Append("| Cluster signature | Cards |\n");
        sb.Append("|-------------------|------:|\n");
        foreach (var cluster in clusters.Value.EnumerateArray().Take(15))
        {
            var sig = GetString(cluster, "first_sentence_signature") ?? "(unsigned)";
            var members = GetInt(cluster, "member_count");
            sb.Append("| `").Append(EscapeInlineCode(sig)).Append("` | ")
              .Append(members).Append(" |\n");
        }
        sb.Append('\n');
    }

    private static void RenderTopUnimplementedWeighted(StringBuilder sb, DashboardInput input)
    {
        sb.Append("## Top unimplemented tournament-weighted cards\n\n");
        if (input.Coverage is null)
        {
            sb.Append("_No coverage data._\n\n");
            return;
        }
        var topMeta = TryGetArray(input.Coverage.Value, "top_meta");
        if (topMeta is null)
        {
            sb.Append("_No top_meta data in coverage report._\n\n");
            return;
        }

        var rows = new List<(string Name, string Tier, double Weight)>();
        foreach (var entry in topMeta.Value.EnumerateArray())
        {
            var tier = GetString(entry, "tier") ?? "";
            if (!string.Equals(tier, "Unimplemented", StringComparison.Ordinal)) continue;
            var name = GetString(entry, "name") ?? "(unknown)";
            var weight = GetDouble(entry, "weight");
            rows.Add((name, tier, weight));
        }

        if (rows.Count == 0)
        {
            sb.Append("_Every top-meta card is covered._\n\n");
            return;
        }

        sb.Append("| Card | Tier | Weight |\n");
        sb.Append("|------|------|------:|\n");
        foreach (var (name, tier, weight) in rows.Take(20))
        {
            sb.Append("| ").Append(EscapeCell(name))
              .Append(" | ").Append(tier)
              .Append(" | ").Append(weight.ToString("0.#", CultureInfo.InvariantCulture)).Append(" |\n");
        }
        sb.Append('\n');
    }

    private static void RenderShippingVelocity(StringBuilder sb, DashboardInput input)
    {
        sb.Append("## Recent shipping velocity\n\n");
        sb.Append("_Last 7 days, parsed from `git log --grep='^feat('`. Card / primitive split inferred from commit prefix (`feat(card)` vs `feat(infra)`)._\n\n");
        if (input.ShippingVelocity.Count == 0)
        {
            sb.Append("_No feat() commits in the last 7 days._\n\n");
            return;
        }
        sb.Append("| Date | PRs | Cards | Primitives |\n");
        sb.Append("|------|----:|------:|-----------:|\n");
        foreach (var row in input.ShippingVelocity)
        {
            sb.Append("| ").Append(row.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))
              .Append(" | ").Append(row.Prs)
              .Append(" | ").Append(row.Cards)
              .Append(" | ").Append(row.Primitives).Append(" |\n");
        }
        sb.Append('\n');
    }

    private static void RenderArchetypes(StringBuilder sb, DashboardInput input)
    {
        sb.Append("## Archetype rollups\n\n");
        sb.Append("_Pulled from `MODERN_COVERAGE.md` archetype rollups section. Later: auto-compute via deck-list snapshots._\n\n");
        if (input.ArchetypeRollups.Count == 0)
        {
            sb.Append("_No archetype rollups parsed._\n\n");
            return;
        }
        sb.Append("| Archetype | Coverage |\n");
        sb.Append("|-----------|---------:|\n");
        foreach (var a in input.ArchetypeRollups)
        {
            sb.Append("| ").Append(EscapeCell(a.Name)).Append(" | ").Append(a.Coverage).Append(" |\n");
        }
        sb.Append('\n');
    }

    // -------------------- Velocity parser ----------------------------------

    /// <summary>
    /// Parses the output of <c>git log --pretty=format:%h|%ci|%s</c> and
    /// rolls it up by date. Subject prefix decides the bucket:
    /// <c>feat(card)</c> → Cards, <c>feat(infra)</c> + <c>feat(rules)</c> →
    /// Primitives. Anything else still counts toward PRs.
    /// </summary>
    public static List<VelocityRow> ParseVelocity(string gitOutput)
    {
        var byDate = new Dictionary<DateOnly, (int prs, int cards, int primitives)>();
        if (string.IsNullOrWhiteSpace(gitOutput)) return new List<VelocityRow>();

        foreach (var line in gitOutput.Split('\n'))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            var parts = line.Split('|', 3);
            if (parts.Length < 3) continue;

            // %ci → "2026-05-25 00:16:50 -0400"
            var dateToken = parts[1].Split(' ', 2)[0];
            if (!DateOnly.TryParseExact(dateToken, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var date))
            {
                continue;
            }

            var subject = parts[2];
            var bucket = ClassifyCommit(subject);
            byDate.TryGetValue(date, out var tally);
            tally.prs++;
            switch (bucket)
            {
                case CommitBucket.Card: tally.cards++; break;
                case CommitBucket.Primitive: tally.primitives++; break;
            }
            byDate[date] = tally;
        }

        return byDate
            .OrderByDescending(kvp => kvp.Key)
            .Select(kvp => new VelocityRow(kvp.Key, kvp.Value.prs, kvp.Value.cards, kvp.Value.primitives))
            .ToList();
    }

    private enum CommitBucket { Card, Primitive, Other }

    private static CommitBucket ClassifyCommit(string subject)
    {
        if (subject.StartsWith("feat(card)", StringComparison.OrdinalIgnoreCase)) return CommitBucket.Card;
        if (subject.StartsWith("feat(infra)", StringComparison.OrdinalIgnoreCase)) return CommitBucket.Primitive;
        if (subject.StartsWith("feat(rules)", StringComparison.OrdinalIgnoreCase)) return CommitBucket.Primitive;
        return CommitBucket.Other;
    }

    // -------------------- Archetype rollup parser --------------------------

    /// <summary>
    /// Pulls "- **Name** — ... ~NN%" lines out of MODERN_COVERAGE.md's
    /// "Coverage by archetype" section. Coverage cell is whatever percent
    /// the line ends in, or "—" if none.
    /// </summary>
    public static List<ArchetypeRow> ParseArchetypeRollups(string md)
    {
        var rows = new List<ArchetypeRow>();
        if (string.IsNullOrWhiteSpace(md)) return rows;

        var lines = md.Split('\n');
        var inSection = false;
        var nameRx = new Regex(@"^-\s*\*\*([^*]+)\*\*", RegexOptions.Compiled);
        var pctRx = new Regex(@"~\s*(\d+)\s*%", RegexOptions.Compiled);

        foreach (var raw in lines)
        {
            var line = raw.TrimEnd('\r');

            if (line.StartsWith("## ", StringComparison.Ordinal))
            {
                inSection = line.Contains("archetype", StringComparison.OrdinalIgnoreCase);
                continue;
            }

            if (!inSection) continue;
            if (!line.StartsWith("- ", StringComparison.Ordinal)) continue;

            var nameMatch = nameRx.Match(line);
            if (!nameMatch.Success) continue;
            var name = nameMatch.Groups[1].Value.Trim();

            var pctMatches = pctRx.Matches(line);
            string coverage = "—";
            if (pctMatches.Count > 0)
            {
                // Use the LAST percent token on the line — the per-card
                // "~Nn%" mentions come earlier, the archetype rollup
                // number is the final one.
                coverage = "~" + pctMatches[^1].Groups[1].Value + "%";
            }
            rows.Add(new ArchetypeRow(name, coverage));
        }
        return rows;
    }

    // -------------------- JSON helpers -------------------------------------

    private static (int covered, int total) ComputeTopNCovered(JsonElement coverage, int n)
    {
        var topMeta = TryGetArray(coverage, "top_meta");
        if (topMeta is null) return (0, 0);
        int total = 0, covered = 0;
        foreach (var entry in topMeta.Value.EnumerateArray())
        {
            if (total >= n) break;
            total++;
            var tier = GetString(entry, "tier");
            if (!string.Equals(tier, "Unimplemented", StringComparison.Ordinal))
            {
                covered++;
            }
        }
        return (covered, total);
    }

    private static string FormatPct(double pct, int? num, int? denom)
    {
        var pctStr = pct.ToString("0.0", CultureInfo.InvariantCulture) + "%";
        if (num is null || denom is null || denom <= 0) return pctStr;
        return $"{pctStr} ({num} / {denom})";
    }

    private static int GetInt(JsonElement e, string prop)
    {
        if (!e.TryGetProperty(prop, out var v)) return 0;
        return v.ValueKind switch
        {
            JsonValueKind.Number => v.TryGetInt32(out var n) ? n : (int)v.GetDouble(),
            JsonValueKind.String => int.TryParse(v.GetString(), out var s) ? s : 0,
            _ => 0,
        };
    }

    private static double GetDouble(JsonElement e, string prop)
    {
        if (!e.TryGetProperty(prop, out var v)) return 0;
        return v.ValueKind switch
        {
            JsonValueKind.Number => v.GetDouble(),
            JsonValueKind.String => double.TryParse(v.GetString(), NumberStyles.Any,
                CultureInfo.InvariantCulture, out var d) ? d : 0,
            _ => 0,
        };
    }

    private static string? GetString(JsonElement e, string prop)
    {
        if (!e.TryGetProperty(prop, out var v)) return null;
        return v.ValueKind == JsonValueKind.String ? v.GetString() : v.ToString();
    }

    private static JsonElement? TryGetObject(JsonElement e, string prop) =>
        e.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.Object ? v : null;

    private static JsonElement? TryGetArray(JsonElement e, string prop) =>
        e.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.Array ? v : null;

    private static string EscapeCell(string s) =>
        s.Replace("|", "\\|").Replace("\n", " ").Replace("\r", " ");

    private static string EscapeInlineCode(string s) =>
        s.Replace("`", "\\`").Replace("|", "\\|");
}
