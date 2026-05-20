using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using FluentAssertions;
using Majik.Server.Matches;
using Majik.Server.Profiles;
using Majik.Server.Tests.Profiles;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MongoDB.Driver;
using Xunit;

namespace Majik.Server.Tests.Matches;

/// <summary>
/// Integration tests for the /matches lifecycle endpoints using a real
/// EphemeralMongo instance and a stubbed auth handler.
/// </summary>
public class MatchEndpointsLifecycleTests : IClassFixture<TestMongoFixture>
{
    private readonly TestMongoFixture _fixture;

    public MatchEndpointsLifecycleTests(TestMongoFixture fixture) => _fixture = fixture;

    // -----------------------------------------------------------------------
    // Factory helpers
    // -----------------------------------------------------------------------

    private WebApplicationFactory<Program> Factory(
        IMongoDatabase db,
        IRandomSource? rng = null)
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

                if (rng != null)
                {
                    services.RemoveAll<IRandomSource>();
                    services.AddSingleton<IRandomSource>(rng);
                }
            });
        });
    }

    /// <summary>Factory that deliberately omits a Mongo connection string to
    /// trigger the 503 short-circuit in all /matches endpoints.</summary>
    private static WebApplicationFactory<Program> NoMongoFactory() =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Mongo:ConnectionString", "");
            builder.UseSetting("Mongo:Database", "");
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

    private static HttpClient AuthedClient(WebApplicationFactory<Program> factory, string sub)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue(MatchTestAuthHandler.SchemeName, sub);
        return client;
    }

    private static HttpClient UnauthClient(WebApplicationFactory<Program> factory) =>
        factory.CreateClient();

    private async Task<IMongoDatabase> FreshDb()
    {
        var db = _fixture.NewDatabase();
        await new MatchRepository(db).EnsureIndexesAsync(CancellationToken.None);
        await new UserProfileRepository(db).EnsureIndexesAsync(CancellationToken.None);
        return db;
    }

    private async Task SeedProfile(IMongoDatabase db, string sub, string handle)
    {
        var repo = new UserProfileRepository(db);
        await repo.UpsertAsync(new UserProfile
        {
            Sub = sub,
            Handle = handle.ToLowerInvariant(),
            HandleDisplay = handle,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        }, CancellationToken.None);
    }

    // -----------------------------------------------------------------------
    // 401 Unauthenticated for every endpoint
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData("POST", "/matches")]
    [InlineData("GET", "/matches")]
    [InlineData("GET", "/matches/00000000-0000-0000-0000-000000000001")]
    [InlineData("POST", "/matches/00000000-0000-0000-0000-000000000001/join")]
    [InlineData("POST", "/matches/00000000-0000-0000-0000-000000000001/play-draw")]
    [InlineData("POST", "/matches/00000000-0000-0000-0000-000000000001/concede")]
    [InlineData("DELETE", "/matches/00000000-0000-0000-0000-000000000001")]
    [InlineData("POST", "/matches/00000000-0000-0000-0000-000000000001/commands")]
    [InlineData("GET", "/matches/00000000-0000-0000-0000-000000000001/state")]
    public async Task Endpoint_Unauthenticated_Returns401(string method, string path)
    {
        var db = await FreshDb();
        using var factory = Factory(db);
        var client = UnauthClient(factory);

        var request = new HttpRequestMessage(new HttpMethod(method), path);
        if (method == "POST" || method == "DELETE")
            request.Content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json");

        var resp = await client.SendAsync(request);
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            $"{method} {path} should require auth");
    }

    // -----------------------------------------------------------------------
    // POST /matches — no profile → 403 no-profile
    // -----------------------------------------------------------------------

    [Fact]
    public async Task CreateMatch_NoProfile_Returns403NoProfile()
    {
        var db = await FreshDb();
        // no profile seeded for this sub
        using var factory = Factory(db);
        var client = AuthedClient(factory, "no-profile-user");

        var resp = await client.PostAsJsonAsync("/matches",
            new { format = "constructed", visibility = "public", deckId = "deck-a", clockMinutes = 20 });

        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var body = await resp.Content.ReadFromJsonAsync<MatchError>();
        body!.Error.Should().Be("no-profile");
    }

    // -----------------------------------------------------------------------
    // POST /matches — happy path → 201 with MatchDto
    // -----------------------------------------------------------------------

    [Fact]
    public async Task CreateMatch_ValidRequest_Returns201WithMatchDto()
    {
        var db = await FreshDb();
        await SeedProfile(db, "alice", "Alice");
        using var factory = Factory(db);
        var client = AuthedClient(factory, "alice");

        var resp = await client.PostAsJsonAsync("/matches",
            new { format = "constructed", visibility = "public", deckId = "deck-a", clockMinutes = 20 });

        resp.StatusCode.Should().Be(HttpStatusCode.Created);
        var dto = await resp.Content.ReadFromJsonAsync<MatchDto>();
        dto.Should().NotBeNull();
        dto!.State.Should().Be("Open");
        dto.Visibility.Should().Be("Public");
        dto.Format.Should().Be("constructed");
        dto.ClockMinutes.Should().Be(20);
        dto.Creator.Sub.Should().Be("alice");
        dto.Creator.DeckId.Should().Be("deck-a");
        dto.Opponent.Should().BeNull();
        dto.Id.Should().NotBe(Guid.Empty);
        resp.Headers.Location.Should().NotBeNull();
        resp.Headers.Location!.ToString().Should().Contain(dto.Id.ToString());
    }

    // -----------------------------------------------------------------------
    // POST /matches — invalid bodies → 400
    // -----------------------------------------------------------------------

    [Fact]
    public async Task CreateMatch_BlankDeckId_Returns400()
    {
        var db = await FreshDb();
        await SeedProfile(db, "alice", "Alice");
        using var factory = Factory(db);
        var client = AuthedClient(factory, "alice");

        var resp = await client.PostAsJsonAsync("/matches",
            new { format = "constructed", visibility = "public", deckId = "", clockMinutes = 20 });

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task CreateMatch_BadVisibility_Returns400()
    {
        var db = await FreshDb();
        await SeedProfile(db, "alice", "Alice");
        using var factory = Factory(db);
        var client = AuthedClient(factory, "alice");

        var resp = await client.PostAsJsonAsync("/matches",
            new { format = "constructed", visibility = "secret", deckId = "deck-a", clockMinutes = 20 });

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task CreateMatch_InvalidClockMinutes_Returns400()
    {
        var db = await FreshDb();
        await SeedProfile(db, "alice", "Alice");
        using var factory = Factory(db);
        var client = AuthedClient(factory, "alice");

        var resp = await client.PostAsJsonAsync("/matches",
            new { format = "constructed", visibility = "public", deckId = "deck-a", clockMinutes = 17 });

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await resp.Content.ReadFromJsonAsync<MatchError>();
        body!.Error.Should().Be("invalid-clock-minutes");
    }

    // -----------------------------------------------------------------------
    // GET /matches?visibility=public — lists only Open+Public
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ListMatches_ReturnsOnlyOpenPublicMatches()
    {
        var db = await FreshDb();
        await SeedProfile(db, "alice", "Alice");
        await SeedProfile(db, "bob", "Bob");
        using var factory = Factory(db);
        var aliceClient = AuthedClient(factory, "alice");
        var bobClient = AuthedClient(factory, "bob");

        // Create a public match
        var pub = await aliceClient.PostAsJsonAsync("/matches",
            new { format = "constructed", visibility = "public", deckId = "deck-a", clockMinutes = 20 });
        pub.StatusCode.Should().Be(HttpStatusCode.Created);

        // Create an invite match
        var inv = await bobClient.PostAsJsonAsync("/matches",
            new { format = "constructed", visibility = "invite", deckId = "deck-b", clockMinutes = 20 });
        inv.StatusCode.Should().Be(HttpStatusCode.Created);

        var listResp = await aliceClient.GetAsync("/matches?visibility=public");
        listResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var list = await listResp.Content.ReadFromJsonAsync<MatchDto[]>();
        list.Should().NotBeNull();
        list!.Should().HaveCount(1);
        list![0].Visibility.Should().Be("Public");
        list![0].State.Should().Be("Open");
    }

    // -----------------------------------------------------------------------
    // GET /matches/{id} — Invite match by non-party → 403 private-match
    // -----------------------------------------------------------------------

    [Fact]
    public async Task GetMatch_InviteMatchByNonParty_Returns403PrivateMatch()
    {
        var db = await FreshDb();
        await SeedProfile(db, "alice", "Alice");
        await SeedProfile(db, "charlie", "Charlie");
        using var factory = Factory(db);
        var aliceClient = AuthedClient(factory, "alice");
        var charlieClient = AuthedClient(factory, "charlie");

        var created = await aliceClient.PostAsJsonAsync("/matches",
            new { format = "constructed", visibility = "invite", deckId = "deck-a", clockMinutes = 20 });
        created.StatusCode.Should().Be(HttpStatusCode.Created);
        var dto = await created.Content.ReadFromJsonAsync<MatchDto>();

        var resp = await charlieClient.GetAsync($"/matches/{dto!.Id}");
        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var body = await resp.Content.ReadFromJsonAsync<MatchError>();
        body!.Error.Should().Be("private-match");
    }

    // -----------------------------------------------------------------------
    // POST /matches/{id}/join — self-join → 409 self-join-forbidden
    // -----------------------------------------------------------------------

    [Fact]
    public async Task JoinMatch_BySelf_Returns409SelfJoinForbidden()
    {
        var db = await FreshDb();
        await SeedProfile(db, "alice", "Alice");
        using var factory = Factory(db);
        var client = AuthedClient(factory, "alice");

        var created = await client.PostAsJsonAsync("/matches",
            new { format = "constructed", visibility = "public", deckId = "deck-a", clockMinutes = 20 });
        created.StatusCode.Should().Be(HttpStatusCode.Created);
        var dto = await created.Content.ReadFromJsonAsync<MatchDto>();

        var resp = await client.PostAsJsonAsync($"/matches/{dto!.Id}/join",
            new { deckId = "deck-a" });
        resp.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var body = await resp.Content.ReadFromJsonAsync<MatchError>();
        body!.Error.Should().Be("self-join-forbidden");
    }

    // -----------------------------------------------------------------------
    // POST /matches/{id}/play-draw — by non-winner → 403 not-roll-winner
    // -----------------------------------------------------------------------

    [Fact]
    public async Task PlayDraw_ByNonWinner_Returns403NotRollWinner()
    {
        var db = await FreshDb();
        await SeedProfile(db, "alice", "Alice");
        await SeedProfile(db, "bob", "Bob");
        // Stub: alice wins the roll (creator=6, opponent=1)
        var rng = new StubRandomSource(new Queue<int>(new[] { 6, 1 }));
        using var factory = Factory(db, rng);
        var aliceClient = AuthedClient(factory, "alice");
        var bobClient = AuthedClient(factory, "bob");

        // Alice creates
        var created = await aliceClient.PostAsJsonAsync("/matches",
            new { format = "constructed", visibility = "public", deckId = "deck-a", clockMinutes = 20 });
        var matchDto = await created.Content.ReadFromJsonAsync<MatchDto>();

        // Bob joins → triggers roll (alice wins: 6 vs 1)
        var joined = await bobClient.PostAsJsonAsync($"/matches/{matchDto!.Id}/join",
            new { deckId = "deck-b" });
        joined.StatusCode.Should().Be(HttpStatusCode.OK);
        var joinedDto = await joined.Content.ReadFromJsonAsync<MatchDto>();
        joinedDto!.Roll.Should().NotBeNull();
        joinedDto.Roll!.WinnerSub.Should().Be("alice");

        // Bob (non-winner) tries to choose play/draw
        var resp = await bobClient.PostAsJsonAsync($"/matches/{matchDto.Id}/play-draw",
            new { choice = "play" });
        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var body = await resp.Content.ReadFromJsonAsync<MatchError>();
        body!.Error.Should().Be("not-roll-winner");
    }

    // -----------------------------------------------------------------------
    // POST /matches/{id}/play-draw — invalid choice → 400 invalid-choice
    // -----------------------------------------------------------------------

    [Fact]
    public async Task PlayDraw_InvalidChoice_Returns400InvalidChoice()
    {
        var db = await FreshDb();
        await SeedProfile(db, "alice", "Alice");
        await SeedProfile(db, "bob", "Bob");
        // Stub: alice wins the roll
        var rng = new StubRandomSource(new Queue<int>(new[] { 6, 1 }));
        using var factory = Factory(db, rng);
        var aliceClient = AuthedClient(factory, "alice");
        var bobClient = AuthedClient(factory, "bob");

        var created = await aliceClient.PostAsJsonAsync("/matches",
            new { format = "constructed", visibility = "public", deckId = "deck-a", clockMinutes = 20 });
        var matchDto = await created.Content.ReadFromJsonAsync<MatchDto>();

        await bobClient.PostAsJsonAsync($"/matches/{matchDto!.Id}/join",
            new { deckId = "deck-b" });

        // Alice (winner) sends bad choice
        var resp = await aliceClient.PostAsJsonAsync($"/matches/{matchDto.Id}/play-draw",
            new { choice = "flip" });
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await resp.Content.ReadFromJsonAsync<MatchError>();
        body!.Error.Should().Be("invalid-choice");
    }

    // -----------------------------------------------------------------------
    // DELETE /matches/{id} — by non-creator → 403 forbidden
    // -----------------------------------------------------------------------

    [Fact]
    public async Task DeleteMatch_ByNonCreator_Returns403Forbidden()
    {
        var db = await FreshDb();
        await SeedProfile(db, "alice", "Alice");
        await SeedProfile(db, "bob", "Bob");
        using var factory = Factory(db);
        var aliceClient = AuthedClient(factory, "alice");
        var bobClient = AuthedClient(factory, "bob");

        var created = await aliceClient.PostAsJsonAsync("/matches",
            new { format = "constructed", visibility = "public", deckId = "deck-a", clockMinutes = 20 });
        var matchDto = await created.Content.ReadFromJsonAsync<MatchDto>();

        var resp = await bobClient.DeleteAsync($"/matches/{matchDto!.Id}");
        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var body = await resp.Content.ReadFromJsonAsync<MatchError>();
        body!.Error.Should().Be("forbidden");
    }

    // -----------------------------------------------------------------------
    // 503 when Mongo is unconfigured
    // -----------------------------------------------------------------------

    [Fact]
    public async Task PostMatches_MongoUnconfigured_Returns503()
    {
        using var factory = NoMongoFactory();
        var client = AuthedClient(factory, "alice");

        var resp = await client.PostAsJsonAsync("/matches",
            new { format = "constructed", visibility = "public", deckId = "deck-a", clockMinutes = 20 });

        resp.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        var body = await resp.Content.ReadFromJsonAsync<MatchError>();
        body!.Error.Should().Be("mongo-not-configured");
    }
}

// ---------------------------------------------------------------------------
// Stubs
// ---------------------------------------------------------------------------

/// <summary>Deterministic IRandomSource backed by a pre-loaded queue of values.
/// Loops when exhausted.</summary>
internal sealed class StubRandomSource : IRandomSource
{
    private readonly Queue<int> _values;

    public StubRandomSource(Queue<int> values) => _values = values;

    public int NextInt(int minInclusive, int maxExclusive)
    {
        if (_values.Count == 0)
            throw new InvalidOperationException("StubRandomSource queue exhausted.");
        return _values.Dequeue();
    }
}

/// <summary>Test auth scheme for /matches endpoint integration tests.
/// Reads the bearer token value as the <c>sub</c> claim.
/// No Authorization header → NoResult (→ 401).</summary>
internal sealed class MatchTestAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "MatchTestAuth";

    public MatchTestAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : base(options, logger, encoder) { }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var header = Request.Headers.Authorization.ToString();
        if (string.IsNullOrEmpty(header)) return Task.FromResult(AuthenticateResult.NoResult());
        var parts = header.Split(' ', 2);
        if (parts.Length != 2 || parts[0] != SchemeName)
            return Task.FromResult(AuthenticateResult.NoResult());

        var sub = parts[1];
        var identity = new ClaimsIdentity(new[] { new Claim("sub", sub) }, SchemeName);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, SchemeName);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
