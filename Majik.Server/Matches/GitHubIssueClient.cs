using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Majik.Server.Composition;

namespace Majik.Server.Matches;

public sealed class GitHubIssueClient : IGitHubIssueClient
{
    private readonly HttpClient _http;
    private readonly GitHubOptions _opts;

    public GitHubIssueClient(HttpClient http, GitHubOptions opts)
    {
        _http = http;
        _opts = opts;
    }

    public async Task<CreatedIssue?> CreateIssueAsync(string title, string body, string label, CancellationToken ct)
    {
        if (!_opts.IsConfigured) return null;

        var req = new HttpRequestMessage(HttpMethod.Post,
            $"https://api.github.com/repos/{_opts.RepositoryOwner}/{_opts.RepositoryName}/issues")
        {
            Content = JsonContent.Create(new { title, body, labels = new[] { label } }),
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _opts.Token);
        req.Headers.UserAgent.ParseAdd("majik-server");
        req.Headers.Accept.ParseAdd("application/vnd.github+json");

        using var resp = await _http.SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode) return null;

        using var stream = await resp.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        var root = doc.RootElement;
        return new CreatedIssue(
            root.GetProperty("number").GetInt32(),
            root.GetProperty("html_url").GetString() ?? "");
    }
}
