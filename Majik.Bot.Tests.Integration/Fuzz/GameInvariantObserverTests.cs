using FluentAssertions;
using Majik.Core.Cards;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Bot.Tests.Integration.Fuzz;

public class GameInvariantObserverTests
{
    private static (Player alice, Player bob, EventBus bus) NewGame()
    {
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);
        var bus = new EventBus();
        return (alice, bob, bus);
    }

    [Fact]
    public void ZoneIntegrity_SameCardInTwoZones_IsFlagged()
    {
        var (alice, bob, bus) = NewGame();
        var observer = new GameInvariantObserver(bus, new[] { alice, bob }, () => 0);

        // Force a corrupt state: one card object present in two zones.
        var card = new Card("Glitch", "");
        card.SetOwner(alice);
        alice.Zones.Battlefield.AddCard(card);
        alice.Zones.Graveyard.AddCard(card);

        observer.RunFinalChecks(turn: 1, phase: "End");

        observer.Violations.Should().Contain(v => v.Kind == "ZoneIntegrity");
    }

    [Fact]
    public void ZoneIntegrity_CleanState_NoViolation()
    {
        var (alice, bob, bus) = NewGame();
        var observer = new GameInvariantObserver(bus, new[] { alice, bob }, () => 0);

        var card = new Card("Clean", "");
        card.SetOwner(alice);
        alice.Zones.Battlefield.AddCard(card);

        observer.RunFinalChecks(turn: 1, phase: "End");

        observer.Violations.Should().NotContain(v => v.Kind == "ZoneIntegrity");
    }

    [Fact]
    public void Result_BothPlayersAlive_AndNoWinner_NotFlaggedUntilCapKnown()
    {
        var (alice, bob, bus) = NewGame();
        var observer = new GameInvariantObserver(bus, new[] { alice, bob }, () => 0);

        // Natural completion with a winner: clean.
        observer.RunFinalChecks(turn: 5, phase: "End", winnerName: "Alice", reachedTurnCap: false);

        observer.Violations.Should().NotContain(v => v.Kind == "SingleResult");
    }

    [Fact]
    public void Result_NoWinner_NotAtCap_IsFlagged()
    {
        var (alice, bob, bus) = NewGame();
        var observer = new GameInvariantObserver(bus, new[] { alice, bob }, () => 0);

        // Game ended with no winner and we did NOT hit the cap → engine ended a game with no result.
        observer.RunFinalChecks(turn: 5, phase: "End", winnerName: null, reachedTurnCap: false);

        observer.Violations.Should().Contain(v => v.Kind == "SingleResult");
    }

    [Fact]
    public void Result_NoWinner_AtCap_FlaggedSuspiciousNotHard()
    {
        var (alice, bob, bus) = NewGame();
        var observer = new GameInvariantObserver(bus, new[] { alice, bob }, () => 0);

        observer.RunFinalChecks(turn: 30, phase: "End", winnerName: null, reachedTurnCap: true);

        observer.Violations.Should().Contain(v => v.Kind == "TurnCapReached");
        observer.Violations.Should().NotContain(v => v.Kind == "SingleResult");
    }
}
