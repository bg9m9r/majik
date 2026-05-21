using System.Security.Claims;
using FluentAssertions;
using Majik.Server.Auth;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Majik.Server.Tests;

/// <summary>Locks Descope sub validation and the Discord-claim lift —
/// from either a direct top-level claim or the nested customAttributes
/// JSON blob Descope emits for custom attributes.</summary>
public class DescopeTokenValidatorTests
{
    [Fact]
    public async Task ValidateAsync_OpaqueDescopeSub_PassesSilently()
    {
        var ctx = BuildContext(new Claim("sub", "U2P0123456789ABCDEF"));

        await DescopeTokenValidator.ValidateAsync(ctx);

        ctx.Result.Should().BeNull("opaque Descope subs are valid; no format check");
    }

    [Fact]
    public async Task ValidateAsync_MissingSub_Fails()
    {
        var ctx = BuildContext();

        await DescopeTokenValidator.ValidateAsync(ctx);

        ctx.Result.Should().NotBeNull();
        ctx.Result!.Failure!.Message.Should().Contain("sub");
    }

    [Fact]
    public async Task ValidateAsync_DirectDiscordClaim_LiftsToPrincipal()
    {
        var ctx = BuildContext(
            new Claim("sub", "U2Pabc"),
            new Claim(DescopeTokenValidator.DiscordUserIdClaim, "123456789012345678"));

        await DescopeTokenValidator.ValidateAsync(ctx);

        ctx.Principal!.FindFirst(DescopeTokenValidator.DiscordUserIdClaim)!.Value
            .Should().Be("123456789012345678");
    }

    [Fact]
    public async Task ValidateAsync_DiscordInCustomAttributesJson_LiftsToPrincipal()
    {
        var ctx = BuildContext(
            new Claim("sub", "U2Pabc"),
            new Claim("customAttributes", "{\"discordUserId\":\"987654321\"}", "JSON"));

        await DescopeTokenValidator.ValidateAsync(ctx);

        ctx.Principal!.FindFirst(DescopeTokenValidator.DiscordUserIdClaim)!.Value
            .Should().Be("987654321");
    }

    [Fact]
    public async Task ValidateAsync_NoDiscordAnywhere_LeavesPrincipalAlone()
    {
        var ctx = BuildContext(new Claim("sub", "U2Pabc"));

        await DescopeTokenValidator.ValidateAsync(ctx);

        ctx.Principal!.FindFirst(DescopeTokenValidator.DiscordUserIdClaim).Should().BeNull();
    }

    [Fact]
    public async Task ValidateAsync_MalformedCustomAttributes_DoesNotThrow()
    {
        var ctx = BuildContext(
            new Claim("sub", "U2Pabc"),
            new Claim("customAttributes", "not-json"));

        await DescopeTokenValidator.ValidateAsync(ctx);

        ctx.Result.Should().BeNull();
        ctx.Principal!.FindFirst(DescopeTokenValidator.DiscordUserIdClaim).Should().BeNull();
    }

    [Fact]
    public async Task ValidateAsync_DiscordClaim_RunTwice_DoesNotDuplicate()
    {
        var ctx = BuildContext(
            new Claim("sub", "U2Pabc"),
            new Claim(DescopeTokenValidator.DiscordUserIdClaim, "111"));

        await DescopeTokenValidator.ValidateAsync(ctx);
        await DescopeTokenValidator.ValidateAsync(ctx);

        ctx.Principal!.Claims
            .Count(c => c.Type == DescopeTokenValidator.DiscordUserIdClaim)
            .Should().Be(1);
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
