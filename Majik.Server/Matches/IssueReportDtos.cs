namespace Majik.Server.Matches;

/// <summary>Client telemetry posted with a report (free-form, bounded by the
/// endpoint's request-size limit). Stored only inside the issue body.</summary>
public sealed record ClientTelemetryDto(
    string? Route,
    string? BuildSha,
    IReadOnlyList<string>? ConsoleErrors,
    IReadOnlyList<string>? RecentSignalR,
    object? PromptState);

public sealed record ReportIssueRequest(string Description, ClientTelemetryDto? Telemetry);

public sealed record ReportIssueResponse(Guid ReportId, int IssueNumber, string IssueUrl);

/// <summary>Failure reasons (mapped to HTTP by the endpoint).</summary>
public enum ReportFailure { NotAllowlisted, NotParticipant, RateLimited, MatchNotFound, GitHubFailed }
