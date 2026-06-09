namespace Majik.Core.Tests.CardData;

/// <summary>
/// Resolves the absolute path to <c>Majik.Core/CardData/Factories/</c> in the
/// source tree from the test assembly's bin directory — mirrors the walk-up
/// approach used by the snapshot infra (<c>SnapshotPaths</c>) so the gate scans
/// real source files without csproj content-copy entries.
/// </summary>
internal static class ResolverNullInertEffectAuditPaths
{
    public static string FactoriesDir
    {
        get
        {
            // Walk up from the test bin dir to the repo's solution dir (the one
            // that contains both Majik.Core.Tests and Majik.Core), then descend
            // into Majik.Core/CardData/Factories.
            var here = new DirectoryInfo(AppContext.BaseDirectory);
            while (here != null && here.Name != "Majik.Core.Tests")
                here = here.Parent;

            // here == .../Majik.Core.Tests  →  parent == solution dir.
            var solutionDir = here?.Parent;
            if (solutionDir != null)
            {
                var path = Path.Combine(
                    solutionDir.FullName, "Majik.Core", "CardData", "Factories");
                if (Directory.Exists(path)) return path;
            }

            throw new DirectoryNotFoundException(
                "Could not locate Majik.Core/CardData/Factories from " +
                AppContext.BaseDirectory);
        }
    }
}
