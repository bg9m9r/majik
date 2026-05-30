using Majik.Core.Combat;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Rules;
using Majik.Core.Services;
using Majik.Core.StateMachine;

namespace Majik.Core.Domain.Aggregates;

/// <summary>
/// Pre-composed service graph consumed by <see cref="Game"/>.
///
/// Acts as the composition seam: tests and the API layer can build a custom
/// graph (e.g. swapping the event bus or stubbing managers) without forcing
/// the aggregate to know how its dependencies are constructed.
/// </summary>
public sealed class GameDependencies
{
    public IEventBus EventBus { get; }
    public GameStateMachine StateMachine { get; }
    public GameService GameService { get; }
    public PlayerService PlayerService { get; }
    public ZoneService ZoneService { get; }
    public PhaseManager PhaseManager { get; }
    public StateBasedActions StateBasedActions { get; }
    public StackResolver StackResolver { get; }
    public CombatManager CombatManager { get; }
    public ContinuousEffectsService ContinuousEffects { get; }

    public GameDependencies(
        IEventBus eventBus,
        GameStateMachine stateMachine,
        GameService gameService,
        PlayerService playerService,
        ZoneService zoneService,
        PhaseManager phaseManager,
        StateBasedActions stateBasedActions,
        StackResolver stackResolver,
        CombatManager combatManager,
        ContinuousEffectsService continuousEffects)
    {
        EventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
        StateMachine = stateMachine ?? throw new ArgumentNullException(nameof(stateMachine));
        GameService = gameService ?? throw new ArgumentNullException(nameof(gameService));
        PlayerService = playerService ?? throw new ArgumentNullException(nameof(playerService));
        ZoneService = zoneService ?? throw new ArgumentNullException(nameof(zoneService));
        PhaseManager = phaseManager ?? throw new ArgumentNullException(nameof(phaseManager));
        StateBasedActions = stateBasedActions ?? throw new ArgumentNullException(nameof(stateBasedActions));
        StackResolver = stackResolver ?? throw new ArgumentNullException(nameof(stackResolver));
        CombatManager = combatManager ?? throw new ArgumentNullException(nameof(combatManager));
        ContinuousEffects = continuousEffects ?? throw new ArgumentNullException(nameof(continuousEffects));

        PhaseManager.SetCombatManager(CombatManager);
    }

    /// <summary>
    /// Build the standard service graph used by production code.
    /// Pass an existing <paramref name="eventBus"/> when callers want to share
    /// the bus (e.g. an outer subscriber registered before the game starts).
    /// </summary>
    public static GameDependencies CreateDefault(IEventBus? eventBus = null)
    {
        var bus = eventBus ?? new EventBus();
        var zoneService = new ZoneService(bus);
        var stateBasedActions = new StateBasedActions(bus, zoneService);
        // Wire the bus so the layer-system memoization cache invalidates on
        // external CDA inputs (graveyard contents, life totals, control,
        // artifact counts — all of which ride game events).
        var continuousEffects = new ContinuousEffectsService(bus);
        var combatManager = new CombatManager(bus, stateBasedActions, zoneService, continuousEffects);

        return new GameDependencies(
            eventBus: bus,
            stateMachine: new GameStateMachine(bus),
            gameService: new GameService(bus),
            playerService: new PlayerService(bus),
            zoneService: zoneService,
            phaseManager: new PhaseManager(bus),
            stateBasedActions: stateBasedActions,
            stackResolver: new StackResolver(bus, zoneService, stateBasedActions),
            combatManager: combatManager,
            continuousEffects: continuousEffects);
    }
}
