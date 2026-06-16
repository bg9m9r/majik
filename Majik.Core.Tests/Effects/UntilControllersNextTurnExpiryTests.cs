using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.Combat;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Rules;
using Majik.Core.Services;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

/// <summary>
/// CR 514.2 — "until your next turn" controller-keyed duration. Proves the
/// effect survives the cleanup of the turn it was created on AND the whole
/// intervening opponent turn, then ends precisely at its controller's next
/// untap step — the behaviour that distinguishes it from "until end of turn".
/// </summary>
public class UntilControllersNextTurnExpiryTests
{
    private static (TurnDriver driver, ContinuousEffectsService continuous,
        Player alice, Player bob, EventBus bus) BuildGame()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var zones = new ZoneService(bus);
        var triggers = new Majik.Core.Abilities.TriggerManager(stack, bus);
        var resolver = new StackResolver(bus, zones);
        var sba = new StateBasedActions(bus, zones, triggers);
        var continuous = new ContinuousEffectsService(bus);
        var combat = new CombatFlow(bus, sba);

        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);
        foreach (var p in new[] { alice, bob })
        {
            for (var i = 0; i < 10; i++)
            {
                var c = NamedCardFactory.Create("Mountain", p);
                p.Zones.Library.AddCard(c); c.SetZone(ZoneType.Library);
            }
        }

        var players = new List<Player> { alice, bob };
        var priorityMgr = new PriorityManager(players, stack, bus, triggers);
        var driver = new TurnDriver(
            players,
            new Dictionary<Player, IPlayerAgent>
            {
                [alice] = new DeterministicBotAgent(),
                [bob] = new DeterministicBotAgent(),
            },
            stack, zones, triggers, resolver, sba, priorityMgr, combat,
            continuous);

        return (driver, continuous, alice, bob, bus);
    }

    [Fact]
    public async Task Pump_PersistsThroughOpponentTurn_EndsAtControllersNextUntap()
    {
        var (driver, continuous, alice, bob, _) = BuildGame();

        var bear = new Creature("Bear", "1G", 2, 2)
        {
            Owner = alice, Controller = alice, Zone = ZoneType.Battlefield,
            ActiveEffects = continuous,
        };
        alice.Zones.Battlefield.AddCard(bear);
        bear.HasSummoningSickness = false;

        // Alice's turn (T2): Liliana-style "-2/-1 until your next turn".
        // RunTurnAsync fires TurnStartedEvent → continuous.CurrentTurnNumber = 2,
        // so the effect is stamped CreatedOnTurn = 2 inside this turn.
        // Register the effect as if inside Alice's turn 2 (after the untap step
        // that already elapsed). Stamp the turn number so the gate matches the
        // live run's TurnStartedEvent.
        continuous.CurrentTurnNumber = 2;
        continuous.Register(new PumpUntilControllersNextTurnEffect(bear, -2, -1, alice));
        bear.Power.Should().Be(0, "the -2/-1 is live during Alice's turn");
        bear.Toughness.Should().Be(1);

        // Run Alice's turn 2 to completion — the end-of-turn cleanup must NOT
        // drop this effect (it is NOT until-end-of-turn).
        await driver.RunTurnAsync(alice, turnNumber: 2);
        bear.Power.Should().Be(0, "until-your-next-turn survives its creation turn's cleanup");
        bear.Toughness.Should().Be(1);

        // Bob's turn (T3) — the opponent turn between. Still must persist.
        await driver.RunTurnAsync(bob, turnNumber: 3);
        bear.Power.Should().Be(0, "until-your-next-turn survives the intervening opponent turn");
        bear.Toughness.Should().Be(1);

        // Alice's NEXT turn (T4) — the untap step ends the effect.
        await driver.RunTurnAsync(alice, turnNumber: 4);
        bear.Power.Should().Be(2, "the effect ends at the controller's next untap step");
        bear.Toughness.Should().Be(2);
    }

    [Fact]
    public async Task EndOfTurnSweep_DoesNotDrop_ControllerKeyedEffect()
    {
        // Direct contrast against the EOT sweep: the controller-keyed flavour
        // must be untouched by ExpireEndOfTurn (would otherwise wear off a full
        // turn early).
        var (_, continuous, alice, _, _) = BuildGame();

        var bear = new Creature("Bear", "1G", 2, 2)
        {
            Owner = alice, Controller = alice, Zone = ZoneType.Battlefield,
            ActiveEffects = continuous,
        };
        alice.Zones.Battlefield.AddCard(bear);

        continuous.CurrentTurnNumber = 2;
        continuous.Register(new PumpUntilControllersNextTurnEffect(bear, -2, -1, alice));
        bear.Power.Should().Be(0);

        continuous.ExpireEndOfTurn();
        bear.Power.Should().Be(0, "EOT sweep ignores ExpiresAtControllersNextTurn effects");
    }

    [Fact]
    public void SameTurnUntap_DoesNotDropEffect()
    {
        // The controller's untap on the SAME turn the effect was created must
        // be a no-op (the untap already elapsed before the effect resolved).
        var (_, continuous, alice, _, _) = BuildGame();

        var bear = new Creature("Bear", "1G", 2, 2)
        {
            Owner = alice, Controller = alice, Zone = ZoneType.Battlefield,
            ActiveEffects = continuous,
        };
        alice.Zones.Battlefield.AddCard(bear);

        continuous.CurrentTurnNumber = 5;
        continuous.Register(new PumpUntilControllersNextTurnEffect(bear, -2, -1, alice));

        // Same turn number → skip.
        continuous.ExpireAtControllersNextUntap(alice, turnNumber: 5);
        bear.Power.Should().Be(0, "the creation turn's untap does not end the effect");

        // Controller's NEXT turn → drop.
        continuous.ExpireAtControllersNextUntap(alice, turnNumber: 7);
        bear.Power.Should().Be(2, "the controller's next untap ends it");
    }

    [Fact]
    public void OpponentUntap_DoesNotDropEffect()
    {
        var (_, continuous, alice, bob, _) = BuildGame();

        var bear = new Creature("Bear", "1G", 2, 2)
        {
            Owner = alice, Controller = alice, Zone = ZoneType.Battlefield,
            ActiveEffects = continuous,
        };
        alice.Zones.Battlefield.AddCard(bear);

        continuous.CurrentTurnNumber = 2;
        continuous.Register(new PumpUntilControllersNextTurnEffect(bear, -2, -1, alice));

        // Bob's untap (different controller) must not end an Alice-keyed effect.
        continuous.ExpireAtControllersNextUntap(bob, turnNumber: 3);
        bear.Power.Should().Be(0, "only the EXPIRY controller's untap ends the effect");
    }
}
