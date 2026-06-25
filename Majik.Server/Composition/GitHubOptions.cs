namespace Majik.Server.Composition;

/// <summary>GitHub API config for opening report issues. Token + repo come
/// from server-only secrets (Render env: GitHub__Token, GitHub__RepositoryOwner,
/// GitHub__RepositoryName). Never shipped to the client.</summary>
public sealed class GitHubOptions
{
    public const string SectionName = "GitHub";

    public string? Token { get; set; }
    public string? RepositoryOwner { get; set; }
    public string? RepositoryName { get; set; }
    public string IssueLabel { get; set; } = "app-report";

    /// <summary>Shared secret for verifying inbound GitHub webhook deliveries
    /// (X-Hub-Signature-256). Server-only (Render env: GitHub__WebhookSecret).
    /// When unset, the webhook endpoint returns 503 — never crashes.</summary>
    public string? WebhookSecret { get; set; }

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(Token)
        && !string.IsNullOrWhiteSpace(RepositoryOwner)
        && !string.IsNullOrWhiteSpace(RepositoryName);
}
