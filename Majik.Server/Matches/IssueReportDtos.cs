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

/// <summary>Whether the caller may file a report on a given match (allowlisted
/// trusted tester AND seated in the match). Drives the portal's Report-button
/// visibility.</summary>
public sealed record CanReportResponse(bool CanReport);

/// <summary>Failure reasons (mapped to HTTP by the endpoint).</summary>
public enum ReportFailure { NotAllowlisted, NotParticipant, RateLimited, MatchNotFound, GitHubFailed }
