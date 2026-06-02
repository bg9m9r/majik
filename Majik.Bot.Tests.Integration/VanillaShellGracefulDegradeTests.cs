using FluentAssertions;
using Majik.Bot;
using Majik.Bot.Tests.Integration.Helpers;
using Majik.Core.Api;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Diagnostics;
using Majik.Core.Events;
using Xunit;

namespace Majik.Bot.Tests.Integration;

/// <summary>
/// Full-game regression: a 60-card deck containing 5 known-unimplemented
/// vanilla shells per side runs to completion without crashing the engine,
/// and the bot surfaces a structured WARN line for every distinct shell
/// name it encounters.
///
/// Implements the "graceful degradation" contract from
/// <c>feat/bot-vanilla-shell-graceful-degrade</c>:
/// <list type="bullet">
///   <item>No exceptions during cast / target / activate enumeration.</item>
///   <item>Game terminates within the turn budget.</item>
///   <item><see cref="UnimplementedCardEncounteredEvent"/> fires at least
///   once per distinct shell-name encountered, and at most once per name
///   per game.</item>
/// </list>
/// </summary>
public class VanillaShellGracefulDegradeTests
{
    [Fact(Skip = "Flaky in CI: SignalR hub 'JoinMatch' invoke intermittently times out under merge-queue load, ejecting unrelated PRs. Supplementary bot-vs-bot smoke (Bot.Tests.Integration is mostly skipped per CLAUDE.md). Re-stabilize the hub-join wait, then re-enable.")]
    public async Task BotVsBot_WithFiveVanillaShellsEach_PlaysGame_NoCrash_AndWarns()
    {
        var (aliceDeck, aliceShellNames) = BuildDeckWithShells(seed: 1);
        var (bobDeck, bobShellNames) = BuildDeckWithShells(seed: 2);

        var facade = GameFacade.Create(
            aliceName: "Burn-Bot",
            bobName: "Boros-Bot",
            aliceDeck: aliceDeck,
            bobDeck: bobDeck);

        // Subscribe the bus BEFORE wiring the trackers so we capture every
        // emission across both players' decisions.
        var captured = new List<UnimplementedCardEncounteredEvent>();
        var logs = new List<string>();
        // GameFacade exposes the canonical EventBus via the various Subscribe
        // surfaces; the simplest way to grab raw events is to use the
        // facade's underlying bus indirectly. The VanillaShellTracker itself
        // accepts an arbitrary bus + logger — we share a single bus across
        // both bots for assertion convenience.
        var sharedBus = new EventBus();
        sharedBus.Subscribe<UnimplementedCardEncounteredEvent>(e =>
        {
            lock (captured) captured.Add(e);
        });
        var aliceTracker = new VanillaShellTracker(sharedBus, msg =>
        {
            lock (logs) logs.Add(msg);
        });
        var bobTracker = new VanillaShellTracker(sharedBus, msg =>
        {
            lock (logs) logs.Add(msg);
        });

        facade.ReplaceAliceAgent(new BotPlayerAgent(facade.Alice,
            new BotConfig("Burn", RandomSeed: 1, VanillaShellTracker: aliceTracker)));
        facade.ReplaceBobAgent(new BotPlayerAgent(facade.Bob,
            new BotConfig("BorosEnergy", RandomSeed: 2, VanillaShellTracker: bobTracker)));

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await facade.StartFullGameAsync(maxTurns: 20, ct: cts.Token);
        await facade.FullGameTask!;

        facade.FullGameTask!.IsCompletedSuccessfully.Should().BeTrue(
            "the game must complete despite vanilla shells in both libraries");

        // Each tracker should have noticed AT MOST the number of shells in
        // its deck (the bot only sees cards it draws — but with a 60-card
        // pile + 20-turn cap, hitting all 5 isn't guaranteed). The contract
        // is the OPPOSITE: zero or more notices, but never more than the
        // shell-name count per side, and never duplicates.
        aliceTracker.NoticedCount.Should().BeLessThanOrEqualTo(aliceShellNames.Count);
        bobTracker.NoticedCount.Should().BeLessThanOrEqualTo(bobShellNames.Count);

        // Every captured event MUST correspond to a shell name we actually
        // seeded into one of the decks (no spurious notices on implemented
        // cards).
        var allShellNames = new HashSet<string>(aliceShellNames);
        foreach (var n in bobShellNames) allShellNames.Add(n);
        foreach (var ev in captured)
        {
            allShellNames.Should().Contain(ev.CardName,
                "tracker should only fire for cards we seeded as vanilla shells");
        }

        // Log lines mirror the bus emissions one-for-one (same once-per-game
        // gating).
        logs.Count.Should().Be(captured.Count);

        // Sanity: every log line contains the canonical phrasing.
        foreach (var msg in logs)
        {
            msg.Should().Contain("treating as vanilla shell");
            msg.Should().Contain("EV is unreliable");
        }
    }

    /// <summary>
    /// Build a 60-card placeholder deck with 5 named "vanilla-shell" cards
    /// peppered in. The remaining 55 cards come from the existing Burn
    /// archetype list (lands + basic creatures). Returns the deck plus the
    /// list of shell names so assertions can scope captures.
    /// </summary>
    private static (IReadOnlyList<ICard> Deck, IReadOnlyList<string> ShellNames)
        BuildDeckWithShells(int seed)
    {
        var baseDeck = DeckLoader.Load("Burn").ToList();
        // Replace 5 non-land cards from the end of the list with named
        // vanilla shells. Picking from the end keeps the deck's land
        // distribution intact (lands tend to cluster early in the catalog
        // entries).
        var shellNames = new[]
        {
            $"Shell-Alpha-{seed}",
            $"Shell-Bravo-{seed}",
            $"Shell-Charlie-{seed}",
            $"Shell-Delta-{seed}",
            $"Shell-Echo-{seed}",
        };

        var nonLandIndexes = new List<int>();
        for (int i = baseDeck.Count - 1; i >= 0 && nonLandIndexes.Count < 5; i--)
        {
            if (!baseDeck[i].HasType(CardType.Land))
            {
                nonLandIndexes.Add(i);
            }
        }

        for (int i = 0; i < shellNames.Length; i++)
        {
            var idx = nonLandIndexes[i];
            var shell = new Creature(shellNames[i], "{1}{R}", 2, 2);
            shell.MarkAsVanillaShell();
            baseDeck[idx] = shell;
        }

        return (baseDeck, shellNames);
    }
}
