using System.Text;
using System.Text.Json;
using Majik.Server.Composition;

namespace Majik.Server.Matches;

public sealed class IssueReportResult
{
    public bool IsSuccess { get; private init; }
    public ReportIssueResponse? Value { get; private init; }
    public ReportFailure Failure { get; private init; }
    public static IssueReportResult Ok(ReportIssueResponse v) => new() { IsSuccess = true, Value = v };
    public static IssueReportResult Fail(ReportFailure f) => new() { IsSuccess = false, Failure = f };
}

/// <summary>Orchestrates an in-app issue report: allowlist + match-participant
/// + rate-limit checks, then snapshots full-reveal state + replay + reporter
/// telemetry into a labelled GitHub issue and persists a light
/// <see cref="MatchReport"/> status record.</summary>
public sealed class IssueReportService
{
    private readonly MatchRepository _matches;
    private readonly MatchReportRepository _reports;
    private readonly IGitHubIssueClient _github;
    private readonly ServerGameFactory? _gameFactory;
    private readonly MatchReplayBuffer? _replayBuffer;
    private readonly ReportingOptions _opts;

    public IssueReportService(
        MatchRepository matches, MatchReportRepository reports, IGitHubIssueClient github,
        ServerGameFactory? gameFactory, MatchReplayBuffer? replayBuffer, ReportingOptions opts)
    {
        _matches = matches; _reports = reports; _github = github;
        _gameFactory = gameFactory; _replayBuffer = replayBuffer; _opts = opts;
    }

    /// <summary>Whether <paramref name="callerSub"/> may file a report on this
    /// match: an allowlisted trusted tester seated in the match. Drives the
    /// portal Report-button visibility (no side effects).</summary>
    public async Task<bool> CanReportAsync(string callerSub, Guid matchId, CancellationToken ct)
    {
        if (!_opts.IsTrusted(callerSub)) return false;
        var match = await _matches.GetByIdAsync(matchId, ct);
        if (match == null) return false;
        return callerSub == match.Creator.Sub || callerSub == match.Opponent?.Sub;
    }

    public async Task<IssueReportResult> CreateAsync(
        string callerSub, Guid matchId, ReportIssueRequest req, CancellationToken ct)
    {
        if (!_opts.IsTrusted(callerSub)) return IssueReportResult.Fail(ReportFailure.NotAllowlisted);

        var match = await _matches.GetByIdAsync(matchId, ct);
        if (match == null) return IssueReportResult.Fail(ReportFailure.MatchNotFound);

        var isParty = callerSub == match.Creator.Sub || callerSub == match.Opponent?.Sub;
        if (!isParty) return IssueReportResult.Fail(ReportFailure.NotParticipant);

        var since = DateTime.UtcNow.AddHours(-1);
        if (await _reports.CountBySubSinceAsync(callerSub, since, ct) >= _opts.MaxReportsPerHour)
            return IssueReportResult.Fail(ReportFailure.RateLimited);

        var title = BuildTitle(req.Description, matchId);
        var body = BuildBody(callerSub, match, req);

        var issue = await _github.CreateIssueAsync(title, body, "app-report", ct);
        if (issue == null) return IssueReportResult.Fail(ReportFailure.GitHubFailed);

        var now = DateTime.UtcNow;
        var report = new MatchReport
        {
            Id = Guid.NewGuid(), ReporterSub = callerSub, MatchId = matchId,
            GameId = match.GameId, IssueNumber = issue.Number, IssueUrl = issue.HtmlUrl,
            Repo = null, Title = title, Status = ReportStatus.IssueOpen,
            CreatedAt = now, UpdatedAt = now,
        };
        await _reports.InsertAsync(report, ct);

        return IssueReportResult.Ok(new ReportIssueResponse(report.Id, issue.Number, issue.HtmlUrl));
    }

    private static string BuildTitle(string description, Guid matchId)
    {
        var firstLine = (description ?? "").Trim().Split('\n')[0];
        if (firstLine.Length > 80) firstLine = firstLine[..80];
        if (firstLine.Length == 0) firstLine = "In-app report";
        return $"[app-report] {firstLine} ({matchId.ToString()[..8]})";
    }

    // GitHub rejects an issue body over 65536 chars (HTTP 422). Stay under it
    // with a safety margin for the markdown scaffolding around the JSON.
    internal const int MaxIssueBodyChars = 60000;

    private string BuildBody(string sub, Match match, ReportIssueRequest req)
    {
        var prefix = new StringBuilder();
        prefix.AppendLine($"**Reporter:** `{sub}`  ");
        prefix.AppendLine($"**Match:** `{match.Id}`  GameId: `{match.GameId}`  ");
        prefix.AppendLine($"**Archetypes:** {match.Creator.Handle} vs {match.Opponent?.Handle}  ");
        prefix.AppendLine();
        prefix.AppendLine("### Description");
        prefix.AppendLine(req.Description ?? "(none)");
        prefix.AppendLine();
        var head = prefix.ToString();

        // Fit the diagnostic bundle under GitHub's 65536-char body limit. Shrink
        // in steps (lazily — stop at the first candidate that fits): full replay →
        // fewer entries → none → drop the full-reveal state too. A big turn-7
        // board + 300 replay events otherwise overflows and 422s the issue.
        IEnumerable<string> Candidates()
        {
            foreach (var cap in new[] { _opts.ReplayCap, 100, 40, 10, 0 })
                yield return Compose(head, match, req.Telemetry, replayCap: cap, includeState: true,
                    note: cap < _opts.ReplayCap
                        ? $"replay trimmed to the last {cap} events to fit GitHub's 65536-char limit"
                        : null);
            yield return Compose(head, match, req.Telemetry, replayCap: 0, includeState: false,
                note: "replay + full state omitted to fit GitHub's 65536-char limit");
        }
        return FitWithinLimit(Candidates(), MaxIssueBodyChars);
    }

    private string Compose(string head, Match match, ClientTelemetryDto? telemetry,
        int replayCap, bool includeState, string? note)
    {
        var bundle = AssembleBundle(match, telemetry, replayCap, includeState);
        var json = JsonSerializer.Serialize(bundle, new JsonSerializerOptions { WriteIndented = true });
        var sb = new StringBuilder(head);
        if (note != null) { sb.AppendLine($"> ⚠️ {note}"); sb.AppendLine(); }
        sb.AppendLine("<details><summary>Diagnostic bundle (replay + state + client telemetry)</summary>");
        sb.AppendLine();
        sb.AppendLine("```json");
        sb.AppendLine(json);
        sb.AppendLine("```");
        sb.AppendLine("</details>");
        return sb.ToString();
    }

    /// <summary>Pure selector: return the first candidate (largest first) whose
    /// length is within <paramref name="maxChars"/>; if none fit, hard-truncate
    /// the smallest. Lazy over the source so callers can avoid composing
    /// candidates they don't need.</summary>
    internal static string FitWithinLimit(IEnumerable<string> candidatesLargestFirst, int maxChars)
    {
        var last = "";
        foreach (var c in candidatesLargestFirst)
        {
            last = c;
            if (c.Length <= maxChars) return c;
        }
        return last.Length > maxChars ? last[..maxChars] : last;
    }

    private object AssembleBundle(Match match, ClientTelemetryDto? telemetry, int replayCap, bool includeState)
    {
        object? state = null;
        object? replay = null;

        if (includeState && match.GameId is Guid gid && _gameFactory?.Get(gid) is { } facade)
        {
            try { state = facade.GetState(); } catch { /* facade torn down — omit */ }
        }
        if (replayCap > 0 && _replayBuffer != null)
        {
            var dto = _replayBuffer.GetReplay(match.Id);
            if (dto != null)
            {
                IReadOnlyList<ReplayEntry> entries = dto.Entries;
                if (entries.Count > replayCap)
                    entries = entries.Skip(entries.Count - replayCap).ToList();
                replay = new { dto.MatchId, dto.SealedAt, dto.Truncated, dto.EntryCount, Entries = entries };
            }
        }
        return new
        {
            match = new { match.Id, match.GameId, match.State, Creator = match.Creator.Handle, Opponent = match.Opponent?.Handle },
            state,
            replay,
            client = telemetry,
        };
    }
}
