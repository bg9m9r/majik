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
/// LIVE-PLAY integration coverage for a scry land ("Temple of Deceit": enters
/// tapped, "When this land enters, scry 1"). Drives the real
/// <see cref="GameFacade"/> end-to-end through the production binder chain +
/// the full <see cref="StartFullGameAsync"/> GameDriver loop — the SAME path
/// the server (REST + SignalR) runs — exactly like
/// <see cref="SurveilLandLivePlayTests"/>.
///
/// <para>Reported live-play WEDGE: a human-vs-bot match froze (dead clock,
/// "no active prompt") immediately after the human's scry/surveil prompt. Root
/// cause: <c>RemoteAgent.ChooseScryDecisionAsync</c> threw
/// <c>NotImplementedException</c> for the human seat, so ANY scry resolving for
/// a human (the prod binder-chain scry effect awaits it during stack
/// resolution) threw out of the resolution — fail-fast crash in DEBUG, swallowed
/// in Release leaving the priority loop awaiting a never-completing task = the
/// wedge. Surveil was wired (its land test passes); scry was not.</para>
/// </summary>
[Collection(FuzzCollection.Name)] // serial: shares the full-game driver model
public sealed class ScryLandLivePlayTests
{
    private const string Temple = "Temple of Deceit"; // enters tapped, ETB scry 1
    private readonly ITestOutputHelper _out;

    public ScryLandLivePlayTests(ITestOutputHelper output) => _out = output;

    [Fact]
    public void Temple_SeedExists_AndBindsScryTriggerThroughProd()
    {
        var repo = new EmbeddedCardRepository();
        var e = repo.GetByName(Temple);
        e.Should().NotBeNull($"'{Temple}' must be in the embedded Modern pool");
        _out.WriteLine($"TypeLine: {e!.TypeLine}");
        _out.WriteLine($"Oracle:   {e.OracleText}");

        var parsed = TypeLineParser.Parse(e.TypeLine);
        parsed.Types.Should().Contain(CardType.Land);

        var shell = new Land(e.Name, parsed.Supertypes, parsed.Subtypes);
        var facade = GameFacade.Create("Alice", "Bob",
            new ICard[] { shell }, Array.Empty<ICard>(), cardRepo: repo);
        var live = facade.Alice.Zones.GetZone(ZoneType.Library).GetCards()
            .Single(c => c.Name == Temple);

        live.Abilities.OfType<ITriggeredAbility>().Should().NotBeEmpty(
            "the scry ETB trigger must bind through the prod binder chain");
    }

    [Fact]
    public async Task PlayingTemple_DeliversScryPrompt_AndGameStaysAnswerable()
    {
        // ── Build a deterministic deck: lots of temples so Alice draws one
        //    fast, plus basics for a non-empty library (scry needs cards to
        //    peek). Built through the PROD path (cardRepo) so the live binder
        //    chain wires the scry trigger exactly as in production.
        var repo = new EmbeddedCardRepository();

        var aliceDeck = new List<ICard>();
        for (var i = 0; i < 20; i++) aliceDeck.Add(TempleShell(repo));
        for (var i = 0; i < 20; i++) aliceDeck.Add(new Land("Swamp"));

        // Bob: trivial deck; he keeps + auto-passes throughout.
        var bobDeck = new List<ICard>();
        for (var i = 0; i < 40; i++) bobDeck.Add(new Land("Island"));

        const int seed = 12345;
        using var idScope = DeterministicIdScope.Push(new DeterministicIdSource(seed));
        var facade = GameFacade.Create("Alice", "Bob", aliceDeck, bobDeck, cardRepo: repo);

        var aliceId = facade.Alice.Id;

        var prompts = new List<PromptDto>();
        var scrySeen = new TaskCompletionSource<PromptDto>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        var channel = System.Threading.Channels.Channel.CreateUnbounded<PromptDto>();
        using var sub = facade.SubscribePrompts(p =>
        {
            prompts.Add(p);
            channel.Writer.TryWrite(p);
            if (p.ExpectedKinds.Contains(nameof(ChooseScryCommand)))
                scrySeen.TrySetResult(p);
        });

        await facade.StartFullGameAsync(
            maxTurns: 2, rng: new GameRandom(seed), logicalClock: new LogicalClock());
        var game = facade.FullGameTask!;

        var playedTemple = false;
        var wedgeError = (Exception?)null;

        // ── Drive: Alice keeps her opener, plays a temple the moment she can,
        //    then PASSES priority (this is the reported wedge step — the scry
        //    trigger should resolve and prompt, NOT throw/wedge). Bob keeps +
        //    passes. Scry prompt → send the peeked card to the bottom.
        for (var step = 0; step < 400; step++)
        {
            if (game.IsCompleted) break;
            if (scrySeen.Task.IsCompleted && playedTemple) break;

            var read = channel.Reader.WaitToReadAsync().AsTask();
            var winner = await Task.WhenAny(read, game).WaitAsync(TimeSpan.FromSeconds(10));
            if (winner == game) break;
            if (!await read) break;
            if (!channel.Reader.TryRead(out var prompt)) continue;

            var cmd = Respond(facade, prompt, ref playedTemple);
            cmd = cmd with { PlayerId = prompt.PlayerId };
            try
            {
                await facade.SubmitAsync(cmd);
            }
            catch (Exception ex)
            {
                // Characterize a wedge: a rejected/faulted command for the seat
                // that currently has the prompt is the reported symptom.
                wedgeError = ex;
                _out.WriteLine($"SUBMIT REJECTED at step {step}: {cmd.GetType().Name} " +
                    $"for {prompt.PlayerId} (kinds=[{string.Join(",", prompt.ExpectedKinds)}]): {ex.Message}");
                break;
            }
        }

        _out.WriteLine($"played temple: {playedTemple}; scry prompt seen: {scrySeen.Task.IsCompleted}");
        _out.WriteLine("prompt kinds seen: " + string.Join(" | ",
            prompts.Select(p => $"{(p.PlayerId == aliceId ? "A" : "B")}:{string.Join(",", p.ExpectedKinds)}")));

        playedTemple.Should().BeTrue("Alice must have been able to play the temple land");
        wedgeError.Should().BeNull(
            "playing a scry land + passing priority must NOT wedge the game " +
            "(reported live-play bug: the scry effect threw NotImplementedException " +
            "for the human seat, faulting the resolution / wedging the priority loop)");
        scrySeen.Task.IsCompleted.Should().BeTrue(
            "playing 'Temple of Deceit' must deliver a scry prompt to Alice " +
            "(ExpectedKinds contains ChooseScryCommand, ScryView non-null)");

        var scryPrompt = await scrySeen.Task;
        scryPrompt.PlayerId.Should().Be(aliceId, "Alice controls the scry trigger");
        scryPrompt.ScryView.Should().NotBeNull();
        scryPrompt.ScryView!.Should().NotBeEmpty("scry 1 peeks the top card");
    }

    // ── helpers ────────────────────────────────────────────────────────────

    private static Land TempleShell(EmbeddedCardRepository repo)
    {
        var e = repo.GetByName(Temple)!;
        var parsed = TypeLineParser.Parse(e.TypeLine);
        return new Land(e.Name, parsed.Supertypes, parsed.Subtypes);
    }

    private GameCommand Respond(GameFacade facade, PromptDto prompt, ref bool playedTemple)
    {
        var kinds = prompt.ExpectedKinds;
        var me = facade.GetState().Players.Single(p => p.Id == prompt.PlayerId);

        if (kinds.Contains(nameof(MulliganCommand)))
            return new MulliganCommand(Keep: true);

        if (kinds.Contains(nameof(ChooseCardsToBottomCommand)))
            return new ChooseCardsToBottomCommand(Array.Empty<Guid>());

        if (kinds.Contains(nameof(ChooseManaCommand)))
            return new ChooseManaCommand(Array.Empty<Guid>());

        // Order triggers: accept the engine's presented order (empty = as-is).
        if (kinds.Contains(nameof(OrderTriggersCommand)))
            return new OrderTriggersCommand(Array.Empty<Guid>());

        // The scry prompt itself: send the peeked card to the bottom (any legal
        // exact partition is fine; we just need it to resolve).
        if (kinds.Contains(nameof(ChooseScryCommand)))
        {
            var peeked = prompt.ScryView?.Select(c => c.InstanceId).ToList() ?? new List<Guid>();
            return new ChooseScryCommand(peeked, Array.Empty<Guid>());
        }

        // Priority window: Alice plays a temple if she has one and hasn't
        // played a land yet this turn; otherwise everyone passes.
        if (kinds.Contains(nameof(PlayLandCommand)) && !playedTemple)
        {
            var temple = me.Hand.Cards.FirstOrDefault(c => c.Name == Temple);
            if (temple != null)
            {
                playedTemple = true;
                return new PlayLandCommand(temple.InstanceId);
            }
        }

        return new PassPriorityCommand();
    }
}
