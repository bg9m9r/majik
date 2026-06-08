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
/// Tests for <see cref="TurnDriver.RunTurnFromPhaseAsync"/> — the sim-only
/// resume entry point that skips beginning-phase init and any earlier phases,
/// then runs to end of turn exactly as RunTurnAsync would from that phase onward.
/// Used by the MCTS bot search to re-enter a cloned mid-game position.
/// </summary>
public class TurnDriverResumeTests
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

    public TurnDriverResumeTests()
    {
        _stack = new Majik.Core.Stack.Stack(_bus);
        _zones = new ZoneService(_bus);
        _triggers = new TriggerManager(_stack, _bus);
        _resolver = new StackResolver(_bus, _zones);
        _sba = new StateBasedActions(_bus, _zones, _triggers);
        _priority = new PriorityManager(new List<Player> { _alice, _bob }, _stack, _bus, _triggers);
    }

    private TurnDriver NewDriver()
    {
        return new TurnDriver(
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

    /// <summary>
    /// Resuming at PostCombatMain must skip the combat phase entirely
    /// (no DeclareAttackers step) but still run to end of turn (Cleanup).
    /// </summary>
    [Fact]
    public async Task RunTurnFromPhase_PostCombatMain_SkipsCombat_RunsToEndOfTurn()
    {
        SeedLibrary(_alice, 5);
        SeedLibrary(_bob, 5);
        var phases = new List<StepStateType>();
        _bus.Subscribe<StepStartedEvent>(e => phases.Add(e.StepType));

        var driver = NewDriver();
        await driver.RunTurnFromPhaseAsync(_alice, turnNumber: 3, resumePhase: PhaseStateType.PostCombatMain);

        phases.Should().NotContain(StepStateType.DeclareAttackers,
            "combat was skipped because we resumed at PostCombatMain");
        phases.Should().NotContain(StepStateType.BeginningOfCombat,
            "combat was skipped because we resumed at PostCombatMain");
        phases.Should().Contain(StepStateType.PostCombatMain,
            "post-combat main phase must run");
        phases.Should().Contain(StepStateType.End,
            "end step must run");
        phases.Should().Contain(StepStateType.Cleanup,
            "cleanup step must run — turn ran to completion");
    }

    /// <summary>
    /// Resuming at PreCombatMain must skip beginning-phase steps (Untap/Upkeep/Draw)
    /// but run all remaining phases: PreCombatMain, Combat, PostCombatMain, End, Cleanup.
    /// </summary>
    [Fact]
    public async Task RunTurnFromPhase_PreCombatMain_SkipsBeginning_RunsFromPreCombatMain()
    {
        SeedLibrary(_alice, 5);
        SeedLibrary(_bob, 5);
        var phases = new List<StepStateType>();
        _bus.Subscribe<StepStartedEvent>(e => phases.Add(e.StepType));

        var driver = NewDriver();
        await driver.RunTurnFromPhaseAsync(_alice, turnNumber: 3, resumePhase: PhaseStateType.PreCombatMain);

        phases.Should().NotContain(StepStateType.Untap,
            "beginning phase is skipped on resume");
        phases.Should().NotContain(StepStateType.Upkeep,
            "beginning phase is skipped on resume");
        phases.Should().NotContain(StepStateType.Draw,
            "beginning phase is skipped on resume");
        phases.Should().Contain(StepStateType.PreCombatMain,
            "pre-combat main must run");
        phases.Should().Contain(StepStateType.Cleanup,
            "cleanup step must run — turn ran to completion");
    }

    /// <summary>
    /// Resuming at Combat must skip beginning phase and PreCombatMain,
    /// but still run Combat through to end of turn.
    /// </summary>
    [Fact]
    public async Task RunTurnFromPhase_Combat_SkipsPreCombatMain_RunsCombatOnward()
    {
        SeedLibrary(_alice, 5);
        SeedLibrary(_bob, 5);
        var phases = new List<StepStateType>();
        _bus.Subscribe<StepStartedEvent>(e => phases.Add(e.StepType));

        var driver = NewDriver();
        await driver.RunTurnFromPhaseAsync(_alice, turnNumber: 3, resumePhase: PhaseStateType.Combat);

        phases.Should().NotContain(StepStateType.PreCombatMain,
            "pre-combat main was skipped");
        phases.Should().Contain(StepStateType.BeginningOfCombat,
            "combat phase must run");
        phases.Should().Contain(StepStateType.PostCombatMain,
            "post-combat main must run after combat");
        phases.Should().Contain(StepStateType.Cleanup,
            "cleanup step must run — turn ran to completion");
    }

    /// <summary>
    /// Resuming at TurnEnding (End step) must skip everything up to the end phase.
    /// </summary>
    [Fact]
    public async Task RunTurnFromPhase_TurnEnding_SkipsEverythingElse_RunsEndAndCleanup()
    {
        SeedLibrary(_alice, 5);
        SeedLibrary(_bob, 5);
        var phases = new List<StepStateType>();
        _bus.Subscribe<StepStartedEvent>(e => phases.Add(e.StepType));

        var driver = NewDriver();
        await driver.RunTurnFromPhaseAsync(_alice, turnNumber: 3, resumePhase: PhaseStateType.TurnEnding);

        phases.Should().NotContain(StepStateType.PreCombatMain,
            "pre-combat main was skipped");
        phases.Should().NotContain(StepStateType.BeginningOfCombat,
            "combat was skipped");
        phases.Should().NotContain(StepStateType.PostCombatMain,
            "post-combat main was skipped");
        phases.Should().Contain(StepStateType.End,
            "end step must run");
        phases.Should().Contain(StepStateType.Cleanup,
            "cleanup step must run");
    }

    /// <summary>
    /// Resume does NOT call untap/draw — an existing tapped permanent must
    /// stay tapped (no untap step ran) and library size unchanged (no draw).
    /// </summary>
    [Fact]
    public async Task RunTurnFromPhase_DoesNotUntapOrDraw()
    {
        SeedLibrary(_alice, 5);
        SeedLibrary(_bob, 5);

        // Put a tapped land on the battlefield.
        var mountain = (Permanent)NamedCardFactory.Create("Mountain", _alice);
        mountain.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(mountain);
        mountain.Tap();
        mountain.IsTapped.Should().BeTrue("precondition");

        var libraryBefore = _alice.Zones.Library.Count;

        var driver = NewDriver();
        await driver.RunTurnFromPhaseAsync(_alice, turnNumber: 3, resumePhase: PhaseStateType.PostCombatMain);

        mountain.IsTapped.Should().BeTrue("untap step did not run — permanent remains tapped");
        // Cleanup discards to hand size, but library should be untouched (no draw step).
        _alice.Zones.Library.Count.Should().Be(libraryBefore, "draw step did not run");
    }

    /// <summary>
    /// Resuming at TurnBeginning maps to PreCombatMain (beginning phase is always skipped on resume).
    /// Runs from PreCombatMain to end of turn.
    /// </summary>
    [Fact]
    public async Task RunTurnFromPhase_TurnBeginning_MapsToPreCombatMain()
    {
        SeedLibrary(_alice, 5);
        SeedLibrary(_bob, 5);
        var phases = new List<StepStateType>();
        _bus.Subscribe<StepStartedEvent>(e => phases.Add(e.StepType));

        var driver = NewDriver();
        // TurnBeginning is the beginning phase — on resume it maps to PreCombatMain
        // since the beginning phase init is always skipped.
        await driver.RunTurnFromPhaseAsync(_alice, turnNumber: 3, resumePhase: PhaseStateType.TurnBeginning);

        phases.Should().NotContain(StepStateType.Untap, "beginning phase is always skipped on resume");
        phases.Should().Contain(StepStateType.PreCombatMain, "maps to PreCombatMain");
        phases.Should().Contain(StepStateType.Cleanup, "ran to end of turn");
    }
}
