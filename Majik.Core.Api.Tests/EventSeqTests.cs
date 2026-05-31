using FluentAssertions;
using Majik.Core.Api;
using Majik.Core.Api.Commands;
using Majik.Core.Api.Dtos;
using Majik.Core.Cards;
using Xunit;

namespace Majik.Core.Api.Tests;

/// <summary>
/// PLAN 04 — locks the per-game monotonic <c>seq</c> contract on
/// <see cref="EventDto.Seq"/> + <see cref="GameStateDto.Seq"/>. The portal
/// uses this to drop stale snapshots and detect dropped events by
/// contiguity, so the invariants here are part of the wire contract.
/// </summary>
public class EventSeqTests
{
    [Fact]
    public async Task EventSeqs_AreStrictlyIncreasing_AndContiguousFromOne()
    {
        var facade = GameFacade.Create("Alice", "Bob", Array.Empty<ICard>(), Array.Empty<ICard>());
        var captured = new List<EventDto>();
        facade.Subscribe(captured.Add);

        await facade.StartAsync();
        var state = facade.GetState();
        // Drive a few priority passes to emit more events.
        await facade.SubmitAsync(new PassPriorityCommand { PlayerId = state.Players[0].Id });
        await facade.SubmitAsync(new PassPriorityCommand { PlayerId = state.Players[1].Id });

        captured.Should().NotBeEmpty();
        var seqs = captured.Select(e => e.Seq).ToList();

        // Contiguous 1..N — every event draws the next counter value with no
        // gaps and no repeats.
        seqs.Should().Equal(Enumerable.Range(1, captured.Count).Select(i => (long)i));
    }

    [Fact]
    public async Task GetState_Seq_EqualsLastEventSeq_AndDoesNotAdvanceTheCounter()
    {
        var facade = GameFacade.Create("Alice", "Bob", Array.Empty<ICard>(), Array.Empty<ICard>());
        var captured = new List<EventDto>();
        facade.Subscribe(captured.Add);

        await facade.StartAsync();

        var lastEventSeq = captured.Select(e => e.Seq).Max();
        var state = facade.GetState();

        state.Seq.Should().Be(lastEventSeq, "a snapshot reports the seq of the last event folded in");

        // Reading state again returns the same seq (no increment on read).
        var state2 = facade.GetState();
        state2.Seq.Should().Be(lastEventSeq);

        // No new event was emitted by the two GetState calls.
        captured.Select(e => e.Seq).Max().Should().Be(lastEventSeq);
    }

    [Fact]
    public async Task GetStateFor_Seq_MatchesLastEventSeq()
    {
        var facade = GameFacade.Create("Alice", "Bob", Array.Empty<ICard>(), Array.Empty<ICard>());
        var captured = new List<EventDto>();
        facade.Subscribe(captured.Add);

        await facade.StartAsync();

        var lastEventSeq = captured.Select(e => e.Seq).Max();
        var perViewer = facade.GetStateFor(facade.Alice.Id);

        perViewer.Should().NotBeNull();
        perViewer!.Seq.Should().Be(lastEventSeq);
    }

    [Fact]
    public void PerPlayerVariants_OfOneEngineEvent_ShareTheSameSeq()
    {
        // A masked event (a draw: Library→Hand) fans out into per-player
        // variants. All variants of one engine event share EventId AND Seq,
        // so the portal's gap-detect on the public seq never produces a
        // cross-seat false gap. We publish the draw through the facade's bus
        // (the same reflection seam other facade tests use) to force a masked
        // per-player envelope.
        var facade = GameFacade.Create("Alice", "Bob", Array.Empty<ICard>(), Array.Empty<ICard>());
        var envelopes = new List<EventEnvelope>();
        facade.SubscribeEnvelopes(envelopes.Add);

        var alice = facade.Alice;
        var card = new Card("Lightning Bolt", "R");
        card.SetOwner(alice);

        var bus = (Majik.Core.Events.EventBus)typeof(GameFacade)
            .GetField("_bus", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .GetValue(facade)!;
        bus.Publish(new Majik.Core.Events.CardDrawnEvent(card, alice));

        var withVariants = envelopes.Where(env => env.PerPlayer != null).ToList();
        withVariants.Should().NotBeEmpty("a CardDrawnEvent produces masked per-player variants");

        foreach (var env in withVariants)
        {
            foreach (var kv in env.PerPlayer!)
            {
                kv.Value.Seq.Should().Be(env.Public.Seq,
                    "each per-player variant shares the public event's seq");
                kv.Value.EventId.Should().Be(env.Public.EventId);
            }
        }
    }
}
