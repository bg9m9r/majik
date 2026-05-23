using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Majik.Server.Auth;

/// <summary>
/// Auth0-specific validation hardening on top of the standard JwtBearer
/// pipeline. Runs after the signature + issuer + audience + lifetime
/// checks succeed.
///
/// Auth0 `sub` values look like <c>oauth2|discord|{discord-user-id}</c>
/// for users coming through the Discord social connection. We don't
/// enforce that shape — `sub` is opaque to the application — we just
/// require it be present and non-empty.
///
/// To get a Discord user ID into the principal, the Auth0 tenant must
/// have a post-login Action that copies <c>identities[0].user_id</c>
/// (when provider is "discord") into a namespaced custom claim on the
/// access token. The claim name is <c>https://majik.tech/discord_user_id</c>
/// — Auth0 silently strips non-namespaced custom claims. When present
/// we lift it onto the principal under <see cref="DiscordUserIdClaim"/>
/// so downstream code (audit log, future friends list) reads it without
/// inspecting the namespaced URI.
///
/// Disabled when `Auth:Authority` is unset — the handler is not wired
/// in that mode (see <see cref="Composition.AuthRegistration"/>).
/// </summary>
public static class Auth0TokenValidator
{
    public const string DiscordUserIdClaim = "discordUserId";

    /// <summary>
    /// Namespaced custom-claim URI emitted by the "Lift Discord user ID"
    /// post-login Action in the Auth0 tenant. Must be a URL — Auth0 drops
    /// custom claims that aren't namespaced with an http(s)://.
    /// </summary>
    private const string NamespacedDiscordClaim = "https://majik.tech/discord_user_id";

    public static Task ValidateAsync(TokenValidatedContext context)
    {
        var logger = context.HttpContext.RequestServices
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger("Majik.Auth");

        var identity = context.Principal?.Identity as ClaimsIdentity;
        // Strict: require the "sub" claim. Previously fell back to
        // ClaimTypes.NameIdentifier, which can be populated by ASP.NET's
        // claim-mapping pass even when the JWT itself has no sub —
        // an attacker who can mint a token without sub shouldn't have
        // their NameIdentifier silently promoted.
        var sub = context.Principal?.FindFirst("sub")?.Value;

        if (string.IsNullOrEmpty(sub))
        {
            // Log only claim TYPES (never values) — claim values may carry
            // PII (email, Discord ID) that has no business in operational
            // logs. Type list is sufficient to debug a misconfigured IdP.
            var claimTypes = context.Principal is null
                ? "<no principal>"
                : string.Join(", ",
                    context.Principal.Claims.Select(c => c.Type).Distinct());
            logger.LogWarning(
                "Auth0TokenValidator: sub missing. Claim types: {ClaimTypes}",
                claimTypes);
            context.Fail("Token missing 'sub' claim.");
            return Task.CompletedTask;
        }

        if (identity is null)
        {
            return Task.CompletedTask;
        }

        // If `sub` came in under a mapped claim type (older JwtSecurityTokenHandler
        // remaps it to ClaimTypes.NameIdentifier), add it back as "sub" so the
        // AsPlayer policy's RequireClaim("sub") finds it.
        if (!identity.HasClaim(c => c.Type == "sub"))
        {
            identity.AddClaim(new Claim("sub", sub));
        }

        var discordId = context.Principal!.FindFirst(NamespacedDiscordClaim)?.Value;
        if (!string.IsNullOrEmpty(discordId) && !identity.HasClaim(c => c.Type == DiscordUserIdClaim))
        {
            identity.AddClaim(new Claim(DiscordUserIdClaim, discordId));
        }

        // Demoted from Information to Debug + dropped the discordUserId
        // value entirely (PII). The Discord ID is still lifted onto the
        // principal above; we just don't log it on every request.
        logger.LogDebug(
            "Auth0TokenValidator: validated sub={Sub} discordPresent={DiscordPresent}",
            sub, !string.IsNullOrEmpty(discordId));

        return Task.CompletedTask;
    }
}
