using System.Security.Claims;
using FluentAssertions;
using Majik.Server.Auth;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace Majik.Server.Tests;

/// <summary>Locks the Auth0 sub-format enforcement and Discord
/// convenience-claim extraction added in the API-middleware slice.</summary>
public class Auth0TokenValidatorTests
{
    [Fact]
    public async Task ValidateAsync_DiscordSub_AddsDiscordUserIdClaim()
    {
        var ctx = BuildContext(new Claim("sub", "oauth2|discord|123456789012345678"));

        await Auth0TokenValidator.ValidateAsync(ctx);

        ctx.Result.Should().BeNull("validation passes silently on success");
        var identity = (ClaimsIdentity)ctx.Principal!.Identity!;
        identity.FindFirst(Auth0TokenValidator.DiscordUserIdClaim)!.Value
            .Should().Be("123456789012345678");
    }

    [Fact]
    public async Task ValidateAsync_NonDiscordSub_DoesNotAddDiscordClaim()
    {
        var ctx = BuildContext(new Claim("sub", "oauth2|google-oauth2|abc"));

        await Auth0TokenValidator.ValidateAsync(ctx);

        ctx.Result.Should().BeNull();
        ctx.Principal!.FindFirst(Auth0TokenValidator.DiscordUserIdClaim).Should().BeNull();
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
    public async Task ValidateAsync_NonSocialSub_Fails()
    {
        // Username-password DB connection produces "auth0|..."
        var ctx = BuildContext(new Claim("sub", "auth0|abc123"));

        await Auth0TokenValidator.ValidateAsync(ctx);

        ctx.Result.Should().NotBeNull();
        ctx.Result!.Failure!.Message.Should().Contain("oauth2|");
    }

    [Fact]
    public async Task ValidateAsync_MalformedSocialSub_Fails()
    {
        var ctx = BuildContext(new Claim("sub", "oauth2|discord"));

        await Auth0TokenValidator.ValidateAsync(ctx);

        ctx.Result.Should().NotBeNull();
        ctx.Result!.Failure!.Message.Should().Contain("Auth0 social");
    }

    [Fact]
    public async Task ValidateAsync_DiscordSub_RunTwice_DoesNotDuplicateClaim()
    {
        var ctx = BuildContext(new Claim("sub", "oauth2|discord|111"));

        await Auth0TokenValidator.ValidateAsync(ctx);
        await Auth0TokenValidator.ValidateAsync(ctx);

        ctx.Principal!.Claims
            .Count(c => c.Type == Auth0TokenValidator.DiscordUserIdClaim)
            .Should().Be(1);
    }

    private static TokenValidatedContext BuildContext(params Claim[] claims)
    {
        var identity = new ClaimsIdentity(claims, "TestAuth");
        var principal = new ClaimsPrincipal(identity);
        var httpContext = new DefaultHttpContext();
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
