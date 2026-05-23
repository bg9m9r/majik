using Majik.Server.Auth;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;

namespace Majik.Server.Composition;

/// <summary>
/// Wires OIDC bearer-token auth.
///
/// Production path: validate JWTs against the configured Auth0 tenant
/// (Auth:Authority = "https://auth.majik.tech/" — trailing slash matters,
/// Auth0 stamps `iss` with the slash) with the API identifier as audience
/// (Auth:Audience = "https://api.majik.tech"). On top of the standard
/// signature/issuer/audience/lifetime checks, <see cref="Auth0TokenValidator"/>
/// enforces a non-empty `sub` and lifts the Discord user ID from the
/// namespaced custom claim Auth0 injects via the "Lift Discord user ID"
/// post-login Action.
///
/// Token discovery is the standard OIDC dance — metadata lives at
/// `{authority}.well-known/openid-configuration` and the handler caches
/// the signing keys.
///
/// When `Auth:Authority` is missing or empty, auth is disabled — useful
/// for local development before an IdP is provisioned. In that mode
/// `AsPlayer`/`RequireAuthenticatedUser` policies trivially fail, so
/// guarded endpoints stay unreachable; the unauth flow surfaces 401
/// instead of crashing on a missing config.
/// </summary>
public static class AuthRegistration
{
    /// <summary>Policy name applied to any endpoint that requires the
    /// caller to be acting as a known player.</summary>
    public const string AsPlayerPolicy = "AsPlayer";

    public static IServiceCollection AddMajikAuth(
        this IServiceCollection services,
        IConfiguration config)
    {
        var authority = config["Auth:Authority"];
        var audience = config["Auth:Audience"];
        var enabled = !string.IsNullOrWhiteSpace(authority);

        // When auth is disabled (Auth:Authority empty), do NOT register a
        // default scheme. The previous "register no-op JwtBearer" path
        // accepted requests with no token at all on some pipelines because
        // the default handler returned NoResult and the policy short-circuited
        // to 401 only sometimes. With no default scheme registered,
        // [Authorize] consistently emits 401 — the behavior production
        // wants when Auth0 hasn't been provisioned yet.
        if (!enabled)
        {
            services.AddAuthentication();
            services.AddAuthorization(options =>
            {
                options.AddPolicy(AsPlayerPolicy, policy =>
                {
                    policy.RequireAuthenticatedUser();
                    policy.RequireClaim("sub");
                });
            });
            return services;
        }

        var authBuilder = services.AddAuthentication(options =>
        {
            options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
            options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
        });

        {
            authBuilder.AddJwtBearer(JwtBearerDefaults.AuthenticationScheme, options =>
            {
                options.Authority = authority;
                options.Audience = audience;
                options.RequireHttpsMetadata = true;
                // ASP.NET defaults to rewriting JWT claim names into legacy
                // WS-Federation / SOAP XML schema URIs (`sub` →
                // `http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier`,
                // `email` → `…/emailaddress`, etc.). Auth0TokenValidator
                // looks up the raw `sub` and the AsPlayer policy requires
                // a literal "sub" claim — both fail silently when mapping
                // is on, producing 401s with "sub missing" warnings even
                // though the JWT itself has a perfectly good sub.
                options.MapInboundClaims = false;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    // Auth0 stamps `iss` with the configured Authority
                    // verbatim (custom domain + trailing slash). One issuer,
                    // unlike Descope's dual direct-flow / OIDC-app-flow
                    // issuers, so no allowlist needed.
                    ValidIssuer = authority,
                    ValidateAudience = !string.IsNullOrWhiteSpace(audience),
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    NameClaimType = "sub",
                    // Default ClockSkew is 5 minutes — far too generous for
                    // short-lived JWTs. Tighten so expired tokens are
                    // rejected within the 30s NTP drift budget.
                    ClockSkew = TimeSpan.FromSeconds(30),
                };
                options.Events = new JwtBearerEvents
                {
                    OnTokenValidated = Auth0TokenValidator.ValidateAsync,
                    // SignalR WebSocket connections cannot send arbitrary
                    // headers, so the JS client passes the JWT via the
                    // `access_token` query string parameter during the
                    // negotiate + WebSocket upgrade for any URL it considers
                    // a hub. ASP.NET's JwtBearer middleware only reads the
                    // Authorization header by default, so hub negotiate
                    // returned 401 even with a valid token. Lift the query
                    // token into `context.Token` for `/hubs/*` requests so
                    // the rest of the JwtBearer pipeline (signature, issuer,
                    // audience, Auth0TokenValidator) runs as normal.
                    // See: https://learn.microsoft.com/aspnet/core/signalr/authn-and-authz#bearer-token-authentication
                    OnMessageReceived = ctx =>
                    {
                        var accessToken = ctx.Request.Query["access_token"];
                        var path = ctx.HttpContext.Request.Path;
                        if (!string.IsNullOrEmpty(accessToken)
                            && path.StartsWithSegments("/hubs"))
                        {
                            ctx.Token = accessToken;
                        }
                        return Task.CompletedTask;
                    },
                    // Default log level masks JwtBearer auth events. Surface
                    // failures + challenges at Warning so they're visible in
                    // production hosting (Render etc) without flipping the
                    // global Microsoft.AspNetCore level.
                    OnAuthenticationFailed = ctx =>
                    {
                        var logger = ctx.HttpContext.RequestServices
                            .GetRequiredService<ILoggerFactory>()
                            .CreateLogger("Majik.Auth");
                        logger.LogWarning(ctx.Exception,
                            "JwtBearer authentication failed: {Message}",
                            ctx.Exception.Message);
                        return Task.CompletedTask;
                    },
                    OnChallenge = ctx =>
                    {
                        if (!string.IsNullOrEmpty(ctx.Error) ||
                            !string.IsNullOrEmpty(ctx.ErrorDescription))
                        {
                            var logger = ctx.HttpContext.RequestServices
                                .GetRequiredService<ILoggerFactory>()
                                .CreateLogger("Majik.Auth");
                            logger.LogWarning(
                                "JwtBearer challenge: error={Error} description={Description}",
                                ctx.Error, ctx.ErrorDescription);
                        }
                        return Task.CompletedTask;
                    },
                };
            });
        }

        services.AddAuthorization(options =>
        {
            options.AddPolicy(AsPlayerPolicy, policy =>
            {
                policy.RequireAuthenticatedUser();
                policy.RequireClaim("sub");
            });
        });

        return services;
    }
}
