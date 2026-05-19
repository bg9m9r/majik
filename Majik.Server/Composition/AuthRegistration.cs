using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

namespace Majik.Server.Composition;

/// <summary>
/// Wires OIDC bearer-token auth.
///
/// IdP-agnostic by design: the server validates JWTs against whatever
/// authority is configured in `Auth:Authority` (Auth0, Cognito, Keycloak,
/// Azure AD, etc.). Token discovery is the standard OIDC dance —
/// metadata lives at `{authority}/.well-known/openid-configuration` and
/// the handler caches the signing keys.
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
                    ValidateAudience = !string.IsNullOrWhiteSpace(audience),
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    NameClaimType = "sub",
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
}
