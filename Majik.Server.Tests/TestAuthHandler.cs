using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Majik.Server.Tests;

/// <summary>
/// Bypasses JWT validation in integration tests by always authenticating
/// the request with a known principal. The handler reads optional
/// headers so individual tests can scope the principal:
///   X-Test-Sub  → "sub" claim (defaults to "test-user")
///   X-Test-Name → "name" claim (defaults to the sub)
///
/// Real production auth flows through JwtBearer; this handler is only
/// installed by <see cref="TestAppFactory"/>.
/// </summary>
public sealed class TestAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public new const string Scheme = "Test";

    public TestAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : base(options, logger, encoder) { }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var sub = Request.Headers["X-Test-Sub"].FirstOrDefault() ?? "test-user";
        var name = Request.Headers["X-Test-Name"].FirstOrDefault() ?? sub;

        var claims = new[]
        {
            new Claim("sub", sub),
            new Claim(ClaimTypes.NameIdentifier, sub),
            new Claim(ClaimTypes.Name, name),
        };
        var identity = new ClaimsIdentity(claims, Scheme, "sub", null);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, Scheme);

        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
