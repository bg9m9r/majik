using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.Api;
using Majik.Core.Api.Commands;
using Majik.Core.Api.Dtos;
using Majik.Core.CardData;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Game;
using Majik.Core.Random;
using Majik.Core.Zones;
using Xunit;
using Xunit.Abstractions;

namespace Majik.Core.Api.Tests;

/// <summary>
/// LIVE-PLAY integration coverage for FETCHING a shock land. A fetchland
/// ("Verdant Catacombs": "{T}, Pay 1 life, Sacrifice this land: Search your
/// library for a Swamp or Forest card, put it onto the battlefield, then
/// shuffle.") searches out a shock land ("Overgrown Tomb": "As Overgrown Tomb
/// enters, you may pay 2 life. If you don't, it enters tapped.") and puts it
/// onto the battlefield.
///
/// <para>Reported live-play bug: PLAYING a shock land from hand correctly
/// prompts "pay 2 life?", but FETCHING one onto the battlefield delivers NO
/// yes/no prompt — the land just enters (tapped) with the player never asked.
/// The play path threads the actor's seat agent into the resolution context;
/// the fetch path lost it (ctx.Agent == null), so the shock replacement had no
/// agent to prompt and silently declined (CR 614.1c → tapped).</para>
///
/// <para>This drives the REAL <see cref="GameFacade"/> end-to-end through the
/// production binder chain + the full <see cref="StartFullGameAsync"/> driver —
/// the SAME path the server runs — playing a prod-built Verdant Catacombs and
/// activating it to fetch a prod-built Overgrown Tomb out of the library.</para>
/// </summary>
[Collection(FuzzCollection.Name)] // serial: shares the full-game driver model
public sealed class FetchLandShockLivePromptTests
{
    private const string Fetch = "Verdant Catacombs"; // searches Swamp or Forest
    private const string Tomb = "Overgrown Tomb";      // a Swamp Forest shock land
    private readonly ITestOutputHelper _out;

    public FetchLandShockLivePromptTests(ITestOutputHelper output) => _out = output;

    [Fact]
    public void Seeds_Exist_AndBindThroughProd()
    {
        var repo = new EmbeddedCardRepository();
        repo.GetByName(Fetch).Should().NotBeNull($"'{Fetch}' must be in the embedded pool");
        repo.GetByName(Tomb).Should().NotBeNull($"'{Tomb}' must be in the embedded pool");
    }

    [Theory]
    [InlineData(false)] // decline → fetched tomb enters TAPPED, life only the fetch's 1
    [InlineData(true)]  // accept  → fetched tomb enters UNTAPPED, fetch's 1 + shock's 2
    public async Task FetchingTomb_DeliversPay2LifePrompt_AndHonoursTheAnswer(bool payTwoLife)
    {
        var repo = new EmbeddedCardRepository();

        // Alice's deck: many fetchlands up top so she draws one fast, plus a
        // pile of tombs (in the library, the legal fetch target). Built through
        // the PROD path so the live binder chain wires the fetch ability AND
        // the shock replacement exactly as in production.
        var aliceDeck = new List<ICard>();
        for (var i = 0; i < 16; i++) aliceDeck.Add(Shell(repo, Fetch));
        for (var i = 0; i < 24; i++) aliceDeck.Add(Shell(repo, Tomb));

        var bobDeck = new List<ICard>();
        for (var i = 0; i < 40; i++) bobDeck.Add(new Land("Island"));

        const int seed = 13579;
        using var idScope = DeterministicIdScope.Push(new DeterministicIdSource(seed));
        var facade = GameFacade.Create("Alice", "Bob", aliceDeck, bobDeck, cardRepo: repo);

        var aliceId = facade.Alice.Id;
        var aliceStartingLife = facade.Alice.LifeTotal;

        var prompts = new List<PromptDto>();
        var yesNoSeen = new TaskCompletionSource<PromptDto>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        var channel = System.Threading.Channels.Channel.CreateUnbounded<PromptDto>();
        using var sub = facade.SubscribePrompts(p =>
        {
            prompts.Add(p);
            channel.Writer.TryWrite(p);
            if (p.ExpectedKinds.Contains(nameof(ChooseYesNoCommand)))
                yesNoSeen.TrySetResult(p);
        });

        await facade.StartFullGameAsync(
            maxTurns: 3, rng: new GameRandom(seed), logicalClock: new LogicalClock());
        var game = facade.FullGameTask!;

        var playedFetch = false;
        var activatedFetch = false;
        var wedgeError = (Exception?)null;

        for (var step = 0; step < 600; step++)
        {
            if (game.IsCompleted) break;
            if (yesNoSeen.Task.IsCompleted) break;

            var read = channel.Reader.WaitToReadAsync().AsTask();
            var winner = await Task.WhenAny(read, game).WaitAsync(TimeSpan.FromSeconds(15));
            if (winner == game) break;
            if (!await read) break;
            if (!channel.Reader.TryRead(out var prompt)) continue;

            var cmd = Respond(facade, prompt, payTwoLife, ref playedFetch, ref activatedFetch);
            cmd = cmd with { PlayerId = prompt.PlayerId };
            try
            {
                await facade.SubmitAsync(cmd);
            }
            catch (Exception ex)
            {
                wedgeError = ex;
                _out.WriteLine($"SUBMIT REJECTED at step {step}: {cmd.GetType().Name} " +
                    $"for {prompt.PlayerId} (kinds=[{string.Join(",", prompt.ExpectedKinds)}]): {ex.Message}");
                break;
            }
        }

        _out.WriteLine($"played fetch: {playedFetch}; activated fetch: {activatedFetch}; " +
            $"yes/no prompt seen: {yesNoSeen.Task.IsCompleted}");
        _out.WriteLine("prompt kinds seen: " + string.Join(" | ",
            prompts.Select(p => $"{(p.PlayerId == aliceId ? "A" : "B")}:{string.Join(",", p.ExpectedKinds)}")));

        playedFetch.Should().BeTrue("Alice must have been able to play the fetchland");
        activatedFetch.Should().BeTrue("Alice must have been able to activate the fetchland");
        wedgeError.Should().BeNull("fetching a shock land must NOT wedge the game");

        // ── THE BUG: fetching the shock land delivers NO pay-2-life prompt —
        //    the tomb just enters (tapped) and Alice is never asked. This
        //    assertion fails on the broken path.
        yesNoSeen.Task.IsCompleted.Should().BeTrue(
            "fetching 'Overgrown Tomb' must deliver a 'pay 2 life?' yes/no prompt to Alice " +
            "(ExpectedKinds contains ChooseYesNoCommand) — exactly like playing it from hand");

        var ynPrompt = await yesNoSeen.Task;
        ynPrompt.PlayerId.Should().Be(aliceId, "Alice controls the fetched shock land's ETB choice");
        ynPrompt.YesNoView.Should().NotBeNull();

        // ── Answer the yes/no + settle the game so the move + life payment
        //    commit. The shock replacement's pay/decline answer is applied on
        //    an async MoveCardToAsync continuation, so poll until the fetched
        //    tomb both lands on the battlefield AND reaches the expected
        //    tapped/life state rather than racing on a fixed delay.
        var expectedLife = payTwoLife ? aliceStartingLife - 1 - 2 : aliceStartingLife - 1;
        var expectedTapped = !payTwoLife;
        await SettleUntilTombCommitted(
            facade, game, channel, payTwoLife, expectedTapped, expectedLife);

        var tombOnField = facade.Alice.Zones.Battlefield.GetCards()
            .FirstOrDefault(c => c.Name == Tomb);
        tombOnField.Should().NotBeNull("the fetched tomb should have entered the battlefield");

        // The fetch itself always costs 1 life (Pay 1 life). The shock's pay-2
        // is on top of that.
        if (payTwoLife)
        {
            (tombOnField as Permanent)!.IsTapped.Should().BeFalse(
                "paying 2 life → the fetched shock land enters UNTAPPED");
            facade.Alice.LifeTotal.Should().Be(aliceStartingLife - 1 - 2,
                "fetch's 1 life + accepting the pay-2 optional = 3 life total");
        }
        else
        {
            (tombOnField as Permanent)!.IsTapped.Should().BeTrue(
                "declining the pay-2 optional → the fetched shock land enters TAPPED");
            facade.Alice.LifeTotal.Should().Be(aliceStartingLife - 1,
                "only the fetch's 1 life is paid when the pay-2 optional is declined");
        }
    }

    /// <summary>
    /// Drain prompts (answering the pending yes/no with <paramref name="payTwoLife"/>)
    /// until the fetched tomb is on Alice's battlefield with the expected
    /// tapped state + life total, or a deadline elapses. The commit runs on an
    /// async replacement continuation, so a fixed delay races; this polls.
    /// </summary>
    private static async Task SettleUntilTombCommitted(
        GameFacade facade, Task game,
        System.Threading.Channels.Channel<PromptDto> channel, bool payTwoLife,
        bool expectedTapped, int expectedLife)
    {
        bool Committed()
        {
            var tomb = facade.Alice.Zones.Battlefield.GetCards()
                .OfType<Permanent>().FirstOrDefault(c => c.Name == Tomb);
            return tomb != null
                && tomb.IsTapped == expectedTapped
                && facade.Alice.LifeTotal == expectedLife;
        }

        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(15);
        while (DateTime.UtcNow < deadline)
        {
            if (Committed()) { await Task.Delay(20); if (Committed()) return; }
            if (game.IsCompleted) break;

            var read = channel.Reader.WaitToReadAsync().AsTask();
            var winner = await Task.WhenAny(read, game, Task.Delay(200));
            if (winner == game) break;
            if (winner != read || !await read)
            {
                await Task.Delay(20);
                continue;
            }
            if (!channel.Reader.TryRead(out var prompt)) continue;

            var dummyPlayed = true;
            var dummyActivated = true;
            var cmd = Respond(facade, prompt, payTwoLife, ref dummyPlayed, ref dummyActivated);
            cmd = cmd with { PlayerId = prompt.PlayerId };
            try { await facade.SubmitAsync(cmd); }
            catch { /* keep polling — the commit may still land */ }
        }
    }

    // ── helpers ────────────────────────────────────────────────────────────

    private static Land Shell(EmbeddedCardRepository repo, string name)
    {
        var e = repo.GetByName(name)!;
        var parsed = TypeLineParser.Parse(e.TypeLine);
        return new Land(e.Name, parsed.Supertypes, parsed.Subtypes);
    }

    private static GameCommand Respond(
        GameFacade facade, PromptDto prompt, bool payTwoLife,
        ref bool playedFetch, ref bool activatedFetch)
    {
        var kinds = prompt.ExpectedKinds;
        var me = facade.GetState().Players.Single(p => p.Id == prompt.PlayerId);

        if (kinds.Contains(nameof(MulliganCommand)))
            return new MulliganCommand(Keep: true);

        if (kinds.Contains(nameof(ChooseCardsToBottomCommand)))
            return new ChooseCardsToBottomCommand(Array.Empty<Guid>());

        if (kinds.Contains(nameof(ChooseManaCommand)))
            return new ChooseManaCommand(Array.Empty<Guid>());

        if (kinds.Contains(nameof(OrderTriggersCommand)))
            return new OrderTriggersCommand(Array.Empty<Guid>());

        // The fetch's library search → pick the tomb (a legal Swamp/Forest land).
        if (kinds.Contains(nameof(ChooseLibraryPickCommand)))
        {
            var tomb = prompt.Candidates?.FirstOrDefault(c => c.Name == Tomb)
                       ?? prompt.Candidates?.FirstOrDefault();
            return new ChooseLibraryPickCommand(tomb?.InstanceId);
        }

        // The fetched shock land's pay-2-life optional.
        if (kinds.Contains(nameof(ChooseYesNoCommand)))
            return new ChooseYesNoCommand(payTwoLife);

        var isAlice = prompt.PlayerId == facade.Alice.Id;

        // Priority window for Alice: play a fetchland, then activate it.
        if (isAlice && kinds.Contains(nameof(PlayLandCommand)) && !playedFetch)
        {
            var fetch = facade.Alice.Zones.Hand.GetCards().FirstOrDefault(c => c.Name == Fetch);
            if (fetch != null)
            {
                playedFetch = true;
                return new PlayLandCommand(fetch.InstanceId);
            }
        }

        if (isAlice && playedFetch && !activatedFetch
            && kinds.Contains(nameof(ActivateAbilityCommand)))
        {
            var fetchPerm = facade.Alice.Zones.Battlefield.GetCards()
                .OfType<Permanent>()
                .FirstOrDefault(c => c.Name == Fetch && !c.IsTapped);
            var ability = fetchPerm?.Abilities
                .OfType<IActivatedAbility>()
                .FirstOrDefault(a => a is not IManaAbility);
            if (fetchPerm != null && ability != null)
            {
                activatedFetch = true;
                return new ActivateAbilityCommand(fetchPerm.InstanceId, ability.Id);
            }
        }

        return new PassPriorityCommand();
    }
}
