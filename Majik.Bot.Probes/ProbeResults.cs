using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Majik.Bot.Probes;

/// <summary>
/// Result of one probe head: counts over the played games, the win-rate of
/// strategy A over DECIDED games, and the per-game records.
/// </summary>
public sealed record ProbeResult(
    string HeadName,
    string DeckA,
    string DeckB,
    string SeatALabel,
    string SeatBLabel,
    bool Canary,
    int SeedBlock,
    int GamesPlayed,
    int AWins,
    int BWins,
    int Draws,
    int Inconclusive,
    IReadOnlyList<ProbeGameRecord> Games,
    int? Iterations = null,
    int? BudgetMs = null)
{
    /// <summary>Decided games (A wins + B wins).</summary>
    public int Decided => AWins + BWins;

    /// <summary>Strategy A's win-rate over decided games (0 when none decided).</summary>
    public double WinRate => Decided > 0 ? (double)AWins / Decided : 0.0;
}

/// <summary>
/// Aggregate result of a panel run: every cell plus the headline mean
/// win-rate across NON-canary cells and the run's config echo.
/// </summary>
public sealed record PanelResult(
    IReadOnlyList<ProbeResult> Cells,
    string? CommitHash,
    DateTime GeneratedUtc)
{
    /// <summary>Mean of the non-canary cells' win-rates (the headline metric).
    /// Canary cells are excluded by design — they track instrument drift on a
    /// known-degenerate matchup. 0 when there are no non-canary cells.</summary>
    public double HeadlineMeanWinRate
    {
        get
        {
            var headline = Cells.Where(c => !c.Canary).ToList();
            return headline.Count > 0 ? headline.Average(c => c.WinRate) : 0.0;
        }
    }
}

/// <summary>JSON + markdown writers for probe results. The JSON schema is
/// pinned by <c>ProbeLibraryTests</c> — change it deliberately.</summary>
public static class ProbeResults
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>Serialize a panel result (cells + headline mean + config echo
    /// + commit hash) to <paramref name="path"/> as indented camelCase JSON.</summary>
    public static void WriteJson(PanelResult panel, string path)
    {
        var dto = new
        {
            generatedUtc = panel.GeneratedUtc,
            commitHash = panel.CommitHash,
            headlineMeanWinRate = panel.HeadlineMeanWinRate,
            cells = panel.Cells.Select(c => new
            {
                head = c.HeadName,
                deckA = c.DeckA,
                deckB = c.DeckB,
                seatA = c.SeatALabel,
                seatB = c.SeatBLabel,
                canary = c.Canary,
                seedBlock = c.SeedBlock,
                gamesPlayed = c.GamesPlayed,
                aWins = c.AWins,
                bWins = c.BWins,
                draws = c.Draws,
                inconclusive = c.Inconclusive,
                decided = c.Decided,
                winRate = c.WinRate,
                iterations = c.Iterations,
                budgetMs = c.BudgetMs,
                games = c.Games,
            }),
        };
        File.WriteAllText(path, JsonSerializer.Serialize(dto, JsonOptions));
    }

    /// <summary>Write the human-readable markdown summary: per-cell table +
    /// headline mean (canaries excluded, listed separately) + config echo.</summary>
    public static void WriteMarkdownSummary(PanelResult panel, string path)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# FB1 ladder panel results");
        sb.AppendLine();
        sb.AppendLine($"- Generated (UTC): {panel.GeneratedUtc:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine($"- Commit: {panel.CommitHash ?? "(not recorded)"}");
        sb.AppendLine($"- **Headline mean win-rate (non-canary cells): {panel.HeadlineMeanWinRate:P1}**");
        sb.AppendLine();
        sb.AppendLine("| cell | decks (A vs B) | seats | N | A wins | B wins | draws | inconcl | win-rate | canary | iter | budgetMs |");
        sb.AppendLine("|------|----------------|-------|---|--------|--------|-------|---------|----------|--------|------|----------|");
        foreach (var c in panel.Cells)
        {
            sb.AppendLine(
                $"| {c.HeadName} | {c.DeckA} vs {c.DeckB} | {c.SeatALabel} vs {c.SeatBLabel} " +
                $"| {c.GamesPlayed} | {c.AWins} | {c.BWins} | {c.Draws} | {c.Inconclusive} " +
                $"| {c.WinRate:P1} | {(c.Canary ? "YES" : "")} | {c.Iterations?.ToString() ?? "-"} " +
                $"| {c.BudgetMs?.ToString() ?? "-"} |");
        }
        sb.AppendLine();
        sb.AppendLine("Canary cells are excluded from the headline mean (known-degenerate matchup; instrument-drift tripwire only).");
        File.WriteAllText(path, sb.ToString());
    }
}
