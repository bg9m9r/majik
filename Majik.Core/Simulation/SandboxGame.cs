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
using Majik.Core.StateMachine;
using Majik.Core.Zones;

namespace Majik.Core.Simulation;

/// <summary>
/// A detached, runnable copy of a game for bot search. Clones live state via
/// <see cref="GameStateCloner"/>, then builds the SAME subsystem stack that
/// <see cref="Majik.Core.Api.GameFacade"/> builds (mirrors GameFacade's
/// subsystem-construction block; omits its IO-wiring / SignalR-bridge block)
/// over the cloned players and a FRESH <see cref="EventBus"/> — deliberately
/// omitting the IO subscribers GameFacade adds (BridgeEvent / RemoteAgent /
/// SignalR). Mutations inside the sandbox fire triggers and SBAs (game logic) but
/// never reach any client, and NEVER touch the original live objects.
/// </summary>
/// <remarks>
/// <para><b>Phase-0 fidelity limitations — things the sandbox does NOT mirror perfectly:</b></para>
/// <list type="bullet">
///   <item>
///     <description>
///       <b>Continuous effects do NOT apply.</b> Cloned permanents have
///       <c>ActiveEffects == null</c>, so layer/anthem/lord/CDA effects are
///       invisible — a 2/2 under a +1/+1 anthem evaluates as 2/2 in-sim.
///       Base characteristics only; no computed P/T or granted abilities.
///     </description>
///   </item>
///   <item>
///     <description>
///       <b>Library order is re-shuffled on run.</b>
///       <see cref="GameDriver.RunGameAsync"/> shuffles libraries at start,
///       so a sandbox run does NOT preserve known top-of-library order from
///       the cloned position; bots must not rely on known draws across the
///       clone boundary.
///     </description>
///   </item>
///   <item>
///     <description>
///       <b>Stack abilities are dropped.</b> <see cref="Majik.Core.Stack.Stack.CloneFrom"/>
///       drops activated/triggered abilities (closures can't be remapped);
///       only <see cref="Majik.Core.Spells.Spell"/> objects clone.
///     </description>
///   </item>
///   <item>
///     <description>
///       <b>Several state groups are not cloned</b> (see
///       <c>MutableFieldTripwireTests</c> SKIPPED-DEFER allow-list):
///       MDFC back-face state, face-down intrinsic abilities, battle/saga/class
///       state, player Ring state, and player Replacements.
///     </description>
///   </item>
/// </list>
/// </remarks>
public sealed class SandboxGame
{
    /// <summary>The sandbox-local event bus. No BridgeEvent / IO subscribers.</summary>
    public EventBus Bus { get; }

    /// <summary>The game driver. Call <c>Driver.RunGameAsync</c> to advance the simulation.</summary>
    public GameDriver Driver { get; }

    /// <summary>The cloned game state produced by <see cref="GameStateCloner.Clone"/>.</summary>
    public ClonedGame State { get; }

    /// <summary>
    /// The sandbox's <see cref="LandDropTracker"/> (CR 305.2). Exposed so the
    /// bot search can read per-seat drops-used at a snapshot point (tree-state
    /// reuse) — the tally lives on the driver, not on the players, so a
    /// players-only snapshot cannot carry it otherwise.
    /// </summary>
    public LandDropTracker LandDrops { get; }

    /// <summary>Always false — by construction this sandbox has no IO bridge.</summary>
    public bool HasIoBridge => false;

    private SandboxGame(EventBus bus, GameDriver driver, ClonedGame state, LandDropTracker landDrops)
    {
        Bus = bus;
        Driver = driver;
        State = state;
        LandDrops = landDrops;
    }

    /// <summary>
    /// Build a <see cref="SandboxGame"/> from live player state. Clones every
    /// player and their zones via <see cref="GameStateCloner"/>, wires the SAME
    /// subsystem stack that <c>GameFacade</c> wires (mirrors GameFacade's
    /// subsystem-construction block; omits its IO-wiring / SignalR-bridge block),
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
    ///
    /// <para>
    /// <paramref name="landDropsUsed"/> is optional (tree-state-reuse seam,
    /// CR 305.2): per-seat (by <see cref="Player.Id"/>) land drops already used
    /// in the resumed turn. When supplied, the sandbox's fresh
    /// <see cref="LandDropTracker"/> is seeded for the matching CLONED players
    /// so a restored mid-turn position does not re-offer a land drop the
    /// snapshot's turn already consumed. Null/empty (default) = today's
    /// behaviour: a fresh tally.
    /// </para>
    ///
    /// <para>
    /// <paramref name="cardRepo"/> is optional: when non-null the sandbox's
    /// <see cref="GameDriver"/>/<c>TurnDriver</c> receives the same cast-time
    /// spell-definition resolver shape <c>GameFacade</c> wires (via the shared
    /// <see cref="Majik.Core.CardData.SpellDefinitionResolverFactory"/>), so
    /// in-sim instants/sorceries actually CAST and RESOLVE. When null
    /// (default), the historical behaviour is preserved: TurnDriver's
    /// "no SpellDef for instant/sorcery" branch rotates every non-permanent
    /// spell back into hand.
    /// </para>
    /// </summary>
    public static SandboxGame From(
        IReadOnlyList<Player> livePlayers,
        GameRandom rng,
        Func<Player, IPlayerAgent> agentFactory,
        Majik.Core.Stack.Stack? liveStack = null,
        TurnState? liveTurnState = null,
        Majik.Core.CardData.ICardRepository? cardRepo = null,
        IReadOnlyDictionary<Guid, int>? landDropsUsed = null)
    {
        // --- Clone -----------------------------------------------------------
        var cloned = GameStateCloner.Clone(livePlayers, liveStack, liveTurnState);

        // --- Fresh subsystems (mirrors GameFacade's subsystem-construction block) ---
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

        // CR 305.2 — tree-state-reuse seam: seed the fresh tracker with the
        // per-seat drops already used in the resumed (snapshot) turn, keyed by
        // stable Player.Id onto the CLONED players. Default (null) = fresh
        // tally, byte-identical to before.
        var landDropTracker = new LandDropTracker();
        if (landDropsUsed != null)
        {
            foreach (var clonePlayer in cloned.Players)
            {
                if (landDropsUsed.TryGetValue(clonePlayer.Id, out var used) && used > 0)
                {
                    landDropTracker.SeedDropsUsed(clonePlayer, used);
                }
            }
        }

        // --- GameDriver (mirrors GameFacade's game-driver construction block) ------
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
            // Cast-time spell-definition resolver — same shared factory
            // GameFacade.BuildSpellDefinitionResolver delegates to, built over
            // the SANDBOX's own subsystems so bound definitions register
            // their effects against this sandbox, never the live game.
            // Null cardRepo → null resolver → pre-existing rotate-in-hand
            // behaviour for instants/sorceries.
            spellDefinitionResolver: Majik.Core.CardData.SpellDefinitionResolverFactory.Create(
                cardRepo,
                replacements: replacements,
                effects: continuousEffects,
                triggers: triggers,
                eventBus: bus,
                zones: zones),
            continuousEffects: continuousEffects,
            landDropTracker: landDropTracker);

        return new SandboxGame(bus, driver, cloned, landDropTracker);
    }

    /// <summary>
    /// Resume this sandbox from a cloned mid-game position at
    /// <paramref name="resumePhase"/> without reshuffling libraries or running
    /// mulligans. Delegates to
    /// <see cref="GameDriver.ResumeGameAsync"/>.
    ///
    /// <para><paramref name="activePlayer"/> must be one of the CLONED players
    /// (e.g. obtained via <c>State.PlayerFor(original)</c>).</para>
    /// </summary>
    public Task<GameDriver.GameResult> ResumeAsync(
        PhaseStateType resumePhase,
        Player activePlayer,
        int turnNumber,
        int maxTurns,
        CancellationToken ct = default)
        => Driver.ResumeGameAsync(resumePhase, activePlayer, turnNumber, maxTurns, ct);
}
