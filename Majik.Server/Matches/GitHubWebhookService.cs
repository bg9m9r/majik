using System.Text.Json;
using System.Text.RegularExpressions;

namespace Majik.Server.Matches;

/// <summary>Handles GitHub <c>pull_request</c> webhooks: when a PR merges and its
/// body closes an app-report issue, advance the MatchReport to Merged.</summary>
public sealed class GitHubWebhookService
{
    private readonly MatchReportRepository _reports;
    public GitHubWebhookService(MatchReportRepository reports) => _reports = reports;

    /// <summary>Returns true if it handled a merged-PR-closing-an-app-report event.</summary>
    public async Task<bool> HandlePullRequestAsync(string json, string coreRepoFullName, CancellationToken ct)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (!root.TryGetProperty("action", out var action) || action.GetString() != "closed") return false;
        if (!root.TryGetProperty("pull_request", out var pr)) return false;
        if (!pr.TryGetProperty("merged", out var merged) || merged.ValueKind != JsonValueKind.True) return false;

        var prBody = pr.TryGetProperty("body", out var b) && b.ValueKind == JsonValueKind.String
            ? b.GetString() ?? "" : "";
        var fixRepo = root.TryGetProperty("repository", out var repo) && repo.TryGetProperty("full_name", out var fn)
            ? fn.GetString() ?? "" : "";
        var mergedAt = pr.TryGetProperty("merged_at", out var ma) && ma.ValueKind == JsonValueKind.String
            ? DateTime.Parse(ma.GetString()!).ToUniversalTime() : DateTime.UtcNow;

        var handled = false;
        foreach (var issueNum in ParseClosedIssueNumbers(prBody, coreRepoFullName))
        {
            var report = await _reports.GetByIssueNumberAsync(issueNum, ct);
            if (report == null) continue;
            await _reports.MarkMergedAsync(report.Id, fixRepo, mergedAt, ct);
            handled = true;
        }
        return handled;
    }

    /// <summary>Parse <c>Closes #N</c> (same-repo) + <c>Closes owner/repo#N</c>
    /// (cross-repo, matching coreRepoFullName) from a PR body. Supports the close
    /// keywords fixes/closes/resolves.</summary>
    public static IReadOnlyList<int> ParseClosedIssueNumbers(string prBody, string coreRepoFullName)
    {
        if (string.IsNullOrEmpty(prBody)) return [];
        var nums = new List<int>();
        // owner/repo#N — only for the core repo where app-report issues live.
        // Fully-qualify Match: Majik.Server.Matches.Match (the entity) shadows it.
        foreach (System.Text.RegularExpressions.Match m in Regex.Matches(prBody, @"\b(?:close[sd]?|fix(?:e[sd])?|resolve[sd]?)\s+([\w.-]+/[\w.-]+)#(\d+)", RegexOptions.IgnoreCase))
            if (string.Equals(m.Groups[1].Value, coreRepoFullName, StringComparison.OrdinalIgnoreCase))
                nums.Add(int.Parse(m.Groups[2].Value));
        // bare #N — same-repo references (the PR is in the core repo)
        foreach (System.Text.RegularExpressions.Match m in Regex.Matches(prBody, @"\b(?:close[sd]?|fix(?:e[sd])?|resolve[sd]?)\s+#(\d+)", RegexOptions.IgnoreCase))
            nums.Add(int.Parse(m.Groups[1].Value));
        return nums.Distinct().ToList();
    }
}
