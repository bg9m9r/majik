using FluentAssertions;
using Majik.Core.Api.Dtos;
using Majik.Server.Matches;
using Majik.Server.Profiles;
using Majik.Server.Tests.Helpers;
using Majik.Server.Tests.Profiles;
using Xunit;

namespace Majik.Server.Tests.Matches;

/// <summary>
/// Slice 4b #4 — CR-706 spectator-fallback hardening for
/// <see cref="MatchService.GetGameStateAsync"/>.
///
/// When <see cref="Majik.Core.Api.GameFacade.GetStateFor"/> returns null the
/// service used to fall back to the full-reveal <c>GetState()</c>, which
/// LEAKS the opponent's hand + library card names. The fix refuses that
/// path: it returns a <c>game-state-unavailable</c> error and NEVER
/// serializes the hidden zones.
///
/// Because <see cref="Majik.Core.Api.GameFacade"/> is sealed (no fakeable
/// GetStateFor), the post-snapshot decision is extracted into the pure
/// helper <see cref="MatchService.ResolveViewerStateResult"/>, which we drive
/// directly with a null snapshot to exercise the refusal branch.
/// </summary>
public class MatchServiceStateGuardTests : IClassFixture<TestMongoFixture>
{
    private readonly TestMongoFixture _fixture;
    public MatchServiceStateGuardTests(TestMongoFixture fixture) => _fixture = fixture;

    private MatchService NewService()
    {
        var db = _fixture.NewDatabase();
        return new MatchService(
            new MatchRepository(db),
            new UserProfileRepository(db),
            new DiceRoller(new SystemRandomSource()),
            new StubDeckLoader(),
            new SystemClock(),
            hub: null,
            timeoutScheduler: null,
            gameFactory: null,
            deckOwnershipPolicy: new AllowStubDeckOwnershipPolicy());
    }

    [Fact]
    public void ResolveViewerStateResult_NullSnapshot_ReturnsErrorNotFullReveal()
    {
        var svc = NewService();
        var matchId = Guid.NewGuid();
        var gameId = Guid.NewGuid();
        var aliceId = Guid.NewGuid();
        var bobId = Guid.NewGuid();

        // GetStateFor returned null → the service must REFUSE, not fall back
        // to the full-reveal spectator view.
        var result = svc.ResolveViewerStateResult(
            state: null,
            matchId: matchId,
            gameId: gameId,
            callerSub: "alice",
            viewerPlayerId: aliceId,
            aliceId: aliceId,
            bobId: bobId,
            isCreator: true);

        result.IsSuccess.Should().BeFalse(
            "a null per-viewer snapshot must NOT be served as a full-reveal DTO");
        result.Error!.Error.Should().Be("game-state-unavailable");
        // No DTO at all → no hidden zones serialized on this path.
        result.Value.Should().BeNull(
            "the leaky full-reveal GameStateDto must never be produced — the " +
            "opponent's hand/library card names are never serialized here (CR 706)");

        // The refusal is counted so the regression is observable in prod.
        svc.StateFallbackRefusedCount.Should().Be(1);
    }

    [Fact]
    public void ResolveViewerStateResult_NonNullSnapshot_ReturnsMaskedPerViewerView()
    {
        var svc = NewService();
        var aliceId = Guid.NewGuid();
        var bobId = Guid.NewGuid();

        // A real per-viewer snapshot (Alice's view) → served as-is. The
        // counter stays at 0; this is the healthy path.
        var perViewer = new GameStateDto(
            GameId: Guid.NewGuid(),
            TurnNumber: 1,
            Phase: "PreCombatMain",
            ActivePlayerId: aliceId,
            Players: Array.Empty<PlayerDto>(),
            Stack: Array.Empty<StackObjectDto>(),
            YouPlayerId: aliceId);

        var result = svc.ResolveViewerStateResult(
            state: perViewer,
            matchId: Guid.NewGuid(),
            gameId: Guid.NewGuid(),
            callerSub: "alice",
            viewerPlayerId: aliceId,
            aliceId: aliceId,
            bobId: bobId,
            isCreator: true);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeSameAs(perViewer,
            "the masked per-viewer snapshot is served unchanged");
        svc.StateFallbackRefusedCount.Should().Be(0);
    }
}
