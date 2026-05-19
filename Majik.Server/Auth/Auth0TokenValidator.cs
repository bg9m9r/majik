using Microsoft.AspNetCore.Authentication.JwtBearer;

namespace Majik.Server.Auth;

/// <summary>
/// Auth0-specific validation hardening on top of the standard JwtBearer
/// pipeline. Runs after the signature + issuer + audience + lifetime
/// checks succeed.
///
/// Enforces two things the generic OIDC handler doesn't know about:
/// 1. The `sub` claim must follow the Auth0 social-connection format
///    `oauth2|{provider}|{external-id}`. Tokens minted by an admin
///    connection or the username-password DB connection lack this prefix
///    and are rejected — Majik's seating model assumes every principal
///    is a stable external user.
/// 2. A convenience `discordUserId` claim is added when the provider is
///    `discord`, lifting the raw snowflake out of the sub so downstream
///    code (audit logs, future friends list) doesn't have to re-parse.
///
/// Disabled when `Auth:Authority` is unset — the handler is not wired
/// in that mode (see <see cref="AuthRegistration"/>).
/// </summary>
public static class Auth0TokenValidator
{
    public const string DiscordUserIdClaim = "discordUserId";

    private const string SubPrefix = "oauth2|";
    private const string DiscordProvider = "discord";

    public static Task ValidateAsync(TokenValidatedContext context)
    {
        var sub = context.Principal?.FindFirst("sub")?.Value;

        if (string.IsNullOrEmpty(sub))
        {
            context.Fail("Token missing 'sub' claim.");
            return Task.CompletedTask;
        }

        if (!sub.StartsWith(SubPrefix, StringComparison.Ordinal))
        {
            context.Fail(
                $"Token 'sub' must be an Auth0 social-connection identity (expected prefix '{SubPrefix}'), got '{sub}'.");
            return Task.CompletedTask;
        }

        // sub layout: oauth2|{provider}|{external-id}
        var parts = sub.Split('|', 3);
        if (parts.Length != 3 || parts[1].Length == 0 || parts[2].Length == 0)
        {
            context.Fail($"Token 'sub' is not a valid Auth0 social identity: '{sub}'.");
            return Task.CompletedTask;
        }

        var provider = parts[1];
        var externalId = parts[2];

        if (string.Equals(provider, DiscordProvider, StringComparison.OrdinalIgnoreCase)
            && context.Principal!.Identity is System.Security.Claims.ClaimsIdentity identity)
        {
            // Idempotent — re-running validation on the same principal
            // shouldn't double-stamp the claim.
            if (!identity.HasClaim(c => c.Type == DiscordUserIdClaim))
            {
                identity.AddClaim(new System.Security.Claims.Claim(DiscordUserIdClaim, externalId));
            }
        }

        return Task.CompletedTask;
    }
}
