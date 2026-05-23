using System.Security.Claims;
using FluentAssertions;
using Majik.Server.Auth;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Majik.Server.Tests;

/// <summary>Locks Auth0 sub validation and the Discord-claim lift from
/// the namespaced custom claim Auth0 emits via the post-login Action.</summary>
public class Auth0TokenValidatorTests
{
    private const string NamespacedDiscordClaim = "https://majik.tech/discord_user_id";

    [Fact]
    public async Task ValidateAsync_OpaqueAuth0Sub_PassesSilently()
    {
        // Auth0 social-connection subs look like "oauth2|discord|{id}".
        // The validator treats sub as opaque — no format check.
        var ctx = BuildContext(new Claim("sub", "oauth2|discord|123456789012345678"));

        await Auth0TokenValidator.ValidateAsync(ctx);

        ctx.Result.Should().BeNull("opaque Auth0 subs are valid; no format check");
    }

    [Fact]
    public async Task ValidateAsync_MissingSub_Fails()
    {
        var ctx = BuildContext();

        await Auth0TokenValidator.ValidateAsync(ctx);

        ctx.Result.Should().NotBeNull();
        ctx.Result!.Failure!.Message.Should().Contain("sub");
    }

    [Fact]
    public async Task ValidateAsync_NamespacedDiscordClaim_LiftsToPrincipal()
    {
        var ctx = BuildContext(
            new Claim("sub", "oauth2|discord|abc"),
            new Claim(NamespacedDiscordClaim, "123456789012345678"));

        await Auth0TokenValidator.ValidateAsync(ctx);

        ctx.Principal!.FindFirst(Auth0TokenValidator.DiscordUserIdClaim)!.Value
            .Should().Be("123456789012345678");
    }

    [Fact]
    public async Task ValidateAsync_NoDiscordClaim_LeavesPrincipalAlone()
    {
        // Non-Discord auth (or an Auth0 tenant where the post-login Action
        // didn't fire) — sub is still required but the Discord lift is
        // optional, so validation succeeds without touching the principal.
        var ctx = BuildContext(new Claim("sub", "auth0|password-user-id"));

        await Auth0TokenValidator.ValidateAsync(ctx);

        ctx.Result.Should().BeNull();
        ctx.Principal!.FindFirst(Auth0TokenValidator.DiscordUserIdClaim).Should().BeNull();
    }

    [Fact]
    public async Task ValidateAsync_DiscordClaim_RunTwice_DoesNotDuplicate()
    {
        var ctx = BuildContext(
            new Claim("sub", "oauth2|discord|abc"),
            new Claim(NamespacedDiscordClaim, "111"));

        await Auth0TokenValidator.ValidateAsync(ctx);
        await Auth0TokenValidator.ValidateAsync(ctx);

        ctx.Principal!.Claims
            .Count(c => c.Type == Auth0TokenValidator.DiscordUserIdClaim)
            .Should().Be(1);
    }

    [Fact]
    public async Task ValidateAsync_NonNamespacedDiscordClaim_IsIgnored()
    {
        // Belt-and-suspenders: Auth0 would have stripped a non-namespaced
        // custom claim before issuing the token, but a misconfigured Action
        // or a future migration could leak one through. Make sure we don't
        // accidentally trust the bare `discordUserId` (which is the name
        // we LIFT TO, not from — would create a circular trust).
        var ctx = BuildContext(
            new Claim("sub", "oauth2|discord|abc"),
            new Claim("discordUserId", "attacker-id"));

        await Auth0TokenValidator.ValidateAsync(ctx);

        // Pre-existing discordUserId claim stays (we don't strip incoming
        // claims), but no lift happens from the namespaced source.
        ctx.Principal!.Claims
            .Count(c => c.Type == Auth0TokenValidator.DiscordUserIdClaim)
            .Should().Be(1);
        ctx.Principal!.FindFirst(Auth0TokenValidator.DiscordUserIdClaim)!.Value
            .Should().Be("attacker-id",
                "the validator doesn't strip pre-existing claims; the JWT signature is the trust root");
    }

    private static TokenValidatedContext BuildContext(params Claim[] claims)
    {
        var identity = new ClaimsIdentity(claims, "TestAuth");
        var principal = new ClaimsPrincipal(identity);
        var httpContext = new DefaultHttpContext();
        var services = new ServiceCollection();
        services.AddLogging();
        httpContext.RequestServices = services.BuildServiceProvider();
        var scheme = new AuthenticationScheme(
            JwtBearerDefaults.AuthenticationScheme,
            null,
            typeof(JwtBearerHandler));
        var options = new JwtBearerOptions();
        var ctx = new TokenValidatedContext(httpContext, scheme, options)
        {
            Principal = principal,
        };
        return ctx;
    }
}
