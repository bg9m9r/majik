using System.Diagnostics;
using Majik.Bot.Probes;

namespace Majik.Console.Commands;

/// <summary>
/// <c>Majik.Console probe</c> — runs strength-probe heads from
/// <see cref="LadderPanel"/> without the xUnit un-skip dance.
///
/// <para>Usage: <c>probe panel [--n N] [--out DIR] [--concurrency C]</c> runs
/// the full FB1 ladder panel; <c>probe &lt;headName&gt; [...]</c> runs one
/// registered head. Streams per-game progress to stdout (and the shared
/// probe-progress log path), writes <c>probe-results.json</c> +
/// <c>probe-summary.md</c> to <c>--out</c> (default
/// <c>./probe-results/&lt;utc-stamp&gt;/</c>).</para>
///
/// <para>Interpretation contract unchanged from the xUnit probes: exit code 0
/// = the run COMPLETED (liveness), non-zero = bad args or crash. There are NO
/// win-rate thresholds — the operator judges from the results.</para>
/// </summary>
public static class ProbeCommand
{
    public const string HelpText =
        """
        probe — run strength-probe heads against the frozen FB1 baseline.
          Majik.Console probe panel       [--n N] [--out DIR] [--concurrency C]
          Majik.Console probe <headName>  [--n N] [--out DIR] [--concurrency C]
          --n N             games per head (default 30)
          --out DIR         results directory (default ./probe-results/<utc-stamp>/)
          --concurrency C   max concurrent heads (default min(heads, cores/2))
        """;

    /// <summary>Parsed run configuration: the resolved heads (N already
    /// applied), the target ("panel" or the canonical head name), and the
    /// optional output/concurrency overrides.</summary>
    public sealed record ProbeRunConfig(
        IReadOnlyList<ProbeHead> Heads,
        string Target,
        string? OutDir,
        int? Concurrency);

    /// <summary>
    /// Parse <c>probe</c> args (including the leading <c>"probe"</c> token).
    /// Returns exactly one of (config, null) or (null, error-message). Pure —
    /// no IO — so it is unit-testable from Majik.Bot.Tests.
    /// </summary>
    public static (ProbeRunConfig? Config, string? Error) Parse(string[] args)
    {
        if (args.Length < 2)
            return (null, $"probe: missing target — expected 'panel' or a head name.\n{AvailableHeads()}");

        string target = args[1];
        int? n = null;
        string? outDir = null;
        int? concurrency = null;

        for (int i = 2; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--n":
                    if (!TryNumeric(args, ref i, out var games))
                        return (null, $"probe: --n requires a positive integer.\n{HelpText}");
                    n = games;
                    break;
                case "--out":
                    if (++i >= args.Length)
                        return (null, $"probe: --out requires a directory path.\n{HelpText}");
                    outDir = args[i];
                    break;
                case "--concurrency":
                    if (!TryNumeric(args, ref i, out var cap))
                        return (null, $"probe: --concurrency requires a positive integer.\n{HelpText}");
                    concurrency = cap;
                    break;
                default:
                    return (null, $"probe: unknown option '{args[i]}'.\n{HelpText}");
            }
        }

        IReadOnlyList<ProbeHead> heads;
        string canonicalTarget;
        if (target.Equals("panel", StringComparison.OrdinalIgnoreCase))
        {
            heads = LadderPanel.FB1;
            canonicalTarget = "panel";
        }
        else
        {
            var head = LadderPanel.Find(target);
            if (head is null)
                return (null, $"probe: unknown head '{target}'.\n{AvailableHeads()}");
            heads = new[] { head };
            canonicalTarget = head.Name;
        }

        if (n is { } games2)
            heads = heads.Select(h => h with { Games = games2 }).ToList();

        return (new ProbeRunConfig(heads, canonicalTarget, outDir, concurrency), null);
    }

    /// <summary>Run the subcommand end-to-end. Exit 0 on completion
    /// (liveness only), 1 on bad args, 2 on crash.</summary>
    public static async Task<int> RunAsync(string[] args)
    {
        var (config, error) = Parse(args);
        if (config is null)
        {
            System.Console.Error.WriteLine(error);
            return 1;
        }

        var outDir = config.OutDir ?? Path.Combine(
            ".", "probe-results", DateTime.UtcNow.ToString("yyyyMMdd-HHmmss'Z'"));

        try
        {
            Directory.CreateDirectory(outDir);
            var commit = TryGitCommitHash();

            System.Console.WriteLine(
                $"probe: running {config.Target} — {config.Heads.Count} head(s), " +
                $"N={config.Heads[0].Games}, out={outDir}, commit={commit ?? "(unknown)"}");

            var panel = await ProbeRunner.RunPanelAsync(
                config.Heads,
                maxConcurrency: config.Concurrency,
                progress: System.Console.WriteLine,
                commitHash: commit);

            var jsonPath = Path.Combine(outDir, "probe-results.json");
            var mdPath = Path.Combine(outDir, "probe-summary.md");
            ProbeResults.WriteJson(panel, jsonPath);
            ProbeResults.WriteMarkdownSummary(panel, mdPath);

            System.Console.WriteLine(
                $"probe: done — headline mean win-rate (non-canary): {panel.HeadlineMeanWinRate:P1}");
            System.Console.WriteLine($"probe: results  {jsonPath}");
            System.Console.WriteLine($"probe: summary  {mdPath}");
            return 0;
        }
        catch (Exception ex)
        {
            System.Console.Error.WriteLine($"probe: CRASH — {ex.GetType().Name}: {ex.Message}");
            System.Console.Error.WriteLine(ex.StackTrace);
            return 2;
        }
    }

    private static bool TryNumeric(string[] args, ref int i, out int value)
    {
        value = 0;
        return ++i < args.Length && int.TryParse(args[i], out value) && value > 0;
    }

    private static string AvailableHeads() =>
        "available: panel, " + string.Join(", ", LadderPanel.FB1.Select(h => h.Name));

    /// <summary>Best-effort commit hash for the results' config echo (the
    /// <c>git rev-parse</c> passthrough). Null when git is unavailable.</summary>
    private static string? TryGitCommitHash()
    {
        try
        {
            using var proc = Process.Start(new ProcessStartInfo
            {
                FileName = "git",
                Arguments = "rev-parse --short HEAD",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            });
            if (proc is null) return null;
            string output = proc.StandardOutput.ReadToEnd().Trim();
            proc.WaitForExit(5000);
            return proc.ExitCode == 0 && output.Length > 0 ? output : null;
        }
        catch
        {
            return null;
        }
    }
}
