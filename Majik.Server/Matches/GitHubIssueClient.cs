using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Majik.Server.Composition;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Majik.Server.Matches;

public sealed class GitHubIssueClient : IGitHubIssueClient
{
    private readonly HttpClient _http;
    private readonly GitHubOptions _opts;
    private readonly ILogger<GitHubIssueClient> _logger;

    public GitHubIssueClient(HttpClient http, GitHubOptions opts, ILogger<GitHubIssueClient>? logger = null)
    {
        _http = http;
        _opts = opts;
        _logger = logger ?? NullLogger<GitHubIssueClient>.Instance;
    }

    public async Task<CreatedIssue?> CreateIssueAsync(string title, string body, string label, CancellationToken ct)
    {
        if (!_opts.IsConfigured)
        {
            _logger.LogWarning("GitHubIssueClient: not configured (missing Token/Owner/Name) — cannot create issue.");
            return null;
        }

        var req = new HttpRequestMessage(HttpMethod.Post,
            $"https://api.github.com/repos/{_opts.RepositoryOwner}/{_opts.RepositoryName}/issues")
        {
            Content = JsonContent.Create(new { title, body, labels = new[] { label } }),
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _opts.Token);
        req.Headers.UserAgent.ParseAdd("majik-server");
        req.Headers.Accept.ParseAdd("application/vnd.github+json");

        using var resp = await _http.SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode)
        {
            // Surface WHY (e.g. 422 body-too-long, 401 bad token) — previously
            // swallowed, which made the endpoint's 502 undiagnosable.
            var detail = await resp.Content.ReadAsStringAsync(ct);
            if (detail.Length > 600) detail = detail[..600];
            _logger.LogError(
                "GitHubIssueClient: create issue failed. Status={Status} BodyLen={BodyLen} Response={Detail}",
                (int)resp.StatusCode, body.Length, detail);
            return null;
        }

        using var stream = await resp.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        var root = doc.RootElement;
        return new CreatedIssue(
            root.GetProperty("number").GetInt32(),
            root.GetProperty("html_url").GetString() ?? "");
    }
}
