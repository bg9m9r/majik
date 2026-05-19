using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using FluentAssertions;
using Majik.Server.Profiles;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MongoDB.Driver;
using Xunit;

namespace Majik.Server.Tests.Profiles;

public class ProfileEndpointsTests : IClassFixture<TestMongoFixture>
{
    private readonly TestMongoFixture _fixture;

    public ProfileEndpointsTests(TestMongoFixture fixture)
    {
        _fixture = fixture;
    }

    private WebApplicationFactory<Program> Factory(IMongoDatabase db) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            // UseSetting wins over appsettings.Development.json (highest priority).
            builder.UseSetting("Mongo:ConnectionString", _fixture.ConnectionString);
            builder.UseSetting("Mongo:Database", db.DatabaseNamespace.DatabaseName);
            builder.ConfigureServices(services =>
            {
                services.RemoveAll(typeof(IHostedService)); // skip index initializer race
                services.PostConfigure<AuthenticationOptions>(opts =>
                {
                    opts.DefaultAuthenticateScheme = TestAuthHandler.SchemeName;
                    opts.DefaultChallengeScheme = TestAuthHandler.SchemeName;
                });
                services.AddAuthentication(TestAuthHandler.SchemeName)
                    .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.SchemeName, _ => { });
            });
        });

    private static HttpClient ClientFor(WebApplicationFactory<Program> factory, string? sub)
    {
        var client = factory.CreateClient();
        if (sub != null)
        {
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue(TestAuthHandler.SchemeName, sub);
        }
        return client;
    }

    [Fact]
    public async Task GetMe_Unauthenticated_Returns401()
    {
        var db = _fixture.NewDatabase();
        await new UserProfileRepository(db).EnsureIndexesAsync(CancellationToken.None);
        using var factory = Factory(db);
        var client = ClientFor(factory, sub: null);

        var resp = await client.GetAsync("/me");
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetMe_NoProfile_Returns404()
    {
        var db = _fixture.NewDatabase();
        await new UserProfileRepository(db).EnsureIndexesAsync(CancellationToken.None);
        using var factory = Factory(db);
        var client = ClientFor(factory, "stub-alice");

        var resp = await client.GetAsync("/me");
        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var body = await resp.Content.ReadFromJsonAsync<ProfileError>();
        body!.Error.Should().Be("no-profile");
    }

    [Fact]
    public async Task PutMe_ValidHandle_Returns200AndRoundTrips()
    {
        var db = _fixture.NewDatabase();
        await new UserProfileRepository(db).EnsureIndexesAsync(CancellationToken.None);
        using var factory = Factory(db);
        var client = ClientFor(factory, "stub-alice");

        var put = await client.PutAsJsonAsync("/me", new { handle = "Alice" });
        put.StatusCode.Should().Be(HttpStatusCode.OK);
        var saved = await put.Content.ReadFromJsonAsync<ProfileDto>();
        saved!.Handle.Should().Be("Alice");

        var get = await client.GetAsync("/me");
        get.StatusCode.Should().Be(HttpStatusCode.OK);
        var fetched = await get.Content.ReadFromJsonAsync<ProfileDto>();
        fetched!.Handle.Should().Be("Alice");
    }

    [Fact]
    public async Task PutMe_HandleTakenByOther_Returns409()
    {
        var db = _fixture.NewDatabase();
        await new UserProfileRepository(db).EnsureIndexesAsync(CancellationToken.None);
        using var factory = Factory(db);

        var alice = ClientFor(factory, "stub-alice");
        var bob = ClientFor(factory, "stub-bob");
        (await alice.PutAsJsonAsync("/me", new { handle = "alice" })).EnsureSuccessStatusCode();

        var put = await bob.PutAsJsonAsync("/me", new { handle = "Alice" }); // different casing
        put.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var body = await put.Content.ReadFromJsonAsync<ProfileError>();
        body!.Error.Should().Be("handle-taken");
    }

    [Fact]
    public async Task PutMe_Reserved_Returns409()
    {
        var db = _fixture.NewDatabase();
        await new UserProfileRepository(db).EnsureIndexesAsync(CancellationToken.None);
        using var factory = Factory(db);
        var client = ClientFor(factory, "stub-alice");

        var put = await client.PutAsJsonAsync("/me", new { handle = "admin" });
        put.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task PutMe_InvalidFormat_Returns400()
    {
        var db = _fixture.NewDatabase();
        await new UserProfileRepository(db).EnsureIndexesAsync(CancellationToken.None);
        using var factory = Factory(db);
        var client = ClientFor(factory, "stub-alice");

        var put = await client.PutAsJsonAsync("/me", new { handle = "x" });
        put.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await put.Content.ReadFromJsonAsync<ProfileError>();
        body!.Error.Should().Be("invalid-handle");
        body.Detail.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task PutMe_SameSubChange_UpdatesInPlace()
    {
        var db = _fixture.NewDatabase();
        await new UserProfileRepository(db).EnsureIndexesAsync(CancellationToken.None);
        using var factory = Factory(db);
        var client = ClientFor(factory, "stub-alice");

        (await client.PutAsJsonAsync("/me", new { handle = "alice" })).EnsureSuccessStatusCode();
        var second = await client.PutAsJsonAsync("/me", new { handle = "AliceB" });
        second.StatusCode.Should().Be(HttpStatusCode.OK);
        var saved = await second.Content.ReadFromJsonAsync<ProfileDto>();
        saved!.Handle.Should().Be("AliceB");
    }
}

/// <summary>Test auth scheme: takes the scheme parameter value as the
/// <c>sub</c> claim. No signature validation — keeps endpoint tests focused
/// on the handler logic, not JwtBearer plumbing.</summary>
internal sealed class TestAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "TestAuth";

    public TestAuthHandler(
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
