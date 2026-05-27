using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.Api;
using Majik.Core.CardData;
using Majik.Core.Cards;
using Majik.Core.Combat;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Rules;
using Majik.Core.Services;
using Majik.Core.StateMachine;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Api.Tests;

/// <summary>
/// End-to-end (engine → wire) regression guarding that the two main phases
/// serialize to distinct labels, so the portal's phase indicator can tell
/// pre-combat from post-combat main.
///
/// Drives a real turn through the production <see cref="TurnDriver"/> (the
/// same driver <see cref="Majik.Core.Game.GameDriver"/> runs in the live
/// match) and replays the captured <see cref="StepStartedEvent"/> phase
/// labels. Since Slice 3 each main phase carries its own first-class
/// <see cref="PhaseStateType"/> value (PreCombatMain / PostCombatMain), so
/// the label is authoritative without consulting the turn state.
///
/// CR 505 — the two main phases are distinct steps; clients key on the
/// "PreCombatMain" / "PostCombatMain" wire labels.
/// </summary>
public class PhaseLabelTurnDriverTests
{
    [Fact]
    public async Task TurnDriver_EmitsDistinctPreAndPostCombatMainWireLabels()
    {
        var bus = new EventBus();
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);

        var stack = new Majik.Core.Stack.Stack(bus);
        var zones = new ZoneService(bus);
        var triggers = new TriggerManager(stack, bus);
        var resolver = new StackResolver(bus, zones);
        var sba = new StateBasedActions(bus, zones, triggers);
        var priority = new PriorityManager(new List<Player> { alice, bob }, stack, bus, triggers);

        SeedLibrary(alice, 5);
        SeedLibrary(bob, 5);

        var driver = new TurnDriver(
            players: new[] { alice, bob },
            agents: new Dictionary<Player, IPlayerAgent>
            {
                [alice] = new DeterministicBotAgent(),
                [bob] = new DeterministicBotAgent(),
            },
            stack: stack,
            zoneService: zones,
            triggerManager: triggers,
            stackResolver: resolver,
            stateBasedActions: sba,
            priorityManager: priority,
            combatFlow: new CombatFlow(bus, sba),
            eventBus: bus);

        // Mirror GameFacade's wire-serialization path: since Slice 3 the
        // phase value itself carries the pre/post distinction, so each main
        // StepStartedEvent's label comes straight from its phase. We still
        // track the turn-state (as GameFacade does) but it no longer affects
        // the label. Record the order the two main steps serialize.
        TurnStateType? currentTurnState = null;
        var mainLabels = new List<string>();
        bus.Subscribe<TurnStateChangedEvent>(e => currentTurnState = e.CurrentState);
        bus.Subscribe<StepStartedEvent>(e =>
        {
            if (!e.StepType.IsMain()) return;
            mainLabels.Add(PhaseLabelResolver.Resolve(e.StepType, currentTurnState));
        });

        await driver.RunTurnAsync(alice, turnNumber: 2);

        // A normal turn has exactly two main phases (CR 505.1).
        mainLabels.Should().HaveCount(2);
        mainLabels[0].Should().Be(PhaseLabelResolver.PreCombatMain);
        mainLabels[1].Should().Be(PhaseLabelResolver.PostCombatMain);
    }

    private static void SeedLibrary(Player p, int n)
    {
        for (var i = 0; i < n; i++)
        {
            var c = NamedCardFactory.Create("Mountain", p);
            p.Zones.Library.AddCard(c);
            c.SetZone(ZoneType.Library);
        }
    }
}
