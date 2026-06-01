using FluentAssertions;
using Majik.Core.Api;
using Majik.Core.Zones;
using Majik.Server.Matches;
using Majik.Server.Profiles;
using Majik.Server.Tests.Helpers;
using Majik.Server.Tests.Profiles;
using Xunit;

namespace Majik.Server.Tests.Matches;

/// <summary>
/// Deferral #8 — the bot's 15-card sideboard is wired into match-start deck
/// construction. Drives a real engine facade (via <see cref="GameRegistry"/>
/// + <see cref="Majik.Server.Composition.ServerGameFactory"/>) through the
/// vs-Bot create path and asserts the bot seat's sideboard (CR 100.4 /
/// CR 408 wishboard) is populated, so wish-tutor + companion effects can see
/// it the moment the game starts.
/// </summary>
public class MatchServiceBotSideboardTests : IClassFixture<TestMongoFixture>
{
    private readonly TestMongoFixture _fixture;
    public MatchServiceBotSideboardTests(TestMongoFixture fixture) => _fixture = fixture;

    private sealed class SeqRandom : IRandomSource
    {
        private readonly Queue<int> _v;
        public SeqRandom(params int[] v) => _v = new Queue<int>(v);
        public int NextInt(int min, int max) => _v.Count > 0 ? _v.Dequeue() : min;
    }

    private sealed class NullPublisher : IMatchHubPublisher
    {
        public void Publish(System.Guid matchId, string @event, object payload) { }
    }

    [Fact]
    public async Task CreateBotMatch_PopulatesBotSideboard_AtMatchStart()
    {
        var db = _fixture.NewDatabase();
        var matchRepo = new MatchRepository(db);
        await matchRepo.EnsureIndexesAsync(default);
        var profileRepo = new UserProfileRepository(db);
        await profileRepo.EnsureIndexesAsync(default);

        const string aliceSub = "u-alice";
        var now = System.DateTime.UtcNow;
        await profileRepo.UpsertAsync(new UserProfile
        {
            Sub = aliceSub, Handle = "alice", HandleDisplay = "Alice",
            CreatedAt = now, UpdatedAt = now,
        }, default);

        var registry = new GameRegistry();
        var gameFactory = new Majik.Server.Composition.ServerGameFactory(registry, cardRepo: null);
        var scheduler = new ImmediateBotMatchScheduler();
        var svc = new MatchService(matchRepo, profileRepo,
            new DiceRoller(new SeqRandom(1, 6)), new StubDeckLoader(), new SystemClock(),
            new NullPublisher(),
            timeoutScheduler: new MatchTimeoutScheduler((_, _, _) => Task.CompletedTask),
            gameFactory: gameFactory,
            deckOwnershipPolicy: new AllowStubDeckOwnershipPolicy(),
            botScheduler: scheduler);
        scheduler.Bind(svc);

        // Burn — a bot-supported archetype (ArchetypeWeights). Its sideboard
        // exercises the same match-start wiring; the wishboard angle is
        // unit-tested directly in GameFacadeTests.PopulateSideboard_*.
        const string archetype = "Burn";
        var expectedSideboard = Majik.Bot.Decks.BotDeckCatalog.GetSideboard(archetype);
        expectedSideboard.Should().HaveCount(15, "precondition: Burn ships a 15-card sideboard");

        var created = await svc.CreateAsync(aliceSub,
            new CreateMatchRequest("constructed", "invite", "starter", 20,
                BotOpponent: new BotOpponentRequest(archetype)),
            default);
        created.IsSuccess.Should().BeTrue(
            $"create should succeed (error: {created.Error?.Error} / {created.Error?.Detail})");

        var match = await matchRepo.GetByIdAsync(created.Value!.Id, default);
        match.Should().NotBeNull();
        match!.GameId.Should().NotBeNull("the facade was created so the game id is set");

        var facade = registry.Get(match.GameId!.Value);
        facade.Should().NotBeNull();

        // The bot is the Bob seat. Its sideboard (== wishboard, CR 408) holds
        // the 15 archetype sideboard cards as live, owned card instances.
        facade!.Bob.Sideboard.Count.Should().Be(15,
            "the bot's 15-card sideboard is wired into match-start deck construction");
        facade.Bob.Sideboard.GetCards().Should().OnlyContain(c => c.Zone == ZoneType.Sideboard);
        facade.Bob.Sideboard.GetCards().Should().OnlyContain(c => ReferenceEquals(c.Owner, facade.Bob));

        // The wishboard (CR 408 alias over the sideboard) is the SAME pile,
        // so any wish-tutor / companion effect can see these cards. Assert a
        // known Burn sideboard card is present.
        facade.Bob.Wishboard.GetCards().Select(c => c.Name).Should().Contain("Rest in Peace");

        // The human (Alice) seat got no sideboard wired (no archetype list).
        facade.Alice.Sideboard.Count.Should().Be(0);
    }
}
