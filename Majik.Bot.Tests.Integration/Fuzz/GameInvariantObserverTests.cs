using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Bot.Tests.Integration.Fuzz;

public class GameInvariantObserverTests
{
    // A minimal event used to drive matching.
    private sealed class PingEvent : GameEvent
    {
        // EventType.Triggered is an existing enum member; the PingCondition
        // gates on `e is PingEvent` so the type value is irrelevant for matching.
        public PingEvent() : base(EventType.Triggered) { }
    }

    // A trigger condition that matches PingEvent.
    private sealed class PingCondition : ITriggerCondition
    {
        public Type EventType => typeof(PingEvent);
        public bool Matches(GameEvent e, ITriggeredAbility ability) => e is PingEvent;
    }

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

    [Fact]
    public void ClassA_TriggerThatShouldFireButDoesnt_IsFlagged()
    {
        var (alice, bob, bus) = NewGame();
        var observer = new GameInvariantObserver(bus, new[] { alice, bob }, () => 0);

        // A card on the battlefield whose ability matches PingEvent and is live there.
        var card = new Card("Pinger", "");
        card.SetOwner(alice);
        card.SetZone(ZoneType.Battlefield);
        alice.Zones.Battlefield.AddCard(card);
        var ability = new TriggeredAbility(
            source: card, controller: alice, condition: new PingCondition(),
            activeZones: new[] { ZoneType.Battlefield });
        card.AddAbility(ability);

        // Publish the event but NEVER publish a TriggeredAbilityTriggeredEvent → simulates a swallowed trigger.
        bus.Publish(new PingEvent());

        observer.RunFinalChecks(turn: 1, phase: "Main", winnerName: "Alice");

        observer.Violations.Should().Contain(v => v.Kind == "OrphanedTrigger" && v.Detail.Contains("Pinger"));
    }

    [Fact]
    public void ClassA_TriggerThatFires_IsClean()
    {
        var (alice, bob, bus) = NewGame();
        var observer = new GameInvariantObserver(bus, new[] { alice, bob }, () => 0);

        var card = new Card("Pinger", "");
        card.SetOwner(alice);
        card.SetZone(ZoneType.Battlefield);
        alice.Zones.Battlefield.AddCard(card);
        var ability = new TriggeredAbility(
            source: card, controller: alice, condition: new PingCondition(),
            activeZones: new[] { ZoneType.Battlefield });
        card.AddAbility(ability);

        var ping = new PingEvent();
        bus.Publish(ping);
        bus.Publish(new TriggeredAbilityTriggeredEvent(ability, ping)); // engine reported the fire

        observer.RunFinalChecks(turn: 1, phase: "Main", winnerName: "Alice");

        observer.Violations.Should().NotContain(v => v.Kind == "OrphanedTrigger");
    }
}
