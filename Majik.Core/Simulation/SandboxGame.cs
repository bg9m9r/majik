using Majik.Core.Abilities;
using Majik.Core.Combat;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Random;
using Majik.Core.Rules;
using Majik.Core.Services;
using Majik.Core.Zones;

namespace Majik.Core.Simulation;

/// <summary>
/// A detached, runnable copy of a game for bot search. Clones live state via
/// <see cref="GameStateCloner"/>, then builds the SAME subsystem stack that
/// <see cref="Majik.Core.Api.GameFacade"/> builds (GameFacade.cs:280-286) over
/// the cloned players and a FRESH <see cref="EventBus"/> — deliberately omitting
/// the IO subscribers GameFacade adds at lines 288-312 (BridgeEvent / RemoteAgent /
/// SignalR). Mutations inside the sandbox fire triggers and SBAs (game logic) but
/// never reach any client, and NEVER touch the original live objects.
/// </summary>
public sealed class SandboxGame
{
    /// <summary>The sandbox-local event bus. No BridgeEvent / IO subscribers.</summary>
    public EventBus Bus { get; }

    /// <summary>The game driver. Call <c>Driver.RunGameAsync</c> to advance the simulation.</summary>
    public GameDriver Driver { get; }

    /// <summary>The cloned game state produced by <see cref="GameStateCloner.Clone"/>.</summary>
    public ClonedGame State { get; }

    /// <summary>Always false — by construction this sandbox has no IO bridge.</summary>
    public bool HasIoBridge => false;

    private SandboxGame(EventBus bus, GameDriver driver, ClonedGame state)
    {
        Bus = bus;
        Driver = driver;
        State = state;
    }

    /// <summary>
    /// Build a <see cref="SandboxGame"/> from live player state. Clones every
    /// player and their zones via <see cref="GameStateCloner"/>, wires the SAME
    /// subsystem stack that <c>GameFacade</c> wires (mirroring GameFacade.cs:280-286),
    /// and returns a fully constructed, runnable sandbox.
    ///
    /// <para>
    /// <paramref name="agentFactory"/> is called once per cloned player to supply
    /// the agent that will drive that seat. The factory receives the CLONED player
    /// so agents that need to inspect hand / library work against the sandbox copy.
    /// </para>
    ///
    /// <para>
    /// <paramref name="liveStack"/> and <paramref name="liveTurnState"/> are
    /// optional: pass them to snapshot a mid-game position. For a fresh-start
    /// simulation (the typical bot-search case) leave both null.
    /// </para>
    /// </summary>
    public static SandboxGame From(
        IReadOnlyList<Player> livePlayers,
        GameRandom rng,
        Func<Player, IPlayerAgent> agentFactory,
        Majik.Core.Stack.Stack? liveStack = null,
        TurnState? liveTurnState = null)
    {
        // --- Clone -----------------------------------------------------------
        var cloned = GameStateCloner.Clone(livePlayers, liveStack, liveTurnState);

        // --- Fresh subsystems (mirror GameFacade.cs:280-286) -----------------
        // A brand-new EventBus: no BridgeEvent subscription, no SignalR wiring.
        var bus = new EventBus();
        var replacements = new ReplacementBus();

        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);
        var zones = new ZoneService(bus, replacements);
        var sba = new StateBasedActions(bus, zones, triggers);
        var resolver = new StackResolver(bus, zones, sba);
        var priority = new PriorityManager(cloned.Players.ToList(), stack, bus, triggers);
        var combatFlow = new CombatFlow(bus, sba, replacements);

        // Fresh ContinuousEffectsService scoped to the sandbox players.
        var continuousEffects = new ContinuousEffectsService(bus);
        continuousEffects.PlayersProvider = () => cloned.Players;

        // Build agent map keyed on the CLONED players.
        var agents = new Dictionary<Player, IPlayerAgent>(cloned.Players.Count);
        foreach (var clonePlayer in cloned.Players)
        {
            agents[clonePlayer] = agentFactory(clonePlayer);
        }

        // --- GameDriver (mirror GameFacade.StartFullGameAsync:1053-1088) ------
        var driver = new GameDriver(
            players: cloned.Players,
            agents: agents,
            stack: stack,
            zoneService: zones,
            triggerManager: triggers,
            stackResolver: resolver,
            stateBasedActions: sba,
            priorityManager: priority,
            combatFlow: combatFlow,
            rng: rng,
            eventBus: bus,
            continuousEffects: continuousEffects,
            landDropTracker: new LandDropTracker());

        return new SandboxGame(bus, driver, cloned);
    }
}
