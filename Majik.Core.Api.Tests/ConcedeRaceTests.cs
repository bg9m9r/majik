using FluentAssertions;
using Majik.Core.Api;
using Majik.Core.Api.Commands;
using Majik.Core.Api.Dtos;
using Majik.Core.Cards;
using Majik.Core.Events;
using Majik.Core.Random;
using Xunit;

namespace Majik.Core.Api.Tests;

/// <summary>
/// CR 104.3a — a conceding player leaves the game immediately. Once
/// <see cref="GameFacade.ConcedeAsync"/> is entered no command from either
/// seat is accepted and the engine never acts for the conceder; CR 104.4a —
/// if every player has lost, the game is a draw; a concession that arrives
/// after the game result exists is a no-op.
/// </summary>
public class ConcedeRaceTests
{
    private static RemoteAgent Seat(GameFacade facade, string field) =>
        (RemoteAgent)typeof(GameFacade)
            .GetField(field, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .GetValue(facade)!;

    [Fact]
    public async Task SubmitRacingConcede_NothingAppliedAfterConcedeReturns_RejectedSubmitsNeverApply_NoFault()
    {
        int appliedAfterConcedeReturned = 0, appliedWithoutAcceptance = 0, accepted = 0, rejectedAsOver = 0;
        for (var i = 0; i < 200; i++)
        {
            var deck = Enumerable.Range(0, 10).Select(_ => (ICard)new Land("Forest")).ToList();
            var facade = GameFacade.Create("Alice", "Bob", deck, deck.ToList());
            var land = new Land("Forest");
            land.SetOwner(facade.Alice); land.SetController(facade.Alice);
            facade.Alice.Zones.Hand.AddCard(land); land.SetZone(Majik.Core.Zones.ZoneType.Hand);
            var concedeReturned = false;
            var landEntered = false;
            facade.EventBus.Subscribe<CardMovedEvent>(e =>
            {
                if (!ReferenceEquals(e.Card, land) || e.ToZone != Majik.Core.Zones.ZoneType.Battlefield) return;
                landEntered = true;
                if (Volatile.Read(ref concedeReturned)) Interlocked.Increment(ref appliedAfterConcedeReturned);
            });
            await facade.StartAsync();

            using var go = new ManualResetEventSlim();
            var submitAccepted = false;
            var submit = Task.Run(() =>
            {
                go.Wait();
                try
                {
                    Seat(facade, "_aliceAgent").Submit(new PlayLandCommand(land.InstanceId) { PlayerId = facade.Alice.Id });
                    submitAccepted = true;
                }
                catch (InvalidOperationException ex) when (ex.Message.Contains("game is over"))
                {
                    Interlocked.Increment(ref rejectedAsOver);
                }
            });
            var concede = Task.Run(async () =>
            {
                go.Wait();
                await facade.ConcedeAsync(facade.Alice.Id);
                Volatile.Write(ref concedeReturned, true);
            });
            go.Set();
            await Task.WhenAll(submit, concede).WaitAsync(TimeSpan.FromSeconds(10));
            await Task.Delay(1);

            if (submitAccepted) accepted++;
            if (landEntered && !submitAccepted) appliedWithoutAcceptance++;
            facade.Alice.HasLost.Should().BeTrue();
            // Once ConcedeAsync has been entered, nothing is accepted from either seat.
            foreach (var (seat, player) in new[] { ("_aliceAgent", facade.Alice), ("_bobAgent", facade.Bob) })
            {
                FluentActions.Invoking(() => Seat(facade, seat).Submit(new PassPriorityCommand { PlayerId = player.Id }))
                    .Should().Throw<InvalidOperationException>().WithMessage("*game is over*");
            }
        }
        appliedAfterConcedeReturned.Should().Be(0, "no command is applied after ConcedeAsync returns");
        appliedWithoutAcceptance.Should().Be(0, "a rejected Submit is never applied");
        (accepted + rejectedAsOver).Should().Be(200, "every racing Submit is either accepted before the concession or rejected as game over");
    }
    [Fact]
    public async Task Concede_WhenTheOpponentHasAlsoLost_IsADraw_AndEachLossIsPublishedOnce()
    {
        // Alice mulligans; the "put cards on the bottom" prompt has no safe
        // decline, so a concession cancels it (the facade's cancellation path).
        // CR 104.4a — if Bob has also lost by then, nobody is left: a draw,
        // not a win for a player who has lost.
        var deck = Enumerable.Range(0, 30).Select(_ => (ICard)new Land("Forest")).ToList();
        var facade = GameFacade.Create("Alice", "Bob", deck, deck.ToList());
        var losses = new List<Guid>();
        using var events = facade.Subscribe(e =>
        {
            if (e.Type == nameof(PlayerLostEvent)) lock (losses) losses.Add(e.Payload is { } p ? PlayerOf(p) : Guid.Empty);
        });
        await facade.StartFullGameAsync(firstPlayerSlot: 0, maxTurns: 10, rng: new GameRandom(5));
        var alice = Seat(facade, "_aliceAgent");
        var bob = Seat(facade, "_bobAgent");
        var aliceMulliganed = false;
        for (var step = 0; alice.ExpectedCommandKinds?.Contains(typeof(ChooseCardsToBottomCommand)) != true; step++)
        {
            step.Should().BeLessThan(20, "the bottom prompt should arrive after Alice keeps a mulligan");
            if (alice.ExpectedCommandKinds?.Contains(typeof(MulliganCommand)) == true)
            {
                await facade.SubmitAsync(new MulliganCommand(Keep: aliceMulliganed) { PlayerId = facade.Alice.Id }).WaitAsync(TimeSpan.FromSeconds(10));
                aliceMulliganed = true;
            }
            else if (bob.ExpectedCommandKinds?.Contains(typeof(MulliganCommand)) == true)
            {
                await facade.SubmitAsync(new MulliganCommand(Keep: true) { PlayerId = facade.Bob.Id }).WaitAsync(TimeSpan.FromSeconds(10));
            }
            else
            {
                await Task.Delay(10);
            }
        }

        // Bob loses in the same window (as an SBA would mark him) — after the
        // concession, before the facade fixes the result.
        facade.BeforeGameResultForTest = () => facade.Bob.MarkLost();
        await facade.ConcedeAsync(facade.Alice.Id).WaitAsync(TimeSpan.FromSeconds(10));

        var result = await facade.FullGameTask!.WaitAsync(TimeSpan.FromSeconds(10));
        result.Winner.Should().BeNull("CR 104.4a — every player has lost: the game is a draw");
        losses.Count(id => id == facade.Alice.Id).Should().Be(1);
        losses.GroupBy(id => id).Should().OnlyContain(g => g.Count() == 1, "PlayerLostEvent at most once per player");
    }

    private static Guid PlayerOf(object payload)
    {
        var json = System.Text.Json.JsonSerializer.Serialize(payload);
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        foreach (var name in new[] { "PlayerId", "playerId" })
        {
            if (doc.RootElement.TryGetProperty(name, out var v) && v.TryGetGuid(out var id)) return id;
        }
        return Guid.Empty;
    }

    [Fact]
    public async Task ConcedeAfterTheResultExists_IsANoOp()
    {
        // A two-turn cap ends the game with nobody lost. A concession that
        // lands after the driver returned (here: from inside that window, via
        // the test hook) must not mark the player lost, publish a loss, or
        // change the result or the final snapshot.
        var deck = Enumerable.Range(0, 30).Select(_ => (ICard)new Land("Forest")).ToList();
        var facade = GameFacade.Create("Alice", "Bob", deck, deck.ToList());
        facade.ReplaceAliceAgent(new Majik.Core.Players.Agents.HeuristicBotAgent());
        facade.ReplaceBobAgent(new Majik.Core.Players.Agents.HeuristicBotAgent());
        var losses = 0;
        using var events = facade.Subscribe(e => { if (e.Type == nameof(PlayerLostEvent)) Interlocked.Increment(ref losses); });
        Task? lateConcede = null;
        facade.AfterGameResultForTest = () => lateConcede = facade.ConcedeAsync(facade.Alice.Id);

        await facade.StartFullGameAsync(firstPlayerSlot: 0, maxTurns: 2, rng: new GameRandom(5));
        var result = await facade.FullGameTask!.WaitAsync(TimeSpan.FromSeconds(30));
        await lateConcede!.WaitAsync(TimeSpan.FromSeconds(10));

        result.Winner.Should().BeNull("the turn cap ended the game before the concession");
        facade.Alice.HasLost.Should().BeFalse();
        losses.Should().Be(0);
        (await Task.Run(() => facade.GetStateFor(facade.Alice.Id)!)).Players
            .Single(p => p.Id == facade.Alice.Id).HasLost.Should().BeFalse();
    }
}
