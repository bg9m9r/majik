using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication.JwtBearer;

namespace Majik.Server.Auth;

/// <summary>
/// Descope-specific validation hardening on top of the standard JwtBearer
/// pipeline. Runs after the signature + issuer + audience + lifetime
/// checks succeed.
///
/// Descope `sub` values are opaque project-scoped user IDs (e.g.
/// `U2P...`) — unlike Auth0's `oauth2|{provider}|{external-id}` layout
/// they carry no provider info, so we don't enforce a prefix. We only
/// require that `sub` exist and is non-empty; <see cref="GameSeating"/>
/// keys seats by that value.
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
        var identity = context.Principal?.Identity as ClaimsIdentity;
        var sub = context.Principal?.FindFirst("sub")?.Value;

        if (string.IsNullOrEmpty(sub))
        {
            context.Fail("Token missing 'sub' claim.");
            return Task.CompletedTask;
        }

        if (identity is null)
        {
            return Task.CompletedTask;
        }

        var discordId = ExtractDiscordUserId(context.Principal!);
        if (!string.IsNullOrEmpty(discordId) && !identity.HasClaim(c => c.Type == DiscordUserIdClaim))
        {
            identity.AddClaim(new Claim(DiscordUserIdClaim, discordId));
        }

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
