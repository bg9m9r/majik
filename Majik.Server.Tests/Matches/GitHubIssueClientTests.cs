using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Majik.Server.Composition;
using Majik.Server.Matches;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Majik.Server.Tests.Matches;

public class GitHubIssueClientTests
{
    private sealed class StubHandler : HttpMessageHandler
    {
        public HttpRequestMessage? Last;
        public HttpResponseMessage Response = new(HttpStatusCode.Created);
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Last = request;
            if (request.Content != null) await request.Content.ReadAsStringAsync(ct); // realise body
            return Response;
        }
    }

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public readonly List<(LogLevel Level, string Message)> Entries = new();
        private sealed class Scope : IDisposable { public void Dispose() { } }
        public IDisposable BeginScope<TState>(TState state) where TState : notnull => new Scope();
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel level, EventId id, TState state, Exception? ex,
            Func<TState, Exception?, string> formatter) => Entries.Add((level, formatter(state, ex)));
    }

    [Fact]
    public async Task CreateIssue_on_422_body_too_long_returns_null_and_logs_error()
    {
        // The exact prod-failure path: a too-large body → GitHub 422. Must NOT
        // throw; returns null + logs WHY (previously swallowed → undiagnosable 502).
        var handler = new StubHandler
        {
            Response = new HttpResponseMessage((HttpStatusCode)422)
            {
                Content = new StringContent("{\"message\":\"Validation Failed\",\"errors\":[{\"field\":\"body\",\"code\":\"invalid\"}]}"),
            },
        };
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.github.com/") };
        var logger = new CapturingLogger<GitHubIssueClient>();
        var client = new GitHubIssueClient(http,
            new GitHubOptions { Token = "t", RepositoryOwner = "o", RepositoryName = "r" }, logger);

        var result = await client.CreateIssueAsync("title", "body", "app-report", default);

        result.Should().BeNull();
        logger.Entries.Should().Contain(e => e.Level == LogLevel.Error && e.Message.Contains("422"));
    }

    [Fact]
    public async Task CreateIssue_posts_to_repo_and_parses_number_and_url()
    {
        var handler = new StubHandler
        {
            Response = new HttpResponseMessage(HttpStatusCode.Created)
            {
                Content = JsonContent.Create(new { number = 42, html_url = "https://github.com/o/r/issues/42" }),
            },
        };
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.github.com/") };
        var opts = new GitHubOptions { Token = "t", RepositoryOwner = "o", RepositoryName = "r" };
        var client = new GitHubIssueClient(http, opts);

        var result = await client.CreateIssueAsync("title", "body", "app-report", default);

        result.Should().NotBeNull();
        result!.Number.Should().Be(42);
        result.HtmlUrl.Should().Be("https://github.com/o/r/issues/42");
        handler.Last!.Method.Should().Be(HttpMethod.Post);
        handler.Last.RequestUri!.AbsolutePath.Should().Be("/repos/o/r/issues");
    }

    [Fact]
    public async Task CreateIssue_returns_null_when_not_configured()
    {
        var http = new HttpClient(new StubHandler());
        var client = new GitHubIssueClient(http, new GitHubOptions()); // no token/repo
        var result = await client.CreateIssueAsync("t", "b", "app-report", default);
        result.Should().BeNull();
    }
}
