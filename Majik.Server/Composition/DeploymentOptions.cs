namespace Majik.Server.Composition;

/// <summary>Config for the DeploymentWatcher's redeploy detection. Bound from
/// the "Deployment" section (Render env: Deployment__CoreRepo,
/// Deployment__PortalRepo, Deployment__PortalVersionUrl, Deployment__PollSeconds).</summary>
public sealed class DeploymentOptions
{
    public const string SectionName = "Deployment";

    /// <summary>Full name of the core/API repo. A report whose fix merged here
    /// is delivered when THIS API process booted after the merge.</summary>
    public string CoreRepo { get; set; } = "bg9m9r/majik";

    /// <summary>Full name of the portal repo. A report whose fix merged here is
    /// delivered when the live portal build is newer than the merge.</summary>
    public string PortalRepo { get; set; } = "bg9m9r/majik.portal";

    /// <summary>URL of the deployed portal's version.json (e.g.
    /// https://app.majik.tech/version.json). Null disables portal detection.</summary>
    public string? PortalVersionUrl { get; set; }

    public int PollSeconds { get; set; } = 60;
}
