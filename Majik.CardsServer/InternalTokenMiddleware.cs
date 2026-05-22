using System.Security.Cryptography;
using System.Text;

namespace Majik.CardsServer;

/// <summary>
/// Gates /internal/* endpoints behind a shared-secret header. Defense-in-depth
/// on top of Render private-service network isolation. Configure via
/// <c>Cards:InternalToken</c> (env: <c>Cards__InternalToken</c>); leaving the
/// value empty in a non-Production environment allows requests through (dev
/// mode). In Production the empty-token case is fatal — see the constructor.
/// </summary>
public sealed class InternalTokenMiddleware
{
    public const string HeaderName = "X-Internal-Token";

    private readonly RequestDelegate _next;
    private readonly byte[]? _expectedBytes;
    private readonly ILogger<InternalTokenMiddleware> _logger;

    public InternalTokenMiddleware(
        RequestDelegate next,
        IConfiguration cfg,
        IHostEnvironment env,
        ILogger<InternalTokenMiddleware> logger)
    {
        _next = next;
        _logger = logger;
        var expected = cfg["Cards:InternalToken"];
        if (string.IsNullOrEmpty(expected))
        {
            if (env.IsProduction())
            {
                // Fail-fast: silently allow-all in production was the worst
                // possible default. Force the deploy to set Cards:InternalToken
                // (or explicitly switch the env to non-Production for a
                // dev build).
                throw new InvalidOperationException(
                    "Cards:InternalToken must be set in Production. Refusing to start in allow-all mode.");
            }
            _expectedBytes = null; // dev/test allow-all
        }
        else
        {
            _expectedBytes = Encoding.UTF8.GetBytes(expected);
        }
    }

    public async Task InvokeAsync(HttpContext ctx)
    {
        if (!ctx.Request.Path.StartsWithSegments("/internal"))
        {
            await _next(ctx);
            return;
        }
        if (_expectedBytes == null)
        {
            // No token configured — allow (non-prod dev mode). Logged once per
            // process at startup by Program.cs; not logged per request.
            await _next(ctx);
            return;
        }
        if (!ctx.Request.Headers.TryGetValue(HeaderName, out var got))
        {
            _logger.LogWarning("Rejected /internal request without {Header}", HeaderName);
            ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }
        // Constant-time compare. Length check first so we never feed
        // mismatched lengths to FixedTimeEquals (which throws). The length
        // is non-secret so leaking timing on length alone is acceptable.
        var gotBytes = Encoding.UTF8.GetBytes(got.ToString());
        if (gotBytes.Length != _expectedBytes.Length
            || !CryptographicOperations.FixedTimeEquals(gotBytes, _expectedBytes))
        {
            _logger.LogWarning("Rejected /internal request with invalid {Header}", HeaderName);
            ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }
        await _next(ctx);
    }
}
