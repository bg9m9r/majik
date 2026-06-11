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
/// LIVE-PLAY integration coverage for a shock land ("Overgrown Tomb":
/// "As Overgrown Tomb enters, you may pay 2 life. If you don't, it enters
/// tapped."). Drives the real <see cref="GameFacade"/> end-to-end through the
/// production binder chain (cardRepo: <see cref="EmbeddedCardRepository"/>) +
/// the full <see cref="StartFullGameAsync"/> GameDriver loop — the SAME path
/// the server (REST + SignalR) runs.
///
/// <para>Reported live-play bug: playing a shock land from hand entered it
/// UNTAPPED and auto-paid 2 life with NO yes/no prompt — the player never got
/// to choose tapped-vs-pay-2. There was no integration test exercising a real
/// shock land's pay-2 optional through the live loop. This closes that gap and
/// asserts the human agent IS prompted, NO answers → tapped + life unchanged,
/// YES answers → untapped + 2 life paid.</para>
/// </summary>
[Collection(FuzzCollection.Name)] // serial: shares the full-game driver model
public sealed class ShockLandLivePlayTests
{
    private const string Tomb = "Overgrown Tomb";
    private readonly ITestOutputHelper _out;

    public ShockLandLivePlayTests(ITestOutputHelper output) => _out = output;

    [Fact]
    public void Tomb_SeedExists_AndBindsShockReplacementThroughProd()
    {
        var repo = new EmbeddedCardRepository();
        var e = repo.GetByName(Tomb);
        e.Should().NotBeNull($"'{Tomb}' must be in the embedded Modern pool");
        _out.WriteLine($"TypeLine: {e!.TypeLine}");
        _out.WriteLine($"Oracle:   {e.OracleText}");

        var parsed = TypeLineParser.Parse(e.TypeLine);
        parsed.Types.Should().Contain(CardType.Land);
    }

    [Theory]
    [InlineData(false)] // decline → enter tapped, life unchanged
    [InlineData(true)]  // accept  → enter untapped, pay 2 life
    public async Task PlayingTomb_DeliversPay2LifePrompt_AndHonoursTheAnswer(bool payTwoLife)
    {
        // ── Alice's deck: many tombs so she draws one fast, plus basics.
        var repo = new EmbeddedCardRepository();

        var aliceDeck = new List<ICard>();
        for (var i = 0; i < 20; i++) aliceDeck.Add(TombShell(repo));
        for (var i = 0; i < 20; i++) aliceDeck.Add(new Land("Swamp"));

        var bobDeck = new List<ICard>();
        for (var i = 0; i < 40; i++) bobDeck.Add(new Land("Island"));

        const int seed = 24680;
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
            maxTurns: 2, rng: new GameRandom(seed), logicalClock: new LogicalClock());
        var game = facade.FullGameTask!;

        var playedTomb = false;
        Guid? tombId = null;
        var wedgeError = (Exception?)null;

        for (var step = 0; step < 400; step++)
        {
            if (game.IsCompleted) break;
            if (yesNoSeen.Task.IsCompleted && playedTomb) break;

            var read = channel.Reader.WaitToReadAsync().AsTask();
            var winner = await Task.WhenAny(read, game).WaitAsync(TimeSpan.FromSeconds(10));
            if (winner == game) break;
            if (!await read) break;
            if (!channel.Reader.TryRead(out var prompt)) continue;

            var cmd = Respond(facade, prompt, payTwoLife, ref playedTomb, ref tombId);
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

        _out.WriteLine($"played tomb: {playedTomb}; yes/no prompt seen: {yesNoSeen.Task.IsCompleted}");
        _out.WriteLine("prompt kinds seen: " + string.Join(" | ",
            prompts.Select(p => $"{(p.PlayerId == aliceId ? "A" : "B")}:{string.Join(",", p.ExpectedKinds)}")));

        playedTomb.Should().BeTrue("Alice must have been able to play the shock land");
        wedgeError.Should().BeNull("playing a shock land must NOT wedge the game");

        // ── THE BUG: no pay-2-life yes/no prompt is raised; the land auto-pays
        //    2 life and enters untapped. This assertion fails on the broken path.
        yesNoSeen.Task.IsCompleted.Should().BeTrue(
            "playing 'Overgrown Tomb' must deliver a 'pay 2 life?' yes/no prompt to Alice " +
            "(ExpectedKinds contains ChooseYesNoCommand, YesNoView non-null) — the player " +
            "must choose tapped-vs-pay-2, not have it auto-decided");

        var ynPrompt = await yesNoSeen.Task;
        ynPrompt.PlayerId.Should().Be(aliceId, "Alice controls the shock land's ETB choice");
        ynPrompt.YesNoView.Should().NotBeNull();

        // ── Settle the game so the move + life payment commit.
        await DrainToQuiescence(facade, game, channel, payTwoLife);

        var tombOnField = facade.Alice.Zones.Battlefield.GetCards()
            .FirstOrDefault(c => c.Name == Tomb);
        tombOnField.Should().NotBeNull("the tomb should have entered the battlefield");

        if (payTwoLife)
        {
            (tombOnField as Permanent)!.IsTapped.Should().BeFalse(
                "paying 2 life → the shock land enters UNTAPPED");
            facade.Alice.LifeTotal.Should().Be(aliceStartingLife - 2,
                "accepting the pay-2 optional debits exactly 2 life");
        }
        else
        {
            (tombOnField as Permanent)!.IsTapped.Should().BeTrue(
                "declining the pay-2 optional → the shock land enters TAPPED");
            facade.Alice.LifeTotal.Should().Be(aliceStartingLife,
                "declining the pay-2 optional must NOT debit any life");
        }
    }

    // ── helpers ────────────────────────────────────────────────────────────

    private static Land TombShell(EmbeddedCardRepository repo)
    {
        var e = repo.GetByName(Tomb)!;
        var parsed = TypeLineParser.Parse(e.TypeLine);
        return new Land(e.Name, parsed.Supertypes, parsed.Subtypes);
    }

    private static async Task DrainToQuiescence(
        GameFacade facade, Task game,
        System.Threading.Channels.Channel<PromptDto> channel, bool payTwoLife)
    {
        for (var step = 0; step < 200; step++)
        {
            if (game.IsCompleted) break;
            var read = channel.Reader.WaitToReadAsync().AsTask();
            var winner = await Task.WhenAny(read, game).WaitAsync(TimeSpan.FromSeconds(10));
            if (winner == game) break;
            if (!await read) break;
            if (!channel.Reader.TryRead(out var prompt)) continue;

            var dummyPlayed = true;
            Guid? dummyTomb = null;
            var cmd = Respond(facade, prompt, payTwoLife, ref dummyPlayed, ref dummyTomb);
            cmd = cmd with { PlayerId = prompt.PlayerId };
            try { await facade.SubmitAsync(cmd); }
            catch { break; }

            // Stop once we've answered the yes/no and moved on a step or two.
            if (prompt.ExpectedKinds.Contains(nameof(ChooseYesNoCommand)))
            {
                // Give the engine a moment to commit the move.
                await Task.Delay(50);
                break;
            }
        }
        // Let the commit settle.
        await Task.Delay(100);
    }

    private static GameCommand Respond(
        GameFacade facade, PromptDto prompt, bool payTwoLife,
        ref bool playedTomb, ref Guid? tombId)
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

        // The shock land's pay-2-life optional.
        if (kinds.Contains(nameof(ChooseYesNoCommand)))
            return new ChooseYesNoCommand(payTwoLife);

        // Priority window: Alice plays a tomb if she has one + hasn't yet.
        if (kinds.Contains(nameof(PlayLandCommand)) && !playedTomb)
        {
            var tomb = me.Hand.Cards.FirstOrDefault(c => c.Name == Tomb);
            if (tomb != null)
            {
                playedTomb = true;
                tombId = tomb.InstanceId;
                return new PlayLandCommand(tomb.InstanceId);
            }
        }

        return new PassPriorityCommand();
    }
}
