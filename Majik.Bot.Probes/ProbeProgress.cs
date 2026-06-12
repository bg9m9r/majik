namespace Majik.Bot.Probes;

/// <summary>
/// Live progress sink for long-running probe heads — the SAME log path
/// contract as the xUnit harness's <c>ProbeProgress</c>
/// (Majik.Bot.Tests.Integration): appends each line to
/// <c>/tmp/majik-probe-progress.log</c> (overridable via
/// <c>MAJIK_PROBE_PROGRESS</c>) so a controller can <c>tail -f</c> a run.
/// Best-effort: any IO failure is swallowed — progress must never fail a
/// probe.
/// </summary>
internal static class ProbeProgress
{
    private static readonly string Path =
        Environment.GetEnvironmentVariable("MAJIK_PROBE_PROGRESS") ?? "/tmp/majik-probe-progress.log";

    private static readonly object Gate = new();

    public static void Log(string line)
    {
        try
        {
            lock (Gate)
            {
                File.AppendAllText(Path, $"[{DateTime.Now:HH:mm:ss}] {line}{Environment.NewLine}");
            }
        }
        catch
        {
            // best-effort only
        }
    }
}
