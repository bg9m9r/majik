using System.Text;

namespace Majik.Core.Tests.Snapshots;

/// <summary>
/// Shared location + filename helpers for the snapshot test infra. Files
/// live in source (not <c>bin/</c>) — we walk up from
/// <see cref="AppContext.BaseDirectory"/> to
/// <c>Majik.Core.Tests/Snapshots/</c> instead of relying on
/// <c>Content / CopyToOutputDirectory</c>, which would force test authors to
/// update the csproj every time they extend the fixture list.
/// </summary>
internal static class SnapshotPaths
{
    /// <summary>
    /// Absolute path to <c>Majik.Core.Tests/Snapshots/</c> in the source tree.
    /// Throws if the directory isn't reachable from
    /// <see cref="AppContext.BaseDirectory"/> — i.e. the tests are being run
    /// from somewhere other than a normal <c>dotnet test</c> invocation.
    /// </summary>
    public static string SnapshotsRoot
    {
        get
        {
            // Walk up from the test assembly's bin directory until we find
            // the Majik.Core.Tests project folder, then descend into
            // Snapshots/. This keeps the source tree as the source of truth
            // for snapshots without forcing csproj manifest entries every
            // time we add a fixture file.
            var here = new DirectoryInfo(AppContext.BaseDirectory);
            while (here != null && here.Name != "Majik.Core.Tests")
            {
                here = here.Parent;
            }
            if (here != null)
            {
                var path = Path.Combine(here.FullName, "Snapshots");
                if (Directory.Exists(path)) return path;
            }

            throw new DirectoryNotFoundException(
                "Could not locate Majik.Core.Tests/Snapshots from " +
                AppContext.BaseDirectory);
        }
    }

    public static string SnapshotsDir => Path.Combine(SnapshotsRoot, "snapshots");
    public static string CardDataDir => Path.Combine(SnapshotsRoot, "card-data");
    public static string FixtureFile => Path.Combine(SnapshotsRoot, "snapshot-cards.json");

    /// <summary>
    /// Deterministic slug for a card name: lower-cased, non-alphanumerics
    /// collapsed to dashes, multiple dashes squeezed to one, trimmed. The
    /// resulting string is the filename stem for both the card-data JSON
    /// and the snapshot JSON.
    /// </summary>
    public static string Slug(string cardName)
    {
        if (string.IsNullOrWhiteSpace(cardName)) return "_";
        var sb = new StringBuilder(cardName.Length);
        var lastDash = true;
        foreach (var ch in cardName.ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(ch))
            {
                sb.Append(ch);
                lastDash = false;
            }
            else if (!lastDash)
            {
                sb.Append('-');
                lastDash = true;
            }
        }
        var s = sb.ToString().Trim('-');
        return string.IsNullOrEmpty(s) ? "_" : s;
    }
}
