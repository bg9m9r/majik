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
/// LIVE-PLAY integration coverage for a surveil land ("Underground Mortuary":
/// enters tapped, "When this land enters, surveil 1"). Drives the real
/// <see cref="GameFacade"/> end-to-end through the production binder chain
/// (cardRepo: <see cref="EmbeddedCardRepository"/>) + the full
/// <see cref="StartFullGameAsync"/> GameDriver loop — the SAME path the server
/// (REST + SignalR) runs.
///
/// <para>Reported live-play bug: playing the land produced NO surveil prompt
/// and then wedged the player (a subsequent PassPriority was rejected). There
/// was no integration test exercising a real surveil land through the live loop
/// — this closes that gap and either reproduces the wedge or proves the engine
/// path is correct.</para>
/// </summary>
[Collection(FuzzCollection.Name)] // serial: shares the full-game driver model
public sealed class SurveilLandLivePlayTests
{
    private const string Mortuary = "Underground Mortuary";
    private readonly ITestOutputHelper _out;

    public SurveilLandLivePlayTests(ITestOutputHelper output) => _out = output;

    [Fact]
    public void Mortuary_SeedExists_AndBindsSurveilTriggerThroughProd()
    {
        // Precondition: the card is in the pool, is a land, and its surveil
        // trigger binds through the production binder chain (not a no-op).
        var repo = new EmbeddedCardRepository();
        var e = repo.GetByName(Mortuary);
        e.Should().NotBeNull($"'{Mortuary}' must be in the embedded Modern pool");
        _out.WriteLine($"TypeLine: {e!.TypeLine}");
        _out.WriteLine($"Oracle:   {e.OracleText}");

        var parsed = TypeLineParser.Parse(e.TypeLine);
        parsed.Types.Should().Contain(CardType.Land);

        var shell = new Land(e.Name, parsed.Supertypes, parsed.Subtypes);
        var facade = GameFacade.Create("Alice", "Bob",
            new ICard[] { shell }, Array.Empty<ICard>(), cardRepo: repo);
        var live = facade.Alice.Zones.GetZone(ZoneType.Library).GetCards()
            .Single(c => c.Name == Mortuary);

        live.Abilities.OfType<ITriggeredAbility>().Should().NotBeEmpty(
            "the surveil ETB trigger must bind through the prod binder chain");
    }

    [Fact]
    public async Task PlayingMortuary_DeliversSurveilPrompt_AndGameStaysAnswerable()
    {
        // ── Build a deterministic deck: lots of mortuaries so Alice draws one
        //    fast, plus basics for a non-empty library (surveil needs cards to
        //    peek). Built through the PROD path (cardRepo) so the live binder
        //    chain wires the surveil trigger exactly as in production.
        var repo = new EmbeddedCardRepository();

        var aliceDeck = new List<ICard>();
        for (var i = 0; i < 20; i++) aliceDeck.Add(MortuaryShell(repo));
        for (var i = 0; i < 20; i++) aliceDeck.Add(new Land("Swamp"));

        // Bob: trivial deck; he keeps + auto-passes throughout.
        var bobDeck = new List<ICard>();
        for (var i = 0; i < 40; i++) bobDeck.Add(new Land("Island"));

        const int seed = 12345;
        using var idScope = DeterministicIdScope.Push(new DeterministicIdSource(seed));
        var facade = GameFacade.Create("Alice", "Bob", aliceDeck, bobDeck, cardRepo: repo);

        var aliceId = facade.Alice.Id;

        var prompts = new List<PromptDto>();
        var surveilSeen = new TaskCompletionSource<PromptDto>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        var channel = System.Threading.Channels.Channel.CreateUnbounded<PromptDto>();
        using var sub = facade.SubscribePrompts(p =>
        {
            prompts.Add(p);
            channel.Writer.TryWrite(p);
            if (p.ExpectedKinds.Contains(nameof(ChooseSurveilCommand)))
                surveilSeen.TrySetResult(p);
        });

        await facade.StartFullGameAsync(
            maxTurns: 2, rng: new GameRandom(seed), logicalClock: new LogicalClock());
        var game = facade.FullGameTask!;

        var playedMortuary = false;
        var wedgeError = (Exception?)null;

        // ── Drive: Alice keeps her opener, plays a mortuary the moment she
        //    can, then PASSES priority (this is the reported wedge step — the
        //    surveil trigger should resolve and prompt, NOT reject the pass).
        //    Bob keeps + passes. Surveil prompt → split the peeked card.
        for (var step = 0; step < 400; step++)
        {
            if (game.IsCompleted) break;
            if (surveilSeen.Task.IsCompleted && playedMortuary) break;

            var read = channel.Reader.WaitToReadAsync().AsTask();
            var winner = await Task.WhenAny(read, game).WaitAsync(TimeSpan.FromSeconds(10));
            if (winner == game) break;
            if (!await read) break;
            if (!channel.Reader.TryRead(out var prompt)) continue;

            var cmd = Respond(facade, prompt, ref playedMortuary);
            cmd = cmd with { PlayerId = prompt.PlayerId };
            try
            {
                await facade.SubmitAsync(cmd);
            }
            catch (Exception ex)
            {
                // Characterize a wedge: a rejected command for the seat that
                // currently has the prompt is the reported symptom.
                wedgeError = ex;
                _out.WriteLine($"SUBMIT REJECTED at step {step}: {cmd.GetType().Name} " +
                    $"for {prompt.PlayerId} (kinds=[{string.Join(",", prompt.ExpectedKinds)}]): {ex.Message}");
                break;
            }
        }

        _out.WriteLine($"played mortuary: {playedMortuary}; surveil prompt seen: {surveilSeen.Task.IsCompleted}");
        _out.WriteLine("prompt kinds seen: " + string.Join(" | ",
            prompts.Select(p => $"{(p.PlayerId == aliceId ? "A" : "B")}:{string.Join(",", p.ExpectedKinds)}")));

        playedMortuary.Should().BeTrue("Alice must have been able to play the mortuary land");
        wedgeError.Should().BeNull(
            "playing a surveil land + passing priority must NOT wedge the game " +
            "(reported live-play bug: PassPriority rejected after the surveil land)");
        surveilSeen.Task.IsCompleted.Should().BeTrue(
            "playing 'Underground Mortuary' must deliver a surveil prompt to Alice " +
            "(ExpectedKinds contains ChooseSurveilCommand, SurveilView non-null)");

        var surveilPrompt = await surveilSeen.Task;
        surveilPrompt.PlayerId.Should().Be(aliceId, "Alice controls the surveil trigger");
        surveilPrompt.SurveilView.Should().NotBeNull();
        surveilPrompt.SurveilView!.Should().NotBeEmpty("surveil 1 peeks the top card");
    }

    // ── helpers ────────────────────────────────────────────────────────────

    private static Land MortuaryShell(EmbeddedCardRepository repo)
    {
        var e = repo.GetByName(Mortuary)!;
        var parsed = TypeLineParser.Parse(e.TypeLine);
        return new Land(e.Name, parsed.Supertypes, parsed.Subtypes);
    }

    private GameCommand Respond(GameFacade facade, PromptDto prompt, ref bool playedMortuary)
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

        // The surveil prompt itself: send the peeked card to the graveyard
        // (any legal exact partition is fine; we just need it to resolve).
        if (kinds.Contains(nameof(ChooseSurveilCommand)))
        {
            var peeked = prompt.SurveilView?.Select(c => c.InstanceId).ToList() ?? new List<Guid>();
            return new ChooseSurveilCommand(peeked, Array.Empty<Guid>());
        }

        // Priority window: Alice plays a mortuary if she has one and hasn't
        // played a land yet this turn; otherwise everyone passes.
        if (kinds.Contains(nameof(PlayLandCommand)) && !playedMortuary)
        {
            var mort = me.Hand.Cards.FirstOrDefault(c => c.Name == Mortuary);
            if (mort != null)
            {
                playedMortuary = true;
                return new PlayLandCommand(mort.InstanceId);
            }
        }

        return new PassPriorityCommand();
    }
}
