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
}
