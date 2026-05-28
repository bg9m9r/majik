using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Majik.Server.Composition;
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
/// Verifies the split rate-limit policies:
///   "expensive"  — 60 req/min — deck CRUD, match create/join, card search.
///   "in-match"   — 600 req/min — commands, state, roll, play-draw, concede, replay.
///
/// Tests confirm:
///  1. Bursting "expensive" routes past 60 yields 429.
///  2. Bursting "in-match" routes past 60 (but under 600) does NOT yield 429.
///  3. Heavy "in-match" traffic does not consume the "expensive" bucket
///     (buckets are independent).
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

    private static HttpClient AuthedClient(WebApplicationFactory<Program> factory, string sub)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue(MatchTestAuthHandler.SchemeName, sub);
        return client;
    }

    // -------------------------------------------------------------------------
    // "expensive" policy — 60 req/min cap enforced
    // -------------------------------------------------------------------------

    /// <summary>
    /// A user issuing 70 GET /decks requests in a single fixed window must
    /// hit 429 once the 60-permit "expensive" bucket is exhausted.
    /// This preserves the original behaviour that was under "authed-60-per-min".
    /// </summary>
    [Fact]
    public async Task DecksList_BurstBeyondPermitLimit_Returns429()
    {
        var db = _fixture.NewDatabase();
        using var factory = Factory(db);

        // Unique sub per test so partitions don't bleed across parallel runs.
        var sub = "burst-expensive-" + Guid.NewGuid().ToString("N");
        var client = AuthedClient(factory, sub);

        var statuses = new List<HttpStatusCode>(70);
        for (int i = 0; i < 70; i++)
        {
            var resp = await client.GetAsync("/decks");
            statuses.Add(resp.StatusCode);
        }

        statuses.Should().Contain(HttpStatusCode.TooManyRequests,
            $"the 'expensive' policy ({RateLimitPolicies.Expensive}) should throttle a 70-request burst on /decks");
    }

    /// <summary>
    /// A user hammering POST /matches past 60 in a minute must receive 429.
    /// Match creation is an "expensive" route.
    /// </summary>
    [Fact]
    public async Task MatchCreate_BurstBeyondPermitLimit_Returns429()
    {
        var db = _fixture.NewDatabase();
        using var factory = Factory(db);

        var sub = "burst-match-create-" + Guid.NewGuid().ToString("N");
        var client = AuthedClient(factory, sub);

        var statuses = new List<HttpStatusCode>(70);
        for (int i = 0; i < 70; i++)
        {
            // The body is intentionally invalid (deckId blank) so we get
            // 400 rather than creating 70 real match documents, but the
            // rate-limiter runs before handler logic and will emit 429 first.
            var resp = await client.PostAsJsonAsync("/matches",
                new { format = "constructed", visibility = "public", deckId = "", clockMinutes = 20 });
            statuses.Add(resp.StatusCode);
        }

        statuses.Should().Contain(HttpStatusCode.TooManyRequests,
            $"the 'expensive' policy should throttle POST /matches beyond 60 req/min");
    }

    // -------------------------------------------------------------------------
    // "in-match" policy — 600 req/min; 70 requests must NOT yield 429
    // -------------------------------------------------------------------------

    /// <summary>
    /// A user submitting 70 POST /matches/{id}/commands requests (all of which
    /// return 404 because the match doesn't exist) must NOT receive 429 —
    /// the "in-match" bucket is 600 req/min.
    /// Before this slice, these requests would have hit the tight 60 req/min
    /// cap and 429'd at ~61.
    /// </summary>
    [Fact]
    public async Task Commands_BurstPast60ButUnder600_DoesNotReturn429()
    {
        var db = _fixture.NewDatabase();
        using var factory = Factory(db);

        var sub = "burst-commands-" + Guid.NewGuid().ToString("N");
        var client = AuthedClient(factory, sub);
        // A non-existent match ID — handler returns 404, but rate-limiter
        // runs before handler logic so we observe the limiter's decision first.
        var matchId = Guid.NewGuid();

        var statuses = new List<HttpStatusCode>(70);
        for (int i = 0; i < 70; i++)
        {
            var resp = await client.PostAsJsonAsync($"/matches/{matchId}/commands",
                new { type = "PassPriority" });
            statuses.Add(resp.StatusCode);
        }

        statuses.Should().NotContain(HttpStatusCode.TooManyRequests,
            $"the 'in-match' policy ({RateLimitPolicies.InMatch}, 600 req/min) should not throttle 70 requests");
    }

    /// <summary>
    /// A user retrieving GET /matches/{id}/state 70 times must NOT receive 429.
    /// State polls are "in-match" (600 req/min).
    /// </summary>
    [Fact]
    public async Task StateGet_BurstPast60ButUnder600_DoesNotReturn429()
    {
        var db = _fixture.NewDatabase();
        using var factory = Factory(db);

        var sub = "burst-state-" + Guid.NewGuid().ToString("N");
        var client = AuthedClient(factory, sub);
        var matchId = Guid.NewGuid();

        var statuses = new List<HttpStatusCode>(70);
        for (int i = 0; i < 70; i++)
        {
            var resp = await client.GetAsync($"/matches/{matchId}/state");
            statuses.Add(resp.StatusCode);
        }

        statuses.Should().NotContain(HttpStatusCode.TooManyRequests,
            "GET /matches/{id}/state is 'in-match' policy and should not throttle 70 requests");
    }

    // -------------------------------------------------------------------------
    // Independence: "in-match" traffic does not consume the "expensive" bucket
    // -------------------------------------------------------------------------

    /// <summary>
    /// Hammering 70 "in-match" requests (POST /matches/{id}/commands) should
    /// NOT cause the subsequent "expensive" request (GET /decks) to 429.
    /// The two policies are independent per-sub buckets.
    /// </summary>
    [Fact]
    public async Task InMatchTraffic_DoesNotExhaustExpensiveBucket()
    {
        var db = _fixture.NewDatabase();
        using var factory = Factory(db);

        // Unique sub so this test has an untouched "expensive" partition.
        var sub = "independence-" + Guid.NewGuid().ToString("N");
        var client = AuthedClient(factory, sub);
        var matchId = Guid.NewGuid();

        // Exhaust more than the "expensive" limit via in-match route.
        for (int i = 0; i < 70; i++)
        {
            await client.PostAsJsonAsync($"/matches/{matchId}/commands",
                new { type = "PassPriority" });
        }

        // Now hit an "expensive" route — should NOT be throttled because
        // the "expensive" bucket for this sub is still full.
        var decksResp = await client.GetAsync("/decks");
        decksResp.StatusCode.Should().NotBe(HttpStatusCode.TooManyRequests,
            "heavy 'in-match' traffic should not consume the 'expensive' bucket");
    }

    /// <summary>
    /// Symmetrical independence check: exhausting the "expensive" bucket via
    /// GET /decks does NOT throttle subsequent "in-match" (commands) requests.
    /// </summary>
    [Fact]
    public async Task ExpensiveTraffic_DoesNotExhaustInMatchBucket()
    {
        var db = _fixture.NewDatabase();
        using var factory = Factory(db);

        var sub = "independence-rev-" + Guid.NewGuid().ToString("N");
        var client = AuthedClient(factory, sub);
        var matchId = Guid.NewGuid();

        // Fill and overflow the "expensive" bucket.
        for (int i = 0; i < 70; i++)
        {
            await client.GetAsync("/decks");
        }

        // The "in-match" bucket for this sub is untouched; first request
        // to an "in-match" route must succeed (not 429).
        var commandsResp = await client.PostAsJsonAsync($"/matches/{matchId}/commands",
            new { type = "PassPriority" });
        commandsResp.StatusCode.Should().NotBe(HttpStatusCode.TooManyRequests,
            "an exhausted 'expensive' bucket should not bleed into the 'in-match' bucket");
    }
}
