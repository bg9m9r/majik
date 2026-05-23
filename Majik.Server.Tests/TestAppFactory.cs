using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Majik.Server.Composition;

namespace Majik.Server.Tests;

/// <summary>
/// WebApplicationFactory specialization that swaps the production OIDC
/// auth for <see cref="TestAuthHandler"/>. Every test request is
/// authenticated; per-test claims controlled via the X-Test-Sub and
/// X-Test-Name request headers.
/// </summary>
public sealed class TestAppFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        // Cards:BaseUrl is required by MajikEngineRegistration. Tests that
        // need a real ICardRepository override it via ConfigureTestServices;
        // this stub URL just lets DI satisfy the HttpClient registration.
        builder.UseSetting("Cards:BaseUrl", "http://test.invalid");

        builder.ConfigureTestServices(services =>
        {
            // Replace the JwtBearer scheme with the test handler so every
            // request gets a fake but-fully-authenticated principal.
            services.AddAuthentication(TestAuthHandler.Scheme)
                .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(
                    TestAuthHandler.Scheme, _ => { });

            // Re-register the AsPlayer policy under the test scheme so
            // [Authorize(Policy = AsPlayer)] resolves to a passing
            // policy when the handler signs the principal in.
            services.AddAuthorization(options =>
            {
                options.AddPolicy(AuthRegistration.AsPlayerPolicy, p =>
                {
                    p.AuthenticationSchemes = new[] { TestAuthHandler.Scheme };
                    p.RequireAuthenticatedUser();
                    p.RequireClaim("sub");
                });
            });
        });
    }
}
