using FluentAssertions;
using Majik.Core.Abilities;
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

namespace Majik.Core.Tests.Game;

/// <summary>
/// CR 506.4 / CR 505.1b — the additional-combat-phase drain loop in
/// <see cref="TurnDriver"/>. A combat-only grant (Combat Celebrant / Fear of
/// Missing Out) re-enters combat; a "followed by an additional main phase"
/// grant (Relentless Assault / World at War) ALSO inserts an extra postcombat
/// main phase before the turn's real postcombat main.
///
/// The tests drive a real turn and record the phase sequence the driver emits
/// (via <see cref="StepStartedEvent"/>), enqueuing the grants onto the live
/// per-game <see cref="AdditionalCombatRegistryProvider"/> queue the moment
/// the first combat begins — exactly where a card's exert/attack trigger would
/// enqueue them.
/// </summary>
public class TurnDriverAdditionalMainPhaseTests
{
    private readonly EventBus _bus = new();
    private readonly Majik.Core.Stack.Stack _stack;
    private readonly ZoneService _zones;
    private readonly TriggerManager _triggers;
    private readonly StackResolver _resolver;
    private readonly StateBasedActions _sba;
    private readonly PriorityManager _priority;
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    public TurnDriverAdditionalMainPhaseTests()
    {
        _stack = new Majik.Core.Stack.Stack(_bus);
        _zones = new ZoneService(_bus);
        _triggers = new TriggerManager(_stack, _bus);
        _resolver = new StackResolver(_bus, _zones);
        _sba = new StateBasedActions(_bus, _zones, _triggers);
        _priority = new PriorityManager(new List<Player> { _alice, _bob }, _stack, _bus, _triggers);
    }

    private TurnDriver NewDriver() => new TurnDriver(
        players: new[] { _alice, _bob },
        agents: new Dictionary<Player, IPlayerAgent>
        {
            [_alice] = new DeterministicBotAgent(),
            [_bob] = new DeterministicBotAgent(),
        },
        stack: _stack,
        zoneService: _zones,
        triggerManager: _triggers,
        stackResolver: _resolver,
        stateBasedActions: _sba,
        priorityManager: _priority,
        combatFlow: new CombatFlow(_bus, _sba),
        eventBus: _bus);

    private static void SeedLibrary(Player p, int n)
    {
        for (var i = 0; i < n; i++)
        {
            var c = NamedCardFactory.Create("Mountain", p);
            p.Zones.Library.AddCard(c);
            c.SetZone(ZoneType.Library);
        }
    }

    [Fact]
    public async Task FollowedByMainGrant_InsertsExtraPostcombatMainAfterExtraCombat()
    {
        using var scope = AdditionalCombatRegistryProvider.PushScope();
        SeedLibrary(_alice, 3);
        SeedLibrary(_bob, 3);

        // Record the postcombat-main + declare-attackers steps the turn emits.
        var steps = new List<StepStateType>();
        var enqueued = false;
        _bus.Subscribe<StepStartedEvent>(e =>
        {
            steps.Add(e.StepType);

            // The moment the FIRST combat's DeclareAttackers begins, enqueue a
            // "combat + following main" grant — exactly where Relentless
            // Assault's resolve / a card's exert trigger would.
            if (e.StepType == StepStateType.DeclareAttackers && !enqueued)
            {
                enqueued = true;
                AdditionalCombatRegistryProvider.Current.EnqueueAdditional(
                    followedByMainPhase: true);
            }
        });

        await NewDriver().RunTurnAsync(_alice, turnNumber: 2);

        // Two DeclareAttackers (the natural combat + the additional combat).
        steps.Count(s => s == StepStateType.DeclareAttackers).Should().Be(2,
            "the additional combat phase re-entered DeclareAttackers (CR 506.4)");

        // Two PostCombatMain phases: the extra main inserted by the grant
        // (CR 505.1b) + the turn's natural postcombat main.
        steps.Count(s => s == StepStateType.PostCombatMain).Should().Be(2,
            "the additional combat is followed by an additional main phase (CR 505.1b)");

        // Sequence order: extra DeclareAttackers → extra PostCombatMain comes
        // BEFORE the turn's End step.
        var extraCombatIdx = steps.LastIndexOf(StepStateType.DeclareAttackers);
        var firstMainAfterExtraCombat = steps.FindIndex(extraCombatIdx, s => s == StepStateType.PostCombatMain);
        firstMainAfterExtraCombat.Should().BeGreaterThan(extraCombatIdx,
            "the additional main phase follows the additional combat");
        steps.IndexOf(StepStateType.End).Should().BeGreaterThan(firstMainAfterExtraCombat,
            "the additional main phase resolves before the turn's End step");
    }

    [Fact]
    public async Task CombatOnlyGrant_InsertsExtraCombatButNoExtraMain()
    {
        using var scope = AdditionalCombatRegistryProvider.PushScope();
        SeedLibrary(_alice, 3);
        SeedLibrary(_bob, 3);

        var steps = new List<StepStateType>();
        var enqueued = false;
        _bus.Subscribe<StepStartedEvent>(e =>
        {
            steps.Add(e.StepType);
            if (e.StepType == StepStateType.DeclareAttackers && !enqueued)
            {
                enqueued = true;
                // Combat Celebrant / Fear of Missing Out — combat only.
                AdditionalCombatRegistryProvider.Current.EnqueueAdditional(
                    followedByMainPhase: false);
            }
        });

        await NewDriver().RunTurnAsync(_alice, turnNumber: 2);

        steps.Count(s => s == StepStateType.DeclareAttackers).Should().Be(2,
            "the additional combat phase re-entered DeclareAttackers");
        steps.Count(s => s == StepStateType.PostCombatMain).Should().Be(1,
            "a combat-only grant adds NO extra main phase (CR 506.4)");
    }
}
