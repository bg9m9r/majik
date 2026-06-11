using FluentAssertions;
using Majik.Bot;
using Majik.Bot.Decks;
using Majik.Bot.Tests.Integration.Helpers;
using Majik.Core.Api;
using Majik.Core.CardData;
using Majik.Core.Diagnostics;
using Majik.Core.Events;
using Xunit;

namespace Majik.Bot.Tests.Integration;

public class BotVsBotGameTests
{
    // Build the embedded seed once for the whole class — it loads ~22k rows
    // off a gzipped resource, so resolving it per theory case would be wasteful.
    private static readonly EmbeddedCardRepository Repo = new();

    [Fact]
    public async Task BurnVsBoros_PlaysGame_NoCrash()
    {
        var facade = GameFacade.Create(
            aliceName: "Burn-Bot",
            bobName: "Boros-Bot",
            aliceDeck: DeckLoader.Load("Burn"),
            bobDeck:   DeckLoader.Load("BorosEnergy"));

        facade.ReplaceAliceAgent(new BotPlayerAgent(facade.Alice, new BotConfig("Burn",        RandomSeed: 1)));
        facade.ReplaceBobAgent(  new BotPlayerAgent(facade.Bob,   new BotConfig("BorosEnergy", RandomSeed: 2)));

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await facade.StartFullGameAsync(maxTurns: 20, ct: cts.Token);
        await facade.FullGameTask!;
        facade.FullGameTask!.IsCompletedSuccessfully.Should().BeTrue();
    }

    /// <summary>Every bot archetype registered in <see cref="BotDeckCatalog"/> —
    /// one theory row per deck, so a newly-added archetype is smoke-tested
    /// automatically with no test edit.</summary>
    public static IEnumerable<object[]> AllArchetypes =>
        BotDeckCatalog.Archetypes.Select(a => new object[] { a });

    /// <summary>
    /// Full-game smoke for EACH bot deck: a mirror match (the archetype
    /// against a copy of itself, distinct seeds) materialized from REAL
    /// embedded-seed cards and run through the production binder/factory
    /// chain (<c>cardRepo</c> is passed to <see cref="GameFacade.Create"/>,
    /// <c>RouteThroughNamedFactories</c> defaults on — same path the server
    /// uses). The contract is "no crash to the turn cap": the engine must
    /// drive both bots through 20 turns of the deck's own cards without an
    /// unhandled exception (DEBUG rethrows event-handler errors, so a faulty
    /// card surfaces as a faulted game task here).
    ///
    /// <para>Mirror matches maximize exposure to each archetype's cards in a
    /// single run; cross-archetype interaction stays covered by
    /// <see cref="BurnVsBoros_PlaysGame_NoCrash"/>.</para>
    /// </summary>
    [Theory]
    [MemberData(nameof(AllArchetypes))]
    public async Task BotDeck_MirrorMatch_PlaysGame_NoCrash(string archetype)
    {
        var facade = GameFacade.Create(
            aliceName: $"{archetype}-A",
            bobName:   $"{archetype}-B",
            aliceDeck: DeckLoader.LoadReal(archetype, Repo),
            bobDeck:   DeckLoader.LoadReal(archetype, Repo),
            cardRepo:  Repo);

        // Runtime coverage hook: capture every vanilla-shell card the bots
        // actually encounter. The facade's bus isn't directly subscribable for
        // raw GameEvents, so use the VanillaShellTracker + shared-bus pattern.
        var encountered = new List<string>();
        var sharedBus = new EventBus();
        sharedBus.Subscribe<UnimplementedCardEncounteredEvent>(e =>
        {
            lock (encountered) encountered.Add(e.CardName);
        });
        var aliceTracker = new VanillaShellTracker(sharedBus, _ => { });
        var bobTracker = new VanillaShellTracker(sharedBus, _ => { });

        facade.ReplaceAliceAgent(new BotPlayerAgent(facade.Alice,
            new BotConfig(archetype, RandomSeed: 1, VanillaShellTracker: aliceTracker)));
        facade.ReplaceBobAgent(new BotPlayerAgent(facade.Bob,
            new BotConfig(archetype, RandomSeed: 2, VanillaShellTracker: bobTracker)));

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        await facade.StartFullGameAsync(maxTurns: 20, ct: cts.Token);
        await facade.FullGameTask!;
        facade.FullGameTask!.IsCompletedSuccessfully.Should().BeTrue(
            $"the '{archetype}' mirror match must run to the turn cap without crashing");

        // Any shell the bot drew must be a recorded gap — an UNregistered shell
        // surfacing in real play is exactly what we want to catch.
        var unregistered = encountered
            .Distinct(StringComparer.Ordinal)
            .Where(n => !KnownPartialImplementations.TryGet(n, out _))
            .ToList();
        unregistered.Should().BeEmpty(
            $"every vanilla-shell card the '{archetype}' bots encountered must be "
            + "in KnownPartialImplementations: " + string.Join(", ", unregistered));
    }
}
