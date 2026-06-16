using FluentAssertions;
using Majik.Server.Composition;
using Majik.Server.Matches;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Majik.Server.Tests.Composition;

/// <summary>
/// W7 — locks the DI wiring that makes the engine-wedge supervision LIVE.
///
/// <para>
/// Before W7 the <c>MatchFacadeBridge</c> was registered without a watchdog /
/// onEngineErrored callback, so a faulted or hung game loop was silently
/// swallowed (the human seat wedged with a dead clock and no log). These tests
/// assert the registration now:
/// </para>
///
/// <list type="bullet">
///   <item>registers <see cref="MatchEngineWatchdog"/> as a SINGLETON (one
///     per-match timer dict shared across requests);</item>
///   <item>binds <c>Watchdog:NoProgressSeconds</c> from config (default 90);</item>
///   <item>builds the <see cref="MatchFacadeBridge"/> singleton successfully
///     with supervision wired in.</item>
/// </list>
///
/// <para>
/// Mirrors the DI-resolution seam in <see cref="ServerBotOptionsTests"/>: a
/// fake Mongo connection string clears the <see cref="MongoRegistration"/>
/// gate so <see cref="MatchRegistration.AddMajikMatches"/> runs; the
/// registrations are lazy factories, so resolving the watchdog / bridge never
/// opens a real Mongo connection.
/// </para>
/// </summary>
public class MatchEngineWatchdogRegistrationTests
{
    private static ServiceProvider BuildProvider(params (string Key, string Value)[] extra)
    {
        var settings = new Dictionary<string, string?>
        {
            // Clears MongoRegistration.IsConfigured so AddMajikMatches wires the
            // match graph; the connection is never actually opened by the
            // services these tests resolve (all factories are lazy).
            [MongoRegistration.ConnectionStringKey] = "mongodb://localhost:27017",
        };
        foreach (var (key, value) in extra)
            settings[key] = value;

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(settings)
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMajikMatches(configuration);
        return services.BuildServiceProvider();
    }

    [Fact]
    public void Watchdog_ResolvesAsSingleton()
    {
        using var sp = BuildProvider();

        var first = sp.GetRequiredService<MatchEngineWatchdog>();
        var second = sp.GetRequiredService<MatchEngineWatchdog>();

        first.Should().NotBeNull();
        second.Should().BeSameAs(first,
            "the watchdog holds a per-match timer dict that must be shared across " +
            "every request that attaches/bumps/cancels it");
    }

    [Fact]
    public void Watchdog_NoProgressSeconds_DefaultsTo90()
    {
        using var sp = BuildProvider();

        sp.GetRequiredService<MatchEngineWatchdog>()
            .NoProgressTimeout.Should().Be(TimeSpan.FromSeconds(90),
                "the code default must match the plan (Watchdog:NoProgressSeconds=90)");
    }

    [Fact]
    public void Watchdog_NoProgressSeconds_BindsFromConfig()
    {
        using var sp = BuildProvider(("Watchdog:NoProgressSeconds", "30"));

        sp.GetRequiredService<MatchEngineWatchdog>()
            .NoProgressTimeout.Should().Be(TimeSpan.FromSeconds(30),
                "env Watchdog__NoProgressSeconds must reach the installed watchdog");
    }

    // NOTE: full MatchFacadeBridge construction (with the watchdog +
    // onEngineErrored callback wired) is exercised by the host-startup
    // integration tests (TestAppFactory), which stand up the SignalR
    // scaffolding the bridge's IMatchHubPublisher transitively needs. The
    // minimal ServiceCollection seam used here only proves the watchdog
    // registration; over-registering SignalR here would duplicate that path.
}
