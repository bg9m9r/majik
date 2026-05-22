using FluentAssertions;
using Majik.CardsServer;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Majik.Server.Tests;

/// <summary>
/// Locks down the security contract for the cards-server internal-token
/// gate:
///   - constant-time compare via FixedTimeEquals (no early-exit on
///     length mismatch and no throw),
///   - fail-fast at startup when running in Production without a
///     configured token.
/// </summary>
public class InternalTokenMiddlewareTests
{
    private static InternalTokenMiddleware Build(string? token, string environmentName)
    {
        var cfg = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Cards:InternalToken"] = token,
            }!)
            .Build();
        var env = new StubHostEnv(environmentName);
        return new InternalTokenMiddleware(
            next: _ => Task.CompletedTask,
            cfg: cfg,
            env: env,
            logger: NullLogger<InternalTokenMiddleware>.Instance);
    }

    [Fact]
    public async Task LengthMismatch_DoesNotThrow_Returns401()
    {
        // Token is 32 chars; client presents a 5-char token. The naive
        // FixedTimeEquals path would throw on length mismatch; we
        // length-check first so the middleware just emits 401.
        var middleware = Build(new string('a', 32), Environments.Development);
        var ctx = MakeRequest(path: "/internal/whatever", token: "short");

        await middleware.InvokeAsync(ctx);

        ctx.Response.StatusCode.Should().Be(StatusCodes.Status401Unauthorized);
    }

    [Fact]
    public async Task ValidToken_PassesThrough()
    {
        var middleware = Build("the-secret", Environments.Development);
        var ctx = MakeRequest(path: "/internal/whatever", token: "the-secret");

        await middleware.InvokeAsync(ctx);

        ctx.Response.StatusCode.Should().Be(StatusCodes.Status200OK);
    }

    [Fact]
    public async Task WrongToken_SameLength_Returns401()
    {
        // Same length → goes through FixedTimeEquals → mismatch → 401.
        var middleware = Build("the-secret", Environments.Development);
        var ctx = MakeRequest(path: "/internal/whatever", token: "not-secret");

        await middleware.InvokeAsync(ctx);

        ctx.Response.StatusCode.Should().Be(StatusCodes.Status401Unauthorized);
    }

    [Fact]
    public async Task MissingHeader_Returns401()
    {
        var middleware = Build("the-secret", Environments.Development);
        var ctx = MakeRequest(path: "/internal/whatever", token: null);

        await middleware.InvokeAsync(ctx);

        ctx.Response.StatusCode.Should().Be(StatusCodes.Status401Unauthorized);
    }

    [Fact]
    public async Task NonInternalPath_AlwaysPassesThrough()
    {
        var middleware = Build("the-secret", Environments.Development);
        var ctx = MakeRequest(path: "/healthz", token: null);

        await middleware.InvokeAsync(ctx);

        ctx.Response.StatusCode.Should().Be(StatusCodes.Status200OK);
    }

    [Fact]
    public void Production_WithoutToken_ThrowsAtConstruction()
    {
        // Fail-fast: refuse to start in Production without a token.
        var act = () => Build(token: null, Environments.Production);
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Cards:InternalToken must be set in Production*");
    }

    [Fact]
    public async Task Development_WithoutToken_AllowsAll()
    {
        // Dev mode: empty token configured → allow-all (used for local
        // tests + bootstrap before the secret is provisioned).
        var middleware = Build(token: null, Environments.Development);
        var ctx = MakeRequest(path: "/internal/whatever", token: null);

        await middleware.InvokeAsync(ctx);

        ctx.Response.StatusCode.Should().Be(StatusCodes.Status200OK);
    }

    private static DefaultHttpContext MakeRequest(string path, string? token)
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.Path = path;
        if (token != null)
            ctx.Request.Headers[InternalTokenMiddleware.HeaderName] = token;
        return ctx;
    }

    private sealed class StubHostEnv : IHostEnvironment
    {
        public StubHostEnv(string envName) => EnvironmentName = envName;
        public string EnvironmentName { get; set; }
        public string ApplicationName { get; set; } = "test";
        public string ContentRootPath { get; set; } = "";
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = null!;
    }
}
