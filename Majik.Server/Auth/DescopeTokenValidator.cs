using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Majik.Server.Auth;

/// <summary>
/// Descope-specific validation hardening on top of the standard JwtBearer
/// pipeline. Runs after the signature + issuer + audience + lifetime
/// checks succeed.
///
/// Descope `sub` values are opaque project-scoped user IDs (e.g.
/// `U2P...`) — unlike Auth0's `oauth2|{provider}|{external-id}` layout
/// they carry no provider info, so we don't enforce a prefix. We only
/// require that `sub` exist and is non-empty.
///
/// To get a Discord user ID into the principal, configure Descope to
/// emit either a top-level <c>discordUserId</c> claim or a nested
/// <c>customAttributes.discordUserId</c> claim on the JWT (via a custom
/// claim mapper attached to the Discord social connection). When present
/// we lift it onto the principal under <see cref="DiscordUserIdClaim"/>
/// so downstream code (audit log, future friends list) reads it without
/// parsing JSON.
///
/// Disabled when `Auth:Authority` is unset — the handler is not wired
/// in that mode (see <see cref="Composition.AuthRegistration"/>).
/// </summary>
public static class DescopeTokenValidator
{
    public const string DiscordUserIdClaim = "discordUserId";

    private const string CustomAttributesClaim = "customAttributes";

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
                "DescopeTokenValidator: sub missing. Claim types: {ClaimTypes}",
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

        var discordId = ExtractDiscordUserId(context.Principal!);
        if (!string.IsNullOrEmpty(discordId) && !identity.HasClaim(c => c.Type == DiscordUserIdClaim))
        {
            identity.AddClaim(new Claim(DiscordUserIdClaim, discordId));
        }

        // Demoted from Information to Debug + dropped the discordUserId
        // value entirely (PII). The Discord ID is still lifted onto the
        // principal above; we just don't log it on every request.
        logger.LogDebug(
            "DescopeTokenValidator: validated sub={Sub} discordPresent={DiscordPresent}",
            sub, !string.IsNullOrEmpty(discordId));

        return Task.CompletedTask;
    }

    private static string? ExtractDiscordUserId(ClaimsPrincipal principal)
    {
        var direct = principal.FindFirst(DiscordUserIdClaim)?.Value;
        if (!string.IsNullOrEmpty(direct))
        {
            return direct;
        }

        var customAttrs = principal.FindFirst(CustomAttributesClaim)?.Value;
        if (string.IsNullOrEmpty(customAttrs))
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(customAttrs);
            if (doc.RootElement.ValueKind == JsonValueKind.Object
                && doc.RootElement.TryGetProperty(DiscordUserIdClaim, out var prop)
                && prop.ValueKind == JsonValueKind.String)
            {
                return prop.GetString();
            }
        }
        catch (JsonException)
        {
            // customAttributes wasn't a JSON object — ignore.
        }

        return null;
    }
}
