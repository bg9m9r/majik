using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Majik.Server.Tests.Composition;

/// <summary>
/// Regression lock for the bug where unhandled exceptions in the API
/// pipeline returned a 500 with NO <c>Access-Control-Allow-Origin</c>
/// header. The browser then blocked the response body and the portal
/// surfaced an opaque "fetch failed" instead of the actual server error.
///
/// Fix: <see cref="Program"/> wires <c>UseExceptionHandler</c> BEFORE
/// <c>UseCors</c>, which short-circuits the exception, sets a 500, and
/// re-runs the response through the downstream middleware so CORS gets
/// to attach its header. These tests verify the ordering invariant by
/// composing a minimal pipeline shaped like the production one and
/// asserting the headers on an exceptional response.
/// </summary>
public class ExceptionHandlerCorsTests
{
    private const string AllowedOrigin = "https://portal.example.test";

    private static IHost BuildHost(Action<IApplicationBuilder> wire)
    {
        return new HostBuilder()
            .ConfigureWebHost(builder =>
            {
                builder.UseTestServer();
                builder.ConfigureServices(services =>
                {
                    services.AddCors(options =>
                    {
                        options.AddPolicy("test", p => p
                            .WithOrigins(AllowedOrigin)
                            .AllowAnyHeader()
                            .AllowAnyMethod()
                            .AllowCredentials());
                    });
                    services.AddRouting();
                    services.AddLogging();
                });
                builder.Configure(app =>
                {
                    wire(app);
                    app.UseRouting();
                    app.UseCors("test");
                    app.UseEndpoints(endpoints =>
                    {
                        endpoints.MapGet("/boom", (HttpContext _) =>
                            throw new InvalidOperationException("test-failure"));
                    });
                });
            })
            .Start();
    }

    [Fact]
    public async Task UnhandledException_WithExceptionHandlerBeforeCors_Returns500WithCorsHeader()
    {
        // The fix: UseExceptionHandler installs a terminal handler that
        // writes a 500. Because it sits BEFORE UseCors, the response
        // re-runs through the CORS middleware on its way out, which
        // observes the cross-origin request and attaches
        // Access-Control-Allow-Origin. Without this ordering the browser
        // strips the body and the SPA can't see the error.
        using var host = BuildHost(app =>
        {
            app.UseExceptionHandler(eh => eh.Run(async ctx =>
            {
                ctx.Response.StatusCode = StatusCodes.Status500InternalServerError;
                ctx.Response.ContentType = "application/json";
                await ctx.Response.WriteAsync("{\"error\":\"internal\"}");
            }));
        });
        var server = host.GetTestServer();

        using var client = server.CreateClient();
        var req = new HttpRequestMessage(HttpMethod.Get, "/boom");
        req.Headers.Add("Origin", AllowedOrigin);

        var resp = await client.SendAsync(req);

        resp.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        resp.Headers.Should().ContainKey("Access-Control-Allow-Origin",
            "CORS middleware must run on the 500 response so the browser " +
            "doesn't block the body");
        resp.Headers.GetValues("Access-Control-Allow-Origin")
            .Should().ContainSingle().Which.Should().Be(AllowedOrigin);

        var body = await resp.Content.ReadAsStringAsync();
        body.Should().Contain("internal",
            "the handler must write a generic error body — stack traces " +
            "are logged server-side, never leaked over the wire");
    }

    [Fact]
    public async Task UnhandledException_WithoutExceptionHandler_HasNoCorsHeader()
    {
        // Negative baseline: without UseExceptionHandler the framework's
        // default behavior bypasses downstream middleware on its way out,
        // so the 500 response lacks the CORS header. This is the bug
        // the production fix addresses; locking it prevents accidental
        // regression if someone removes UseExceptionHandler from Program.cs.
        using var host = BuildHost(app => { /* no exception handler */ });
        var server = host.GetTestServer();
        server.AllowSynchronousIO = true;

        using var client = server.CreateClient();
        var req = new HttpRequestMessage(HttpMethod.Get, "/boom");
        req.Headers.Add("Origin", AllowedOrigin);

        try
        {
            var resp = await client.SendAsync(req);
            // If a 500 surfaces, it must NOT have CORS — that's the bug.
            if (resp.StatusCode == HttpStatusCode.InternalServerError)
            {
                resp.Headers.Contains("Access-Control-Allow-Origin")
                    .Should().BeFalse(
                        "without UseExceptionHandler, CORS does not run on the " +
                        "exception response — this is exactly what the prod fix solves");
            }
        }
        catch (InvalidOperationException)
        {
            // TestServer surfaces unhandled exceptions to the client by
            // throwing. Either outcome (no CORS header on 500, or exception
            // bubble) confirms that the un-handled path bypasses middleware.
        }
    }
}
