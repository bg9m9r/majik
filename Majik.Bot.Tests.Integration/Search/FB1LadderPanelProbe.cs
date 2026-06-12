using FluentAssertions;
using Majik.Bot.Probes;
using Xunit;
using Xunit.Abstractions;

namespace Majik.Bot.Tests.Integration.Search;

// ═══════════════════════════════════════════════════════════════════════════════
// FB1 LADDER PANEL — xUnit wrapper over the Majik.Bot.Probes library
// ═══════════════════════════════════════════════════════════════════════════════
//
// Thin instrument wrapper: runs LadderPanel.FB1 (13 cells — 5 mirrors + 4 asym
// pairs both seat assignments; mcts live-shape vs the FROZEN frozen-fb1
// baseline) through the SAME ProbeRunner the `Majik.Console probe panel`
// subcommand calls — single source of truth, so the two paths play identical
// seeds (block 90000 + 1000×cell, game i = block + i).
//
// PREFER THE CONSOLE: `dotnet run --project Majik.Console -c Release -- probe
// panel` is the primary way to run this panel (streams progress, writes
// JSON + markdown results). This wrapper exists so the panel is also runnable
// the old un-skip way alongside the other probe classes.
//
// Micro config: set MAJIK_FB1_PANEL_N=<games> to override N per cell (e.g. 2
// for a smoke pass); unset = the baseline N=30.
//
// INTERPRETATION (no hard win-rate assertion — liveness only): each cell must
// decide ≥1 game with a finite win-rate in [0,1]; the controller reads the
// [STRENGTH] lines from /tmp/majik-probe-progress.log and judges. Headline =
// mean win-rate over NON-canary cells (the Prowess/Burn pair is the canary).
// ═══════════════════════════════════════════════════════════════════════════════

public class FB1LadderPanelProbe
{
    private readonly ITestOutputHelper _output;
    public FB1LadderPanelProbe(ITestOutputHelper output) => _output = output;

    [Fact(Skip = "on-demand strength probe — un-skip to run (prefer `Majik.Console probe panel`)")]
    public async Task Mcts_Live_VsFrozenFb1_FullPanel()
    {
        // Env override so a micro smoke (N=2) doesn't need a const swap.
        int n = int.TryParse(
            Environment.GetEnvironmentVariable("MAJIK_FB1_PANEL_N"), out var v) && v > 0
            ? v
            : LadderPanel.DefaultGames;

        var heads = LadderPanel.FB1.Select(h => h with { Games = n }).ToList();

        var panel = await ProbeRunner.RunPanelAsync(
            heads, progress: _output.WriteLine);

        foreach (var cell in panel.Cells)
        {
            cell.Decided.Should().BeGreaterThan(0,
                $"{cell.HeadName} must decide at least one game for its win-rate to be meaningful");
            cell.WinRate.Should().BeInRange(0.0, 1.0);
        }

        var summary =
            $"[STRENGTH] [fb1-panel] headline mean (non-canary) = {panel.HeadlineMeanWinRate:P1} " +
            $"over {panel.Cells.Count(c => !c.Canary)} cells (N={n}/cell)";
        _output.WriteLine(summary);
    }
}
