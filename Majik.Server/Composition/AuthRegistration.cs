using Majik.Server.Auth;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;

namespace Majik.Server.Composition;

/// <summary>
/// Wires OIDC bearer-token auth.
///
/// Production path: validate JWTs against the configured Descope project
/// (Auth:Authority = "https://api.descope.com/&lt;PROJECT_ID&gt;") with the
/// project ID as audience (Auth:Audience). On top of the standard
/// signature/issuer/audience/lifetime checks, <see cref="DescopeTokenValidator"/>
/// enforces a non-empty `sub` and lifts a `discordUserId` claim from
/// either a direct claim or Descope's nested `customAttributes` JSON.
///
/// Token discovery is the standard OIDC dance — metadata lives at
/// `{authority}/.well-known/openid-configuration` and the handler
/// caches the signing keys.
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

        var authBuilder = services.AddAuthentication(options =>
        {
            options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
            options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
        });

        if (enabled)
        {
            authBuilder.AddJwtBearer(JwtBearerDefaults.AuthenticationScheme, options =>
            {
                options.Authority = authority;
                options.Audience = audience;
                options.RequireHttpsMetadata = true;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    // Descope stamps different `iss` claims depending on
                    // which auth surface mints the token:
                    //   - direct flow:    https://api.descope.com/<PID>
                    //   - OIDC app flow:  https://api.descope.com/v1/apps/<PID>
                    // Both are signed by the same JWKS at the project root,
                    // so we keep `Authority` pointed there for key discovery
                    // and accept either issuer here.
                    ValidIssuers = BuildValidIssuers(authority!),
                    ValidateAudience = !string.IsNullOrWhiteSpace(audience),
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    NameClaimType = "sub",
                };
                options.Events = new JwtBearerEvents
                {
                    OnTokenValidated = DescopeTokenValidator.ValidateAsync,
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
        else
        {
            // Register the scheme with no-op handler so [Authorize]
            // endpoints return 401 instead of throwing
            // "No authenticationScheme was specified".
            authBuilder.AddJwtBearer(JwtBearerDefaults.AuthenticationScheme, _ => { });
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

    private static string[] BuildValidIssuers(string authority)
    {
        var trimmed = authority.TrimEnd('/');
        var lastSlash = trimmed.LastIndexOf('/');
        if (lastSlash <= "https://".Length)
        {
            return new[] { trimmed };
        }

        var baseUrl = trimmed[..lastSlash];
        var projectId = trimmed[(lastSlash + 1)..];
        return new[]
        {
            trimmed,
            $"{baseUrl}/v1/apps/{projectId}",
        };
    }
}
