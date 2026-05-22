namespace Majik.CardsServer;

/// <summary>
/// Gates /internal/* endpoints behind a shared-secret header. Defense-in-depth
/// on top of Render private-service network isolation. Configure via
/// <c>Cards:InternalToken</c> (env: <c>Cards__InternalToken</c>); leaving the
/// value empty disables the check (intended for local dev — never deploy
/// without setting the token in production).
/// </summary>
public sealed class InternalTokenMiddleware
{
    public const string HeaderName = "X-Internal-Token";

    private readonly RequestDelegate _next;
    private readonly string? _expected;
    private readonly ILogger<InternalTokenMiddleware> _logger;

    public InternalTokenMiddleware(RequestDelegate next, IConfiguration cfg, ILogger<InternalTokenMiddleware> logger)
    {
        _next = next;
        _expected = cfg["Cards:InternalToken"];
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext ctx)
    {
        if (!ctx.Request.Path.StartsWithSegments("/internal"))
        {
            await _next(ctx);
            return;
        }
        if (string.IsNullOrEmpty(_expected))
        {
            // No token configured — allow (dev mode). Logged once per process at startup
            // by Program.cs; not logged per request to avoid noise.
            await _next(ctx);
            return;
        }
        if (!ctx.Request.Headers.TryGetValue(HeaderName, out var got) || got != _expected)
        {
            _logger.LogWarning("Rejected /internal request without valid {Header}", HeaderName);
            ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }
        await _next(ctx);
    }
}
