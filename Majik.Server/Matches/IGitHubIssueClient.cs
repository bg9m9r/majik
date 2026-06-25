namespace Majik.Server.Matches;

public sealed record CreatedIssue(int Number, string HtmlUrl);

public interface IGitHubIssueClient
{
    /// <summary>Create a labelled issue. Returns null if GitHub is not
    /// configured or the API call fails (caller decides how to surface).</summary>
    Task<CreatedIssue?> CreateIssueAsync(string title, string body, string label, CancellationToken ct);
}
