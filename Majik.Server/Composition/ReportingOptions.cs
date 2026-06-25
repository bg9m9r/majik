namespace Majik.Server.Composition;

/// <summary>In-app issue reporting config. Allowlist of trusted-tester
/// subs; reporting is a no-op (403 not-allowlisted) for everyone else.</summary>
public sealed class ReportingOptions
{
    public const string SectionName = "Reporting";

    public bool Enabled { get; set; } = true;

    /// <summary>Auth subs permitted to file reports.</summary>
    public List<string> TrustedTesterSubs { get; set; } = new();

    /// <summary>Max reports a sub may file per rolling hour.</summary>
    public int MaxReportsPerHour { get; set; } = 5;

    /// <summary>Replay entries to embed (most recent N).</summary>
    public int ReplayCap { get; set; } = 300;

    public bool IsTrusted(string sub) =>
        Enabled && TrustedTesterSubs.Contains(sub, StringComparer.Ordinal);
}
