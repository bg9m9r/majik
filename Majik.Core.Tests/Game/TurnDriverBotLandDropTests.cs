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
/// CR 305.2 — a player may play at most one land per turn (unless an
/// extra-land effect bumps the cap). Regression for a bug where a
/// <see cref="HeuristicBotAgent"/> driven through <see cref="TurnDriver"/>
/// played multiple lands in one main phase because the
/// <see cref="PriorityLoop"/> the driver constructed each priority round
/// was not given the turn's <see cref="LandDropTracker"/>. Without that
/// wiring, <c>PriorityLoop.ApplyActionAsync</c>'s <c>PlayLand</c> branch
/// skipped both the <c>CanPlayLand</c> gate and the
/// <c>RecordLandPlayed</c> counter increment — every <c>PriorityAction.PlayLand</c>
/// the bot proposed simply succeeded.
/// </summary>
public class TurnDriverBotLandDropTests
{
    [Fact]
    public async Task HeuristicBot_WithMultipleLandsInHand_PlaysAtMostOnePerTurn()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var zones = new ZoneService(bus);
        var triggers = new TriggerManager(stack, bus);
        var resolver = new StackResolver(bus, zones);
        var sba = new StateBasedActions(bus, zones, triggers);
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);
        var players = new List<Player> { alice, bob };
        var priority = new PriorityManager(players, stack, bus, triggers);
        var tracker = new LandDropTracker();

        // Two basic lands in Alice's hand — bot will iterate over them.
        var l1 = NamedCardFactory.Create("Mountain", alice);
        var l2 = NamedCardFactory.Create("Mountain", alice);
        l1.SetZone(ZoneType.Hand); l2.SetZone(ZoneType.Hand);
        alice.Zones.Hand.AddCard(l1);
        alice.Zones.Hand.AddCard(l2);

        // Minimal libraries so the draw step + cleanup don't crash.
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
                [alice] = new HeuristicBotAgent(),
                [bob] = new DeterministicBotAgent(),
            },
            stack, zones, triggers, resolver, sba, priority,
            new CombatFlow(bus, sba),
            continuousEffects: null,
            landDropTracker: tracker);

        await driver.RunTurnAsync(alice, turnNumber: 2);

        // CR 305.2 — only one of the two lands should have actually
        // entered the battlefield. The other stays in hand.
        var landsOnField = alice.Zones.Battlefield.GetCards()
            .Count(c => c.HasType(Majik.Core.Cards.Types.CardType.Land));
        landsOnField.Should().Be(1, "CR 305.2 caps a player at one land drop per turn");
        tracker.DropsUsedThisTurn(alice).Should().Be(1);
    }

    [Fact]
    public async Task HeuristicBot_LandCap_ResetsOnNextTurn()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var zones = new ZoneService(bus);
        var triggers = new TriggerManager(stack, bus);
        var resolver = new StackResolver(bus, zones);
        var sba = new StateBasedActions(bus, zones, triggers);
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);
        var players = new List<Player> { alice, bob };
        var priority = new PriorityManager(players, stack, bus, triggers);
        var tracker = new LandDropTracker();

        // Two lands in hand → bot plays one per turn over two turns.
        var l1 = NamedCardFactory.Create("Mountain", alice);
        var l2 = NamedCardFactory.Create("Mountain", alice);
        l1.SetZone(ZoneType.Hand); l2.SetZone(ZoneType.Hand);
        alice.Zones.Hand.AddCard(l1);
        alice.Zones.Hand.AddCard(l2);

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
                [alice] = new HeuristicBotAgent(),
                [bob] = new DeterministicBotAgent(),
            },
            stack, zones, triggers, resolver, sba, priority,
            new CombatFlow(bus, sba),
            continuousEffects: null,
            landDropTracker: tracker);

        await driver.RunTurnAsync(alice, turnNumber: 2);
        var afterTurn1 = alice.Zones.Battlefield.GetCards()
            .Count(c => c.HasType(Majik.Core.Cards.Types.CardType.Land));
        tracker.DropsUsedThisTurn(alice).Should().Be(1);

        await driver.RunTurnAsync(alice, turnNumber: 3);
        var afterTurn2 = alice.Zones.Battlefield.GetCards()
            .Count(c => c.HasType(Majik.Core.Cards.Types.CardType.Land));

        // CR 305.2 — second turn the cap resets so the second land lands.
        afterTurn1.Should().Be(1);
        afterTurn2.Should().Be(2);
    }

    /// <summary>
    /// CR 305.2 modified by extra-land effects (Azusa, Lost but Seeking;
    /// Exploration): bumping <see cref="LandDropTracker.SetMaxLandDropsThisTurn"/>
    /// must let the bot play more lands in that turn. Guards against the
    /// fix over-correcting by hard-capping at one.
    /// </summary>
    [Fact]
    public async Task HeuristicBot_WithExtraLandDropsThisTurn_PlaysUpToCap()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var zones = new ZoneService(bus);
        var triggers = new TriggerManager(stack, bus);
        var resolver = new StackResolver(bus, zones);
        var sba = new StateBasedActions(bus, zones, triggers);
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);
        var players = new List<Player> { alice, bob };
        var priority = new PriorityManager(players, stack, bus, triggers);
        var tracker = new LandDropTracker();

        var l1 = NamedCardFactory.Create("Mountain", alice);
        var l2 = NamedCardFactory.Create("Mountain", alice);
        var l3 = NamedCardFactory.Create("Mountain", alice);
        foreach (var l in new[] { l1, l2, l3 })
        {
            l.SetZone(ZoneType.Hand);
            alice.Zones.Hand.AddCard(l);
        }

        foreach (var p in players)
        {
            for (var i = 0; i < 5; i++)
            {
                var c = NamedCardFactory.Create("Mountain", p);
                p.Zones.Library.AddCard(c);
                c.SetZone(ZoneType.Library);
            }
        }

        // Azusa-style effect: bump the per-turn cap each turn. TurnDriver
        // publishes TurnStartedEvent BEFORE calling LandDropTracker.ResetTurn,
        // so subscribing to that event would have the reset clobber the bump.
        // Subscribing to the first StepStartedEvent (Untap) fires after the
        // reset, which mirrors how a real continuous static ability would
        // re-apply its effect after turn-state mutation.
        bus.Subscribe<StepStartedEvent>(e =>
        {
            if (ReferenceEquals(e.Player, alice)
                && e.StepType == Majik.Core.StateMachine.PhaseStateType.Untap)
            {
                tracker.SetMaxLandDropsThisTurn(alice, 3);
            }
        });

        var driver = new TurnDriver(
            players,
            new Dictionary<Player, IPlayerAgent>
            {
                [alice] = new HeuristicBotAgent(),
                [bob] = new DeterministicBotAgent(),
            },
            stack, zones, triggers, resolver, sba, priority,
            new CombatFlow(bus, sba),
            continuousEffects: null,
            eventBus: bus,
            landDropTracker: tracker);

        await driver.RunTurnAsync(alice, turnNumber: 2);

        var landsOnField = alice.Zones.Battlefield.GetCards()
            .Count(c => c.HasType(Majik.Core.Cards.Types.CardType.Land));
        landsOnField.Should().Be(3, "Azusa-style cap=3 should let the bot drop all three lands");
        tracker.DropsUsedThisTurn(alice).Should().Be(3);
    }
}
