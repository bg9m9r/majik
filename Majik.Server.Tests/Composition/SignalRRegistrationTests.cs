using FluentAssertions;
using Majik.Server.Composition;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Majik.Server.Tests.Composition;

public class SignalRRegistrationTests
{
    [Fact]
    public void NoConnectionString_RegistersSignalRWithoutBackplane()
    {
        var services = new ServiceCollection();
        var cfg = new ConfigurationBuilder().Build();

        services.AddLogging();
        services.AddMajikSignalR(cfg);

        var provider = services.BuildServiceProvider();
        // Core SignalR service is always present; absence of backplane is
        // observable by the lack of the Redis HubLifetimeManager descriptor.
        var hubLifetimeManagers = services
            .Where(d => d.ServiceType.FullName?.Contains("HubLifetimeManager", StringComparison.Ordinal) ?? false)
            .ToList();
        hubLifetimeManagers.Should().NotBeEmpty("SignalR must register a HubLifetimeManager");
        services.Any(d =>
            d.ImplementationType?.FullName?.Contains("Redis", StringComparison.OrdinalIgnoreCase) == true)
            .Should().BeFalse("no Redis-backed HubLifetimeManager when connection string is missing");
    }

    [Fact]
    public void WithConnectionString_RegistersRedisHubLifetimeManager()
    {
        var services = new ServiceCollection();
        var cfg = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Redis:ConnectionString"] = "localhost:6379",
            })
            .Build();

        services.AddLogging();
        services.AddMajikSignalR(cfg);

        // Redis backplane swaps in RedisHubLifetimeManager<T> as the open
        // generic for IHubLifetimeManager<>; assert the type registration is
        // present without booting a real Redis client.
        var redisManagerRegistered = services.Any(d =>
            d.ImplementationType?.FullName?.Contains("Redis", StringComparison.OrdinalIgnoreCase) == true);
        redisManagerRegistered.Should().BeTrue("AddStackExchangeRedis must register the Redis HubLifetimeManager");
    }

    [Fact]
    public void ChannelPrefix_IsNamespacedToAvoidCollisions()
    {
        SignalRRegistration.ChannelPrefix.Should().StartWith("majik:");
    }

    [Fact]
    public void ConnectionStringKey_MatchesDoubleUnderscoreEnvVarConvention()
    {
        // Render passes env vars Redis__ConnectionString → IConfiguration as Redis:ConnectionString.
        SignalRRegistration.ConnectionStringKey.Should().Be("Redis:ConnectionString");
    }
}
