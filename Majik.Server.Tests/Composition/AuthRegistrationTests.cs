using FluentAssertions;
using Majik.Server.Composition;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using Xunit;

namespace Majik.Server.Tests.Composition;

public class AuthRegistrationTests
{
    [Fact]
    public void EnabledAuthority_DisablesInboundClaimMapping()
    {
        // Locks the fix for the prod 401 storm where ASP.NET's default
        // claim mapping rewrote `sub` to ClaimTypes.NameIdentifier,
        // making DescopeTokenValidator fail every request with
        // "sub missing" even though the JWT had a valid sub.
        var options = BuildJwtBearerOptions();

        options.MapInboundClaims.Should().BeFalse(
            "DescopeTokenValidator + AsPlayer policy read the raw `sub` claim; " +
            "ASP.NET's default mapping rewrites it to ClaimTypes.NameIdentifier and breaks both.");
    }

    [Fact]
    public async Task OnMessageReceived_HubPath_WithAccessTokenQuery_LiftsToken()
    {
        // Locks the fix for the SignalR /hubs/match/negotiate 401. WebSocket
        // upgrade can't send Authorization headers, so the SignalR JS client
        // passes the JWT in `?access_token=...`. JwtBearer middleware only
        // reads the Authorization header by default; without this lift the
        // hub negotiate 401's even with a valid token.
        var options = BuildJwtBearerOptions();
        var ctx = BuildMessageReceivedContext(
            options,
            path: "/hubs/match",
            queryString: "?access_token=test-jwt-value");

        await options.Events!.MessageReceived(ctx);

        ctx.Token.Should().Be("test-jwt-value");
    }

    [Fact]
    public async Task OnMessageReceived_HubPath_WithNegotiateSuffix_LiftsToken()
    {
        // /hubs/match/negotiate is the exact 401-ing path from prod. The
        // StartsWithSegments("/hubs") check catches /hubs/match,
        // /hubs/match/negotiate, and any future hub path.
        var options = BuildJwtBearerOptions();
        var ctx = BuildMessageReceivedContext(
            options,
            path: "/hubs/match/negotiate",
            queryString: "?negotiateVersion=1&access_token=test-jwt-value");

        await options.Events!.MessageReceived(ctx);

        ctx.Token.Should().Be("test-jwt-value");
    }

    [Fact]
    public async Task OnMessageReceived_NonHubPath_DoesNotLiftToken()
    {
        // REST endpoints already work via the Authorization header; lifting
        // the query token for them would let an attacker stash a JWT in a
        // shareable URL and have the server treat it as authentication. Keep
        // the query-string path narrowly scoped to /hubs/*.
        var options = BuildJwtBearerOptions();
        var ctx = BuildMessageReceivedContext(
            options,
            path: "/matches/abc/state",
            queryString: "?access_token=test-jwt-value");

        await options.Events!.MessageReceived(ctx);

        ctx.Token.Should().BeNull();
    }

    [Fact]
    public async Task OnMessageReceived_HubPath_NoAccessTokenQuery_DoesNotSetToken()
    {
        // Hub requests with Authorization header (or no auth at all) must
        // not have ctx.Token overwritten — the JwtBearer middleware reads
        // the header itself when ctx.Token stays null.
        var options = BuildJwtBearerOptions();
        var ctx = BuildMessageReceivedContext(
            options,
            path: "/hubs/match",
            queryString: "");

        await options.Events!.MessageReceived(ctx);

        ctx.Token.Should().BeNull();
    }

    private static JwtBearerOptions BuildJwtBearerOptions()
    {
        var services = new ServiceCollection();
        var cfg = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Auth:Authority"] = "https://auth.majik.tech/",
                ["Auth:Audience"] = "https://api.majik.tech",
            })
            .Build();

        services.AddLogging();
        services.AddMajikAuth(cfg);

        var provider = services.BuildServiceProvider();
        return provider
            .GetRequiredService<IOptionsMonitor<JwtBearerOptions>>()
            .Get(JwtBearerDefaults.AuthenticationScheme);
    }

    private static MessageReceivedContext BuildMessageReceivedContext(
        JwtBearerOptions options,
        string path,
        string queryString)
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Path = path;
        httpContext.Request.QueryString = new QueryString(queryString);

        var scheme = new AuthenticationScheme(
            JwtBearerDefaults.AuthenticationScheme,
            displayName: null,
            handlerType: typeof(JwtBearerHandler));

        return new MessageReceivedContext(httpContext, scheme, options);
    }
}
