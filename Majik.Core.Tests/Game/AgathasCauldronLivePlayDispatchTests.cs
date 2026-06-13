using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Combat;
using Majik.Core.Counters;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Rules;
using Majik.Core.Services;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.Game;

/// <summary>
/// CR 602.2b — regression for the live-match Agatha's Soul Cauldron no-op.
///
/// Driving the Cauldron's "{T}: Exile target card from a graveyard. When a
/// creature card is exiled this way, put a +1/+1 counter on target creature
/// you control" through the REAL TurnDriver dispatch path (RunTurnAsync →
/// DispatchActivate → AbilityActivator → resolution) must place the +1/+1
/// counter on the chosen recipient and imprint the exiled creature.
///
/// The bug: TurnDriver.DispatchActivate flattened all per-request target
/// choices into a single flat <c>targets</c> list and never built the
/// per-request <c>ChosenTargets</c> grouping nor called
/// <see cref="ActivatedAbility.SetChosenTargets"/>. Agatha's effect closure
/// reads <c>tapAbility.ChosenTargets</c> and bails when it is empty, so the
/// whole ability resolved as a no-op (no counter → no granted abilities).
/// </summary>
public class AgathasCauldronLivePlayDispatchTests
{
    private static TurnDriver BuildDriver(
        Player alice, Player bob,
        IPlayerAgent aliceAgent, IPlayerAgent bobAgent,
        EventBus bus,
        out Majik.Core.Stack.Stack stack)
    {
        stack = new Majik.Core.Stack.Stack(bus);
        var zones = new ZoneService(bus);
        var triggers = new TriggerManager(stack, bus);
        var resolver = new StackResolver(bus, zones);
        var sba = new StateBasedActions(bus, zones, triggers);
        var players = new List<Player> { alice, bob };
        var priority = new PriorityManager(players, stack, bus, triggers);

        // Seed minimal libraries so the draw step works.
        foreach (var p in players)
        {
            for (var i = 0; i < 5; i++)
            {
                var c = NamedCardFactory.Create("Mountain", p);
                p.Zones.Library.AddCard(c);
                c.SetZone(ZoneType.Library);
            }
        }

        return new TurnDriver(
            players,
            new Dictionary<Player, IPlayerAgent>
            { [alice] = aliceAgent, [bob] = bobAgent },
            stack, zones, triggers, resolver, sba, priority,
            new CombatFlow(bus, sba),
            continuousEffects: null,
            landDropTracker: new LandDropTracker());
    }

    [Fact]
    public async Task ActivatingCauldronTap_PlacesCounterAndImprints_ThroughLiveDispatch()
    {
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);
        var bus = new EventBus();

        // A creature card sits in Alice's graveyard — the exile + imprint target.
        var deadCreature = new Creature("Dead Bear", "1G", 2, 2);
        deadCreature.SetOwner(alice);
        alice.Zones.Graveyard.AddCard(deadCreature);
        deadCreature.SetZone(ZoneType.Graveyard);

        // The recipient creature Alice controls (gets the +1/+1 counter).
        var recipient = new Creature("Counter Bear", "1G", 2, 2);
        recipient.SetOwner(alice);
        recipient.ChangeController(alice);
        recipient.ClearSummoningSickness();
        alice.Zones.Battlefield.AddCard(recipient);
        recipient.SetZone(ZoneType.Battlefield);

        // The Cauldron itself, in play and untapped.
        var cauldron = AgathasSoulCauldronFactory.Create(alice);
        cauldron.SetZone(ZoneType.Battlefield);
        alice.Zones.Battlefield.AddCard(cauldron);
        var tapAbility = cauldron.Abilities.OfType<ActivatedAbility>().Single();

        var aliceAgent = new ScriptedAgent();
        // Propose activating the Cauldron's {T} ability.
        aliceAgent.QueuePriority(new PriorityAction.ActivateAbility(tapAbility, System.Array.Empty<object>()));
        // Request 0 = the graveyard card to exile; request 1 = the counter recipient.
        aliceAgent.QueueTargets(new object[] { deadCreature });
        aliceAgent.QueueTargets(new object[] { recipient });

        // Alice declares no attackers (the recipient stays back to keep the
        // turn moving without combat side-effects).
        aliceAgent.QueueAttackers(new CombatPlan(
            System.Array.Empty<Majik.Core.Players.Agents.AttackerDeclaration>()));

        var bobAgent = new ScriptedAgent();
        for (var i = 0; i < 200; i++)
        {
            aliceAgent.QueuePriority(PriorityAction.Pass);
            bobAgent.QueuePriority(PriorityAction.Pass);
        }

        var driver = BuildDriver(alice, bob, aliceAgent, bobAgent, bus, out _);

        await driver.RunTurnAsync(alice, turnNumber: 2);

        deadCreature.Zone.Should().Be(ZoneType.Exile,
            "the chosen graveyard creature is exiled by the resolving ability");
        cauldron.ImprintedCards.Should().Contain(deadCreature,
            "an exiled creature card is imprinted on the Cauldron (CR 702.49)");
        recipient.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(1,
            "the chosen recipient gains the +1/+1 counter — the per-request " +
            "ChosenTargets grouping must survive the live dispatch path");
    }
}
