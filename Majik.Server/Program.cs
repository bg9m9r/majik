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
builder.Services.AddMajikMatches(builder.Configuration);
builder.Services.AddMajikDecks(builder.Configuration);
builder.Services.AddMajikSignalR(builder.Configuration);
// SignalR Clients.User(...) keys on the "sub" claim. See
// SubUserIdProvider for rationale; needed for the hub publisher's
// per-recipient fan-out which carries CR 706 hidden info.
builder.Services.AddSingleton<Microsoft.AspNetCore.SignalR.IUserIdProvider, Majik.Server.Matches.SubUserIdProvider>();

// In-app issue reporting (Slice 1). Bound + registered as singletons,
// mirroring the ServerBotOptions GetSection().Get<>() pattern in
// MajikEngineRegistration. Env binding: Reporting__TrustedTesterSubs__0,
// Reporting__MaxReportsPerHour, GitHub__Token, GitHub__RepositoryOwner,
// GitHub__RepositoryName, GitHub__IssueLabel.
var reportingOptions = builder.Configuration.GetSection(ReportingOptions.SectionName)
    .Get<ReportingOptions>() ?? new ReportingOptions();
builder.Services.AddSingleton(reportingOptions);

var gitHubOptions = builder.Configuration.GetSection(GitHubOptions.SectionName)
    .Get<GitHubOptions>() ?? new GitHubOptions();
builder.Services.AddSingleton(gitHubOptions);

// Typed HttpClient for opening report issues. Injects HttpClient + the
// singleton GitHubOptions registered above.
builder.Services.AddHttpClient<IGitHubIssueClient, GitHubIssueClient>();

// Slice 3 — user-keyed notifications. Singleton publisher resolves the hub
// context to push "report-delivered" to a reporter's sub (routing via
// SubUserIdProvider registered above).
builder.Services.AddSingleton<INotificationsPublisher, NotificationsPublisher>();

// Slice 3 — deployment-watcher config + portal version probe. The probe is a
// typed HttpClient (polls the portal's version.json); options are bound like
// ReportingOptions/GitHubOptions. The hosted DeploymentWatcher itself is
// registered in AddMajikMatches (Mongo-gated — it needs MatchReportRepository).
var deploymentOptions = builder.Configuration.GetSection(DeploymentOptions.SectionName)
    .Get<DeploymentOptions>() ?? new DeploymentOptions();
builder.Services.AddSingleton(deploymentOptions);
builder.Services.AddHttpClient<IPortalVersionProbe, HttpPortalVersionProbe>();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddOpenApi();

// Per-user (fall back to per-IP) rate-limit — two named policies so the
// tight bucket protects abuse-prone routes while per-priority-window
// in-match traffic gets 10× headroom.
//
//  "expensive"  — 60 req/min — deck CRUD, match create/join, card search,
//                 profile mutations. Protects against trivial DoS and
//                 credential-stuffing on write-heavy endpoints.
//  "in-match"   — 600 req/min — commands, state polls, roll, play-draw,
//                 concede, replay.  Called every priority window during a
//                 game; the tight cap would 429 legit gameplay at ~3 turns.
//
// Health, whoami, OpenAPI, and the SignalR negotiate are NOT rate-limited.
// Both policies partition per-sub (JWT "sub" claim, IP fallback).
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    static string PartitionKey(Microsoft.AspNetCore.Http.HttpContext ctx) =>
        ctx.User?.FindFirst("sub")?.Value
        ?? ctx.Connection.RemoteIpAddress?.ToString()
        ?? "anonymous";

    // "expensive" — 60 req/min per sub
    options.AddPolicy(RateLimitPolicies.Expensive, ctx =>
        RateLimitPartition.GetFixedWindowLimiter(PartitionKey(ctx), _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 60,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            AutoReplenishment = true,
        }));

    // "in-match" — 600 req/min per sub (10× headroom for gameplay traffic)
    options.AddPolicy(RateLimitPolicies.InMatch, ctx =>
        RateLimitPartition.GetFixedWindowLimiter(
            "in-match:" + PartitionKey(ctx), _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 600,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                AutoReplenishment = true,
            }));
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

// Slice 3 — GitHub webhook. Anonymous (GitHub can't carry our JWT); the raw
// body is HMAC-verified against GitHub__WebhookSecret before any work runs.
// 503 when no secret is configured (degrade gracefully, never crash).
app.MapPost("/webhooks/github", async (HttpRequest req, GitHubOptions gh, CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(gh.WebhookSecret))
        return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);

    // GitHubWebhookService is Mongo-gated (it drives MatchReportRepository).
    // Resolve lazily so the host still builds when Mongo is unconfigured.
    var svc = req.HttpContext.RequestServices.GetService<GitHubWebhookService>();
    if (svc is null)
        return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);

    req.EnableBuffering();
    using var reader = new StreamReader(req.Body, leaveOpen: true);
    var body = await reader.ReadToEndAsync(ct);
    req.Body.Position = 0;

    var sig = req.Headers["X-Hub-Signature-256"].FirstOrDefault();
    if (!GitHubWebhookVerifier.IsValid(body, sig, gh.WebhookSecret))
        return Results.Unauthorized();

    var eventType = req.Headers["X-GitHub-Event"].FirstOrDefault();
    if (eventType == "pull_request")
    {
        var coreRepo = $"{gh.RepositoryOwner}/{gh.RepositoryName}";
        await svc.HandlePullRequestAsync(body, coreRepo, ct);
    }
    return Results.Ok();
})
.AllowAnonymous()
.WithName("GitHubWebhook")
.WithTags("Meta");

// Slice 3 — build identity. Reporters/portal compare commitSha + buildTime to
// detect a redeploy carrying a fix. RENDER_GIT_COMMIT is injected by Render at
// runtime; buildTime is this process's start time (captured once at boot).
var bootTimeIso = DateTime.UtcNow.ToString("o");
app.MapGet("/version", () => Results.Ok(new
{
    commitSha = Environment.GetEnvironmentVariable("RENDER_GIT_COMMIT") ?? "dev",
    buildTime = bootTimeIso,
}))
.AllowAnonymous()
.WithName("GetVersion")
.WithTags("Meta")
.Produces(StatusCodes.Status200OK);

app.MapProfileEndpoints();
app.MapCardsEndpoints();
app.MapMatchEndpoints();
app.MapDeckEndpoints();
app.MapHub<MatchHub>("/hubs/match");
app.MapHub<NotificationsHub>("/hubs/notifications");

app.Run();

public partial class Program { }
