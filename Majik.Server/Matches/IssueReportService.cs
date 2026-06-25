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

    private string BuildBody(string sub, Match match, ReportIssueRequest req)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"**Reporter:** `{sub}`  ");
        sb.AppendLine($"**Match:** `{match.Id}`  GameId: `{match.GameId}`  ");
        sb.AppendLine($"**Archetypes:** {match.Creator.Handle} vs {match.Opponent?.Handle}  ");
        sb.AppendLine();
        sb.AppendLine("### Description");
        sb.AppendLine(req.Description ?? "(none)");
        sb.AppendLine();

        var bundle = AssembleBundle(match, req.Telemetry);
        var json = JsonSerializer.Serialize(bundle, new JsonSerializerOptions { WriteIndented = true });
        sb.AppendLine("<details><summary>Diagnostic bundle (replay + state + client telemetry)</summary>");
        sb.AppendLine();
        sb.AppendLine("```json");
        sb.AppendLine(json);
        sb.AppendLine("```");
        sb.AppendLine("</details>");
        return sb.ToString();
    }

    private object AssembleBundle(Match match, ClientTelemetryDto? telemetry)
    {
        object? state = null;
        object? replay = null;

        if (match.GameId is Guid gid && _gameFactory?.Get(gid) is { } facade)
        {
            try { state = facade.GetState(); } catch { /* facade torn down — omit */ }
        }
        if (_replayBuffer != null)
        {
            var dto = _replayBuffer.GetReplay(match.Id);
            if (dto != null)
            {
                IReadOnlyList<ReplayEntry> entries = dto.Entries;
                if (entries.Count > _opts.ReplayCap)
                    entries = entries.Skip(entries.Count - _opts.ReplayCap).ToList();
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
