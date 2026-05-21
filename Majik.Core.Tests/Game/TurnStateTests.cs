using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.Combat;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Rules;
using Majik.Core.Services;
using Majik.Core.Zones;
using Xunit;

/// <summary>
/// Unit and integration tests for <see cref="TurnState"/> — per-turn counters
/// for revolt, connive X, and draw watchers.
/// </summary>
public class TurnStateTests
{
    // -----------------------------------------------------------------------
    // Pure unit tests — TurnState in isolation
    // -----------------------------------------------------------------------

    [Fact]
    public void RecordCreatureDied_IncrementsGlobalAndPerControllerCounters()
    {
        var ts = new TurnState();
        var alice = new Player("Alice", 20);

        ts.RecordCreatureDied(alice);

        ts.CreaturesDiedThisTurn.Should().Be(1);
        ts.CreaturesDiedByController(alice).Should().Be(1);
    }

    [Fact]
    public void RecordPermanentLeftBattlefield_IncrementsGlobalAndPerControllerCounters()
    {
        var ts = new TurnState();
        var bob = new Player("Bob", 20);

        ts.RecordPermanentLeftBattlefield(bob);

        ts.PermanentsLeftBattlefieldThisTurn.Should().Be(1);
        ts.PermanentsLeftByController(bob).Should().Be(1);
    }

    [Fact]
    public void RevoltActive_FalseUntilAPermanentLeaves()
    {
        var ts = new TurnState();
        var alice = new Player("Alice", 20);

        ts.RevoltActive(alice).Should().BeFalse();

        ts.RecordPermanentLeftBattlefield(alice);

        ts.RevoltActive(alice).Should().BeTrue();
    }

    [Fact]
    public void RecordCardDrawn_IncrementsPerPlayerCount()
    {
        var ts = new TurnState();
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);

        ts.RecordCardDrawn(alice);
        ts.RecordCardDrawn(alice);
        ts.RecordCardDrawn(bob);

        ts.CardsDrawnByPlayer(alice).Should().Be(2);
        ts.CardsDrawnByPlayer(bob).Should().Be(1);
    }

    [Fact]
    public void Reset_ClearsAllCounters()
    {
        var ts = new TurnState();
        var alice = new Player("Alice", 20);

        ts.RecordCreatureDied(alice);
        ts.RecordPermanentLeftBattlefield(alice);
        ts.RecordCardDrawn(alice);

        ts.Reset();

        ts.CreaturesDiedThisTurn.Should().Be(0);
        ts.PermanentsLeftBattlefieldThisTurn.Should().Be(0);
        ts.CreaturesDiedByController(alice).Should().Be(0);
        ts.PermanentsLeftByController(alice).Should().Be(0);
        ts.CardsDrawnByPlayer(alice).Should().Be(0);
        ts.RevoltActive(alice).Should().BeFalse();
    }

    [Fact]
    public void RecordCreatureDied_NullController_OnlyIncrementsGlobalCounter()
    {
        var ts = new TurnState();
        var alice = new Player("Alice", 20);

        ts.RecordCreatureDied(null);

        ts.CreaturesDiedThisTurn.Should().Be(1);
        ts.CreaturesDiedByController(alice).Should().Be(0);
    }

    [Fact]
    public void MultipleCreatureDeaths_AccumulateCorrectly()
    {
        var ts = new TurnState();
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);

        ts.RecordCreatureDied(alice);
        ts.RecordCreatureDied(alice);
        ts.RecordCreatureDied(bob);

        ts.CreaturesDiedThisTurn.Should().Be(3);
        ts.CreaturesDiedByController(alice).Should().Be(2);
        ts.CreaturesDiedByController(bob).Should().Be(1);
    }

    // -----------------------------------------------------------------------
    // Integration tests — TurnDriver wires the counters
    // -----------------------------------------------------------------------

    private static (TurnDriver driver, Player alice, Player bob) BuildDriver(IEventBus bus)
    {
        var stack = new Majik.Core.Stack.Stack(bus);
        var zones = new ZoneService(bus);
        var triggers = new TriggerManager(stack, bus);
        var resolver = new StackResolver(bus, zones);
        var sba = new StateBasedActions(bus, zones, triggers);
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);
        var players = new List<Player> { alice, bob };
        var priority = new PriorityManager(players, stack, bus, triggers);

        // Seed minimal libraries so the turn can run through draw step.
        foreach (var p in players)
        {
            for (var i = 0; i < 5; i++)
            {
                var c = NamedCardFactory.Create("Mountain", p);
                p.Zones.Library.AddCard(c);
                c.SetZone(ZoneType.Library);
            }
        }

        var driver = new TurnDriver(
            players,
            new Dictionary<Player, IPlayerAgent>
            {
                [alice] = new DeterministicBotAgent(),
                [bob] = new DeterministicBotAgent()
            },
            stack, zones, triggers, resolver, sba, priority,
            new CombatFlow(bus, sba),
            continuousEffects: null,
            landDropTracker: null,
            eventBus: bus);

        return (driver, alice, bob);
    }

    [Fact]
    public void CreatureDying_IncrementsTurnStateCount()
    {
        var bus = new EventBus();
        var (driver, alice, _) = BuildDriver(bus);

        // Place a bear on Alice's battlefield.
        var bear = new Majik.Core.Cards.Creature("Bear", "1G", 2, 2)
        {
            Owner = alice,
            Zone = ZoneType.Battlefield
        };
        bear.SetController(alice);
        alice.Zones.Battlefield.AddCard(bear);

        // Manually publish a CardMovedEvent (battlefield → graveyard) which
        // is exactly what ZoneService emits when the card leaves.
        bus.Publish(new CardMovedEvent(bear, ZoneType.Battlefield, ZoneType.Graveyard));

        driver.TurnState.CreaturesDiedThisTurn.Should().Be(1);
        driver.TurnState.CreaturesDiedByController(alice).Should().Be(1);
        driver.TurnState.PermanentsLeftBattlefieldThisTurn.Should().Be(1);
        driver.TurnState.PermanentsLeftByController(alice).Should().Be(1);
    }

    [Fact]
    public void NonCreaturePermanentLeaving_DoesNotIncrementCreatureCounter()
    {
        var bus = new EventBus();
        var (driver, alice, _) = BuildDriver(bus);

        var artifact = new Majik.Core.Cards.Artifact("Sword", "2")
        {
            Owner = alice,
            Zone = ZoneType.Battlefield
        };
        artifact.SetController(alice);
        alice.Zones.Battlefield.AddCard(artifact);

        bus.Publish(new CardMovedEvent(artifact, ZoneType.Battlefield, ZoneType.Graveyard));

        driver.TurnState.CreaturesDiedThisTurn.Should().Be(0, "artifacts are not creatures");
        driver.TurnState.PermanentsLeftBattlefieldThisTurn.Should().Be(1);
        driver.TurnState.PermanentsLeftByController(alice).Should().Be(1);
    }

    [Fact]
    public async Task TurnState_ResetsAtTurnBegin()
    {
        var bus = new EventBus();
        var (driver, alice, bob) = BuildDriver(bus);

        // Simulate some events from a "previous turn".
        bus.Publish(new CardMovedEvent(
            new Majik.Core.Cards.Creature("Bear", "1G", 2, 2)
            {
                Owner = alice,
                Zone = ZoneType.Battlefield
            },
            ZoneType.Battlefield,
            ZoneType.Graveyard));

        driver.TurnState.CreaturesDiedThisTurn.Should().Be(1);

        // Now start a fresh turn — TurnDriver.RunTurnAsync calls TurnState.Reset()
        // before processing any steps.
        await driver.RunTurnAsync(bob, turnNumber: 2);

        driver.TurnState.CreaturesDiedThisTurn.Should().Be(0,
            "TurnState.Reset() is called at the start of each RunTurnAsync");
        driver.TurnState.PermanentsLeftBattlefieldThisTurn.Should().Be(0);
        driver.TurnState.RevoltActive(alice).Should().BeFalse();
    }

    [Fact]
    public void CardDrawn_IncrementsTurnStateDrawCounter()
    {
        var bus = new EventBus();
        var (driver, alice, _) = BuildDriver(bus);

        // Simulate a card-draw event.
        var card = new Majik.Core.Cards.Creature("Bear", "1G", 2, 2) { Owner = alice };
        bus.Publish(new CardDrawnEvent(card, alice));

        driver.TurnState.CardsDrawnByPlayer(alice).Should().Be(1);
    }
}
