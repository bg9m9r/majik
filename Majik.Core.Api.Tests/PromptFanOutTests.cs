using FluentAssertions;
using Majik.Core.Api;
using Majik.Core.Api.Commands;
using Majik.Core.Api.Dtos;
using Majik.Core.Cards;
using Xunit;

namespace Majik.Core.Api.Tests;

/// <summary>
/// The <see cref="PromptDto"/> is built once per prompt, before anything can
/// answer it, and every <see cref="GameFacade.SubscribePrompts"/> observer
/// receives that same instance — an observer that answers synchronously (or
/// a fast submitter released by the prompt signal) can't strip the payload
/// from observers that run after it.
/// </summary>
public class PromptFanOutTests
{
    private static RemoteAgent Seat(GameFacade facade, string field) =>
        (RemoteAgent)typeof(GameFacade)
            .GetField(field, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .GetValue(facade)!;

    [Fact]
    public async Task ALaterObserver_GetsTheFullPayload_EvenIfAnEarlierObserverAnsweredSynchronously()
    {
        var deck = Enumerable.Range(0, 20).Select(_ => (ICard)new Land("Forest")).ToList();
        var facade = GameFacade.Create("Alice", "Bob", deck, deck.Select(_ => (ICard)new Land("Mountain")).ToList());
        var land = new Land("Forest");
        land.SetOwner(facade.Alice); land.SetController(facade.Alice);
        facade.Alice.Zones.Hand.AddCard(land); land.SetZone(Majik.Core.Zones.ZoneType.Hand);
        var alice = Seat(facade, "_aliceAgent");

        var answered = 0;
        using var first = facade.SubscribePrompts(p =>
        {
            if (p.PlayerId == facade.Alice.Id && Interlocked.Exchange(ref answered, 1) == 0)
            {
                alice.Submit(new PassPriorityCommand { PlayerId = facade.Alice.Id });
            }
        });
        var seenBySecond = new TaskCompletionSource<PromptDto>(TaskCreationOptions.RunContinuationsAsynchronously);
        PromptDto? seenByFirstObserverToo = null;
        using var second = facade.SubscribePrompts(p =>
        {
            if (p.PlayerId == facade.Alice.Id) seenBySecond.TrySetResult(p);
        });
        using var third = facade.SubscribePrompts(p =>
        {
            if (p.PlayerId == facade.Alice.Id) Interlocked.CompareExchange(ref seenByFirstObserverToo, p, null);
        });

        await facade.StartAsync();

        var prompt = await seenBySecond.Task.WaitAsync(TimeSpan.FromSeconds(10));
        prompt.ExpectedKinds.Should().Contain(nameof(PassPriorityCommand));
        prompt.LegalActions.Should().NotBeNull("the payload is captured before anyone can answer the prompt");
        prompt.LegalActions!.PlayableLandIds.Should().Contain(land.InstanceId);
        seenByFirstObserverToo.Should().BeSameAs(prompt, "every observer receives the one DTO built for the prompt");
        answered.Should().Be(1);
    }
}
