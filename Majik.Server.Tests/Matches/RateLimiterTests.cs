using System.Net;
using System.Net.Http.Headers;
using FluentAssertions;
using Majik.Server.Matches;
using Majik.Server.Tests.Profiles;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using MongoDB.Driver;
using Xunit;

namespace Majik.Server.Tests.Matches;

/// <summary>
/// Verifies the per-user rate limiter installed on /decks, /matches, /cards.
/// Burst beyond the per-minute permit count must yield 429 — defending the
/// API against trivial DoS and credential-stuffing-on-cheap-endpoints.
/// </summary>
public class RateLimiterTests : IClassFixture<TestMongoFixture>
{
    private readonly TestMongoFixture _fixture;
    public RateLimiterTests(TestMongoFixture fixture) => _fixture = fixture;

    private WebApplicationFactory<Program> Factory(IMongoDatabase db)
    {
        return new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Mongo:ConnectionString", _fixture.ConnectionString);
            builder.UseSetting("Mongo:Database", db.DatabaseNamespace.DatabaseName);
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
            });
        });
    }

    [Fact]
    public async Task DecksList_BurstBeyondPermitLimit_Returns429()
    {
        var db = _fixture.NewDatabase();
        using var factory = Factory(db);

        // Use a unique sub per test so we don't bleed into other tests'
        // partitions (fixed-window limiter partitions on caller sub).
        var sub = "burst-" + Guid.NewGuid().ToString("N");
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue(MatchTestAuthHandler.SchemeName, sub);

        // Permit is 60/min. Issue 70 sequential requests; somewhere
        // past the 60th we expect a 429.
        var statuses = new List<HttpStatusCode>(70);
        for (int i = 0; i < 70; i++)
        {
            var resp = await client.GetAsync("/decks");
            statuses.Add(resp.StatusCode);
        }

        statuses.Should().Contain(HttpStatusCode.TooManyRequests,
            "the per-sub fixed-window limiter should throttle a 70-request burst");
    }
}
