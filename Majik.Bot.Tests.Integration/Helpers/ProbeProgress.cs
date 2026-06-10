namespace Majik.Bot.Tests.Integration.Helpers;

/// <summary>
/// Live progress sink for the long-running strength probes. xUnit buffers a test's
/// ITestOutputHelper output until the test COMPLETES, so a 30-minute probe is silent
/// while it runs. This appends each progress line to a file as it happens, letting a
/// controller `tail -f` the run. Path overridable via MAJIK_PROBE_PROGRESS; appends
/// (callers may delete the file at probe start for a clean view). Best-effort: any IO
/// failure is swallowed — progress must never fail a probe.
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
