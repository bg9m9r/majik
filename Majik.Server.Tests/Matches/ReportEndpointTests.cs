using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Majik.Server.Matches;
using Majik.Server.Profiles;
using Majik.Server.Tests.Profiles;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using MongoDB.Driver;
using Xunit;

namespace Majik.Server.Tests.Matches;

/// <summary>Endpoint tests for POST /matches/{id}/report. Mirrors the
/// MatchEndpointsLifecycleTests factory / authed-client / direct-seed pattern;
/// injects a fake IGitHubIssueClient + an allowlist via config so no real
/// GitHub call is made and the allowlist gate is exercised end-to-end.</summary>
public class ReportEndpointTests : IClassFixture<TestMongoFixture>
{
    private readonly TestMongoFixture _fixture;
    public ReportEndpointTests(TestMongoFixture fixture) => _fixture = fixture;

    private sealed class FakeGitHub : IGitHubIssueClient
    {
        public Task<CreatedIssue?> CreateIssueAsync(string title, string body, string label, CancellationToken ct)
            => Task.FromResult<CreatedIssue?>(new CreatedIssue(99, "https://github.com/o/r/issues/99"));
    }

    private async Task<IMongoDatabase> FreshDb()
    {
        var db = _fixture.NewDatabase();
        await new MatchRepository(db).EnsureIndexesAsync(CancellationToken.None);
        await new UserProfileRepository(db).EnsureIndexesAsync(CancellationToken.None);
        return db;
    }

    /// <summary>Directly insert a Playing match owned by <paramref name="ownerSub"/>
    /// (bot opponent) + seed the owner's profile. Avoids driving the full
    /// create/join/roll REST flow — we only need a participant + Playing state.</summary>
    private static async Task<Guid> SeedPlayingMatchOwnedBy(IMongoDatabase db, string ownerSub)
    {
        var profiles = new UserProfileRepository(db);
        await profiles.UpsertAsync(new UserProfile
        {
            Sub = ownerSub, Handle = ownerSub.ToLowerInvariant(), HandleDisplay = ownerSub,
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
        }, CancellationToken.None);

        var id = Guid.NewGuid();
        await new MatchRepository(db).InsertAsync(new Match
        {
            Id = id, State = MatchState.Playing,
            Visibility = MatchVisibility.Public, Format = "constructed", ClockMinutes = 20,
            Creator = new MatchPlayer { Sub = ownerSub, Handle = ownerSub, DeckId = "deck-a", DeckSnapshot = new() },
            Opponent = new MatchPlayer { Sub = "bot:burn", Handle = "Bot", DeckId = "bot", DeckSnapshot = new() },
            GameId = Guid.NewGuid(),
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
        }, CancellationToken.None);
        return id;
    }

    private WebApplicationFactory<Program> FactoryWith(
        IMongoDatabase db,
        IReadOnlyList<string> allowlist,
        Action<IServiceCollection>? configureServices = null)
    {
        return new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Mongo:ConnectionString", _fixture.ConnectionString);
            builder.UseSetting("Mongo:Database", db.DatabaseNamespace.DatabaseName);
            // Allowlist config (binds ReportingOptions in Program.cs). Empty list
            // → no trusted subs → 403 not-allowlisted.
            builder.UseSetting("Reporting:Enabled", "true");
            for (var i = 0; i < allowlist.Count; i++)
                builder.UseSetting($"Reporting:TrustedTesterSubs:{i}", allowlist[i]);

            builder.ConfigureServices(services =>
            {
                services.RemoveAll(typeof(IHostedService));
                services.PostConfigure<AuthenticationOptions>(opts =>
                {
                    opts.DefaultAuthenticateScheme = MatchTestAuthHandler.SchemeName;
                    opts.DefaultChallengeScheme = MatchTestAuthHandler.SchemeName;
                });
                services.AddAuthentication(MatchTestAuthHandler.SchemeName)
                    .AddScheme<AuthenticationSchemeOptions, MatchTestAuthHandler>(
                        MatchTestAuthHandler.SchemeName, _ => { });

                // No real GitHub call.
                services.RemoveAll<IGitHubIssueClient>();
                services.AddSingleton<IGitHubIssueClient, FakeGitHub>();

                configureServices?.Invoke(services);
            });
        });
    }

    private static HttpClient AuthedClient(WebApplicationFactory<Program> factory, string sub)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue(MatchTestAuthHandler.SchemeName, sub);
        return client;
    }

    [Fact]
    public async Task Report_by_allowlisted_participant_returns_201_and_issue()
    {
        var db = await FreshDb();
        var matchId = await SeedPlayingMatchOwnedBy(db, "alice");

        using var factory = FactoryWith(db, allowlist: new[] { "alice" });
        var client = AuthedClient(factory, "alice");

        var resp = await client.PostAsJsonAsync($"/matches/{matchId}/report",
            new { description = "Boltwave wedge", telemetry = new { route = "/match/x", buildSha = "abc123" } });

        resp.StatusCode.Should().Be(HttpStatusCode.Created);
        var dto = await resp.Content.ReadFromJsonAsync<ReportIssueResponse>();
        dto!.IssueNumber.Should().Be(99);
    }

    [Fact]
    public async Task Report_by_non_allowlisted_returns_403()
    {
        var db = await FreshDb();
        var matchId = await SeedPlayingMatchOwnedBy(db, "alice");
        using var factory = FactoryWith(db, allowlist: Array.Empty<string>());
        var client = AuthedClient(factory, "alice");

        var resp = await client.PostAsJsonAsync($"/matches/{matchId}/report",
            new { description = "bug", telemetry = (object?)null });

        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
