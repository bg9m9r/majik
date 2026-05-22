using FluentAssertions;
using Majik.Server.Composition;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
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
        var services = new ServiceCollection();
        var cfg = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Auth:Authority"] = "https://api.descope.com/P000000000000000000000000",
                ["Auth:Audience"] = "P000000000000000000000000",
            })
            .Build();

        services.AddLogging();
        services.AddMajikAuth(cfg);

        using var provider = services.BuildServiceProvider();
        var options = provider
            .GetRequiredService<IOptionsMonitor<JwtBearerOptions>>()
            .Get(JwtBearerDefaults.AuthenticationScheme);

        options.MapInboundClaims.Should().BeFalse(
            "DescopeTokenValidator + AsPlayer policy read the raw `sub` claim; " +
            "ASP.NET's default mapping rewrites it to ClaimTypes.NameIdentifier and breaks both.");
    }
}
