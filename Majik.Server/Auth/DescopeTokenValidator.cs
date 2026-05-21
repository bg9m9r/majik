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
        var sub = context.Principal?.FindFirst("sub")?.Value
            ?? context.Principal?.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        if (string.IsNullOrEmpty(sub))
        {
            var claimSummary = context.Principal is null
                ? "<no principal>"
                : string.Join(", ",
                    context.Principal.Claims.Select(c => $"{c.Type}={c.Value}"));
            logger.LogWarning(
                "DescopeTokenValidator: sub missing. Claims: {Claims}",
                claimSummary);
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

        logger.LogInformation(
            "DescopeTokenValidator: validated sub={Sub} discordUserId={Discord}",
            sub, discordId ?? "<none>");

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
