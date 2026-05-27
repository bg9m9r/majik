using System.Text.Json;
using FluentAssertions;
using Majik.Core.Api;
using Majik.Core.Cards;
using Majik.Core.Api.Commands;
using Majik.Core.Api.Dtos;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Api.Tests;

public class GameFacadeTests
{
    [Fact]
    public async Task NewGame_GetState_Returns2PlayerSnapshot()
    {
        var facade = GameFacade.Create("Alice", "Bob", Array.Empty<ICard>(), Array.Empty<ICard>());
        await facade.StartAsync();

        var state = facade.GetState();

        state.Players.Should().HaveCount(2);
        state.Players.Select(p => p.Name).Should().Equal("Alice", "Bob");
        state.GameId.Should().NotBe(Guid.Empty);
        state.ActivePlayerId.Should().Be(state.Players[0].Id);
    }

    [Fact]
    public void ActivePlayerId_BeforeAnyTurn_DefaultsToAlice()
    {
        // CR 103.7 — before the engine has emitted a TurnStartedEvent the
        // facade has no tracked active player. The accessor must fall back
        // to Alice (the creator seat) so the server clock + wire contract
        // always have a non-empty active-player id to derive from.
        var facade = GameFacade.Create("Alice", "Bob", Array.Empty<ICard>(), Array.Empty<ICard>());

        facade.ActivePlayerId.Should().Be(facade.Alice.Id);
    }

    [Fact]
    public async Task PassPriorityCommand_FromBothPlayers_DrainsPriorityRound()
    {
        var facade = GameFacade.Create("Alice", "Bob", Array.Empty<ICard>(), Array.Empty<ICard>());
        await facade.StartAsync();
        var state = facade.GetState();
        var alice = state.Players[0].Id;
        var bob = state.Players[1].Id;

        // Alice has priority first
        await facade.SubmitAsync(new PassPriorityCommand { PlayerId = alice });
        await facade.SubmitAsync(new PassPriorityCommand { PlayerId = bob });

        // Round resolved (stack empty + all passed).
        facade.IsRoundComplete.Should().BeTrue();
    }

    [Fact]
    public async Task Subscribe_DeliversEventDtoForCardMoves()
    {
        var facade = GameFacade.Create("Alice", "Bob", Array.Empty<ICard>(), Array.Empty<ICard>());
        var captured = new List<EventDto>();
        facade.Subscribe(captured.Add);

        await facade.StartAsync();

        captured.Should().NotBeEmpty();
        captured.Any(e => e.Type == nameof(Majik.Core.Domain.DomainEvents.PriorityReceivedEvent))
            .Should().BeTrue();
    }

    [Fact]
    public async Task GetStateFor_AliceViewer_MasksBobsHand_WithPlaceholdersThatPreserveCount()
    {
        // Regression guard for the "Opp hand: 0" portal bug. The per-viewer
        // snapshot (CR 706) must replace each card in the opponent's hand
        // with a "(hidden)" placeholder. Crucially the COUNT must be
        // preserved so the UI can render N face-down cards — the bug
        // was that the masked hand collapsed to length 0 in the wire view.
        var facade = GameFacade.Create("Alice", "Bob", Array.Empty<ICard>(), Array.Empty<ICard>());

        // Seed 7 cards into each player's hand directly (skip the engine's
        // async opening-draw / mulligan to keep this test deterministic).
        for (var i = 0; i < 7; i++)
        {
            SeedHand(facade.Alice, $"AliceSecret-{i}");
            SeedHand(facade.Bob, $"BobSecret-{i}");
        }

        var aliceView = facade.GetStateFor(facade.Alice.Id);
        aliceView.Should().NotBeNull();

        var aliceInView = aliceView!.Players.Single(p => p.Name == "Alice");
        var bobInView = aliceView.Players.Single(p => p.Name == "Bob");

        aliceInView.Hand.Cards.Should().HaveCount(7,
            "viewer always sees the real contents of their OWN hand.");
        aliceInView.Hand.Cards.Select(c => c.Name)
            .Should().BeEquivalentTo(Enumerable.Range(0, 7).Select(i => $"AliceSecret-{i}"));

        bobInView.Hand.Cards.Should().HaveCount(7,
            "CR 706 — opponent hand count must be preserved so the UI can " +
            "render N face-down cards. The bug was that the masked hand " +
            "collapsed to length 0.");
        bobInView.Hand.Cards.Should().OnlyContain(c => c.Name == "(hidden)",
            "every opponent hand card must be a placeholder, never a real name.");
        bobInView.Hand.Cards.Should().OnlyContain(c => c.ManaCost == "",
            "hidden placeholders must not leak mana cost.");
        bobInView.Hand.Cards.Should().OnlyContain(c => c.Types.Count == 0,
            "hidden placeholders must not leak card types.");
    }

    [Fact]
    public void GetStateFor_UnknownViewerId_ReturnsNull()
    {
        var facade = GameFacade.Create("Alice", "Bob", Array.Empty<ICard>(), Array.Empty<ICard>());

        var view = facade.GetStateFor(Guid.NewGuid());

        view.Should().BeNull("an id that matches no seat must surface as null " +
            "so callers (MatchService) can map it to a game-not-started error.");
    }

    private static void SeedHand(Majik.Core.Players.Player player, string name)
    {
        var card = new Card(name, "");
        card.SetOwner(player);
        card.SetZone(ZoneType.Hand);
        player.Zones.Hand.AddCard(card);
    }

    [Fact]
    public async Task SubmitCommand_FromWrongPlayer_Throws()
    {
        var facade = GameFacade.Create("Alice", "Bob", Array.Empty<ICard>(), Array.Empty<ICard>());
        await facade.StartAsync();
        var state = facade.GetState();
        var bob = state.Players[1].Id;

        // Bob doesn't have priority — Alice does.
        var act = async () => await facade.SubmitAsync(new PassPriorityCommand { PlayerId = bob });

        await act.Should().ThrowAsync<InvalidOperationException>();
    }
}
