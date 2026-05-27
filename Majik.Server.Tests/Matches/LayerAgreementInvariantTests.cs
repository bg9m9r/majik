using FluentAssertions;
using Majik.Server.Tests.Profiles;
using Xunit;

namespace Majik.Server.Tests.Matches;

/// <summary>
/// Robustness Slice 0 safety-net invariants, driven through
/// <see cref="LayerAgreementHarness"/>. These assert the three load-bearing
/// agreements between the engine, the server clock, and the wire contract:
///   1. clock holder ↔ engine active player (CR 117 / 103.7),
///   2. phase vocabulary is always disambiguated (never raw "Main"),
///   3. seat identity (Creator → Alice).
/// </summary>
public class LayerAgreementInvariantTests : IClassFixture<TestMongoFixture>
{
    private readonly TestMongoFixture _fixture;
    public LayerAgreementInvariantTests(TestMongoFixture fixture) => _fixture = fixture;

    // -----------------------------------------------------------------------
    // Task 2 — smoke test: bot match reaches Playing + a clock-update is seen.
    // -----------------------------------------------------------------------

    [Fact]
    public async Task BotMatch_ReachesPlaying_AndPublishesClockUpdate()
    {
        using var h = await LayerAgreementHarness.StartBotMatchAsync(_fixture, rngSeed: 1);

        // Facade exists and is live (active player resolvable).
        h.Facade.Should().NotBeNull();
        h.Facade.ActivePlayerId.Should().NotBe(System.Guid.Empty);

        // PlayDrawAsync publishes the opening match.clock-update on the
        // Rolling → Playing transition.
        h.Published.Snapshot().Select(e => e.@event)
            .Should().Contain("match.clock-update");
    }
}
