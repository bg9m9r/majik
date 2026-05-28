using System.Threading.RateLimiting;
using Majik.Server.Cards;
using Majik.Server.Composition;
using Majik.Server.Decks;
using Majik.Server.Matches;
using Majik.Server.Profiles;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

// Request body size cap (DoS hardening). Every endpoint on this server takes
// a tiny body: game commands are well under 1 KB and the largest legitimate
// POST is a deck create carrying a full decklist (a few hundred short card
// names) — comfortably under 256 KB. The OpenAPI/whoami/health paths are GETs
// with no body. Capping globally turns an oversized body into a clean 413
// (BadHttpRequestException → Kestrel's 413) instead of letting a multi-MB
// payload stream into memory and surface as a 500. Kestrel's default is
// 30 MB; 256 KB is ~120x headroom over our largest real payload.
const long MaxRequestBodyBytes = 256 * 1024;
builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MaxRequestBodySize = MaxRequestBodyBytes;
});

builder.Services.AddMajikEngine(builder.Configuration);
builder.Services.AddMajikAuth(builder.Configuration);
builder.Services.AddMajikCors(builder.Configuration);
builder.Services.AddMajikMongo(builder.Configuration);
builder.Services.AddMajikRedis(builder.Configuration);
builder.Services.AddMajikMatches(builder.Configuration);
builder.Services.AddMajikDecks(builder.Configuration);
builder.Services.AddMajikSignalR(builder.Configuration);
// SignalR Clients.User(...) keys on the "sub" claim. See
// SubUserIdProvider for rationale; needed for the hub publisher's
// per-recipient fan-out which carries CR 706 hidden info.
builder.Services.AddSingleton<Microsoft.AspNetCore.SignalR.IUserIdProvider, Majik.Server.Matches.SubUserIdProvider>();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddOpenApi();

// Per-user (fall back to per-IP) rate limit. 60 req/min on the expensive
// or write-heavy endpoints: deck CRUD, match creation/join, card search.
// Health, whoami, OpenAPI, and the SignalR negotiate are unpartitioned
// (no policy attached on their endpoints).
const string AuthedRateLimitPolicy = "authed-60-per-min";
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy(AuthedRateLimitPolicy, httpContext =>
    {
        var key = httpContext.User?.FindFirst("sub")?.Value
            ?? httpContext.Connection.RemoteIpAddress?.ToString()
            ?? "anonymous";
        return RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 60,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            AutoReplenishment = true,
        });
    });
});

var app = builder.Build();

// Forwarded headers FIRST so downstream auth/log/limiter see the real
// client IP and scheme behind Render's load balancer. KnownNetworks/
// KnownProxies cleared because Render's proxy IPs aren't fixed — accept
// the headers from any hop. (Acceptable for our threat model: the
// rate-limit key prefers sub claim and only falls back to IP for
// unauthed callers.)
var forwardedOptions = new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
};
forwardedOptions.KnownIPNetworks.Clear();
forwardedOptions.KnownProxies.Clear();
app.UseForwardedHeaders(forwardedOptions);

// Global exception handler — sits BEFORE CORS so the 500 response is
// re-run through the downstream middleware chain, ensuring the
// Access-Control-Allow-Origin header is attached. Without this the
// browser blocks the body of any unhandled-exception response and the
// SPA surfaces an opaque "fetch failed" instead of a meaningful 500.
// Stack traces are logged server-side, never leaked in the body.
app.UseExceptionHandler(eh => eh.Run(async ctx =>
{
    var feature = ctx.Features.Get<Microsoft.AspNetCore.Diagnostics.IExceptionHandlerFeature>();
    string? exceptionType = null;
    if (feature?.Error != null)
    {
        exceptionType = feature.Error.GetType().Name;
        var logger = ctx.RequestServices.GetRequiredService<ILoggerFactory>()
            .CreateLogger("Majik.Server.UnhandledException");
        logger.LogError(
            feature.Error,
            "Unhandled exception on {Method} {Path} (traceId={TraceId})",
            ctx.Request.Method, ctx.Request.Path, ctx.TraceIdentifier);
    }
    ctx.Response.StatusCode = StatusCodes.Status500InternalServerError;
    ctx.Response.ContentType = "application/json";
    // Body includes the request trace id (for cross-referencing server logs)
    // and the exception TYPE NAME (not message — message can leak secrets).
    // Full message + stack stays in the log only.
    var body = System.Text.Json.JsonSerializer.Serialize(new
    {
        error = "internal",
        traceId = ctx.TraceIdentifier,
        type = exceptionType,
    });
    await ctx.Response.WriteAsync(body);
}));

// OpenAPI is always exposed — the portal's CI build fetches /openapi/v1.json
// from the deployed API to regenerate its typed client during `ng build`.
// This is an open-source MTG rules engine; the schema is not sensitive.
app.MapOpenApi();

if (CorsRegistration.HasAnyOrigins(builder.Configuration))
{
    app.UseCors(CorsRegistration.PolicyName);
}

app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

app.MapGet("/healthz", () => Results.Ok(new { status = "ok" }))
   .WithName("HealthCheck")
   .WithTags("Meta")
   .AllowAnonymous()
   .Produces(StatusCodes.Status200OK);

app.MapGet("/whoami", (System.Security.Claims.ClaimsPrincipal user) =>
    Results.Ok(new
    {
        sub = user.FindFirst("sub")?.Value,
        name = user.Identity?.Name,
        authenticated = user.Identity?.IsAuthenticated ?? false,
    }))
   .WithName("WhoAmI")
   .WithTags("Meta")
   .RequireAuthorization(AuthRegistration.AsPlayerPolicy)
   .Produces(StatusCodes.Status200OK)
   .Produces(StatusCodes.Status401Unauthorized);

app.MapProfileEndpoints();
app.MapCardsEndpoints();
app.MapMatchEndpoints();
app.MapDeckEndpoints();
app.MapHub<MatchHub>("/hubs/match");

app.Run();

public partial class Program { }
