using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.StateMachine;
using Majik.Core.Zones;

namespace Majik.Core.Game;

/// <summary>
/// Async driver for one priority "round" (Rule 117): each player in APNAP
/// order is given priority until all pass in succession. If the stack has
/// objects when all pass, the top resolves and the round restarts. If the
/// stack is empty, the round ends.
///
/// Stays out of <see cref="PriorityManager"/> proper to keep that class
/// agent-free; this orchestrator owns the await loop.
/// </summary>
public sealed class PriorityLoop
{
    private readonly IReadOnlyList<Player> _players;
    private readonly PriorityManager _priority;
    private readonly Majik.Core.Stack.Stack _stack;
    private readonly StackResolver _stackResolver;
    private readonly ZoneService _zoneService;
    private readonly IReadOnlyDictionary<Player, IPlayerAgent> _agents;
    private readonly Func<int> _turnNumberAccessor;
    private readonly Func<PhaseStateType?> _phaseAccessor;
    private readonly LandDropTracker _landDropTracker;
    private readonly Func<Player, PriorityAction.CastSpell, GameContext, Task>? _castDispatcher;
    private readonly Func<Player, PriorityAction.ActivateAbility, GameContext, Task>? _activateDispatcher;
    private readonly Action<Player, PriorityAction.ActivateManaAbility>? _manaAbilityDispatcher;
    // Slice 5a — server-side auto-pass plumbing. All three are optional;
    // when null the loop falls back to its pre-Slice-5a behaviour
    // (always prompt the agent). Wired by GameFacade for the server-side
    // human-portal path; left null in unit tests / bot-vs-bot harnesses.
    private readonly Func<Player, IAutoPassPrefsView?>? _autoPassPrefsProvider;
    // True when the engine has narrowed the priority window to PASS-ONLY
    // (no lands, no castable spells, no activatable abilities). Built by
    // the upper Majik.Core.Api layer (PriorityKinds.IsPassOnly fed with
    // PriorityKinds.Build), passed in as a predicate to keep PriorityLoop
    // unaware of the wire command types.
    private readonly Func<GameContext, bool>? _isPassOnlyDeadWindow;
    private readonly Func<DateTime> _clock;
    // Stack-mutation event subscriptions held so the loop can detach on
    // its way out. TurnDriver constructs a fresh PriorityLoop per round
    // and the bus outlives every one of them — without unsubscribe,
    // every priority round would accumulate two handlers on the bus.
    private readonly IEventBus? _eventBus;
    private readonly Action<StackObjectAddedEvent>? _onStackAdded;
    private readonly Action<StackObjectResolvedEvent>? _onStackResolved;
    // Wall-clock timestamp of the most recent stack mutation seen by
    // this loop (additions OR resolutions — anything that visibly
    // mutates the stack from the player's POV). Initialised to
    // DateTime.MinValue so the first dead window doesn't accidentally
    // sit inside the display window at game start.
    private DateTime _lastStackMutatedAt = DateTime.MinValue;
    private Player? _activePlayer;

    public PriorityLoop(
        IReadOnlyList<Player> players,
        PriorityManager priority,
        Majik.Core.Stack.Stack stack,
        StackResolver stackResolver,
        ZoneService zoneService,
        IReadOnlyDictionary<Player, IPlayerAgent> agents,
        Func<int> turnNumberAccessor,
        Func<PhaseStateType?> phaseAccessor,
        LandDropTracker landDropTracker,
        Func<Player, PriorityAction.CastSpell, GameContext, Task>? castDispatcher = null,
        Func<Player, PriorityAction.ActivateAbility, GameContext, Task>? activateDispatcher = null,
        Action<Player, PriorityAction.ActivateManaAbility>? manaAbilityDispatcher = null,
        Func<Player, IAutoPassPrefsView?>? autoPassPrefsProvider = null,
        Func<GameContext, bool>? isPassOnlyDeadWindow = null,
        IEventBus? eventBus = null,
        Func<DateTime>? clock = null)
    {
        _castDispatcher = castDispatcher;
        _activateDispatcher = activateDispatcher;
        _manaAbilityDispatcher = manaAbilityDispatcher;
        _players = players ?? throw new ArgumentNullException(nameof(players));
        _priority = priority ?? throw new ArgumentNullException(nameof(priority));
        _stack = stack ?? throw new ArgumentNullException(nameof(stack));
        _stackResolver = stackResolver ?? throw new ArgumentNullException(nameof(stackResolver));
        _zoneService = zoneService ?? throw new ArgumentNullException(nameof(zoneService));
        _agents = agents ?? throw new ArgumentNullException(nameof(agents));
        _turnNumberAccessor = turnNumberAccessor;
        _phaseAccessor = phaseAccessor;
        // CR 305.2 — the per-turn one-land cap is engine-level and unconditional.
        // PriorityLoop must always own a tracker so PlayLand consumption is gated
        // uniformly for every actor (bot or human). Callers that don't otherwise
        // need a tracker should instantiate a fresh one.
        _landDropTracker = landDropTracker ?? throw new ArgumentNullException(nameof(landDropTracker));
        _autoPassPrefsProvider = autoPassPrefsProvider;
        _isPassOnlyDeadWindow = isPassOnlyDeadWindow;
        _clock = clock ?? (() => DateTime.UtcNow);

        // Slice 5a — subscribe to stack-mutation events so the auto-pass
        // gate can suppress an immediate pass while the user is still
        // watching the stack settle. Subscriptions are best-effort: when
        // no bus is supplied (legacy ctor sites) the loop runs without a
        // stack-display window — the FullControl + PhaseStop guards still
        // protect the user, and auto-pass simply fires as fast as the
        // engine can compute the kinds.
        if (eventBus != null)
        {
            _eventBus = eventBus;
            _onStackAdded = _ => _lastStackMutatedAt = _clock();
            _onStackResolved = _ => _lastStackMutatedAt = _clock();
            eventBus.Subscribe(_onStackAdded);
            eventBus.Subscribe(_onStackResolved);
        }
    }

    /// <summary>
    /// Detach this loop's stack-mutation subscriptions from the engine
    /// bus. Safe to call multiple times. Callers that construct a fresh
    /// PriorityLoop per priority round (TurnDriver) MUST call this when
    /// the round ends so handlers don't pile up on the bus across the
    /// game's lifetime.
    /// </summary>
    public void DetachFromBus()
    {
        if (_eventBus == null) return;
        if (_onStackAdded != null) _eventBus.Unsubscribe(_onStackAdded);
        if (_onStackResolved != null) _eventBus.Unsubscribe(_onStackResolved);
    }

    /// <summary>
    /// Runs priority rounds until the stack is empty AND all players pass in
    /// succession on an empty stack (Rule 117.4 — phase can end).
    /// </summary>
    public async Task RunUntilRoundEndsAsync(Player activePlayer, CancellationToken ct = default)
    {
        _activePlayer = activePlayer;
        while (true)
        {
            _priority.InitializeForPhase(activePlayer);

            // Drive one full pass: each player either acts (resets pass count)
            // or passes. Round ends when all players have passed in succession.
            // Safety cap on non-pass actions per round to catch infinite
            // agent loops (e.g. bot keeps proposing a cast whose payment
            // silently fails). 500 is well above any realistic round.
            const int kActionLimit = 500;
            var actionCount = 0;
            while (!_priority.AllPlayersPassed && !ct.IsCancellationRequested)
            {
                var current = _priority.CurrentPlayer
                    ?? throw new InvalidOperationException("No current priority holder");

                var agent = _agents[current];
                var ctx = MakeContext(current, activePlayer);

                // Slice 5a — server-side auto-pass. Skip the agent prompt
                // entirely when the engine knows the window is dead AND
                // the viewer's prefs allow it. The agent's Prompt<T>
                // never fires → no SignalR prompt published → no client
                // round-trip. Bot agents are unaffected: the wiring at
                // GameFacade returns null for bot seats so this branch
                // never short-circuits a bot's decision path.
                if (TryAutoPass(current, ctx, out var autoAction))
                {
                    // Apply the synthesized pass directly. This mirrors
                    // the PassAction branch below (no priority shift
                    // beyond PassPriority) — no other side effects, no
                    // log entry, no event publish. The engine carries
                    // on exactly as if the agent had submitted Pass.
                    _priority.PassPriority();
                    _ = autoAction; // discard — value is always Pass
                    continue;
                }

                var action = await agent.ChoosePriorityActionAsync(ctx, ct).ConfigureAwait(false);

                if (action is PriorityAction.PassAction)
                {
                    _priority.PassPriority();
                    continue;
                }

                if (++actionCount > kActionLimit)
                {
                    System.Console.Error.WriteLine(
                        $"PRIORITY LOOP SAFETY: {kActionLimit} non-pass actions in one round, " +
                        $"actor={current.Name}, last action={action.GetType().Name}. Forcing round end.");
                    return;
                }

                var applied = await ApplyActionAsync(current, action, ctx, ct).ConfigureAwait(false);

                if (!applied)
                {
                    // The proposal was rejected / no-op'd (e.g. an illegal
                    // PlayLand over the CR 305.2 per-turn cap). The action
                    // never happened, so honoring its HoldPriority flag here
                    // would hand priority straight back to the same actor —
                    // and a misbehaving agent that re-proposes the same
                    // illegal action would spin the round to kActionLimit,
                    // flooding the log. Treat a rejected proposal as a pass so
                    // the loop always makes forward progress.
                    _priority.PassPriority();
                }
                else if (HoldsPriority(action))
                {
                    // CR 117.3c — actor keeps priority instead of passing.
                    // Reset the pass count but DO NOT shift current player.
                    _priority.HoldPriority();
                }
                else
                {
                    // Action taken — priority returns to active player.
                    _priority.HoldPriority();
                    _priority.InitializeForPhase(activePlayer);
                }
            }

            if (_stack.IsEmpty)
            {
                return;
            }

            // PLAN 01 — resolve the top object on the async path so its
            // effects can await player prompts. The resolution context's
            // agent is looked up per the resolving object's controller
            // (engine seat agents first, AgentRegistry fallback); the live
            // game context is built for the active player.
            await _stackResolver.ResolveTopAsync(
                _stack,
                ResolveAgentForController,
                MakeContext(activePlayer, activePlayer),
                ct).ConfigureAwait(false);
            // Loop back: start a fresh priority round with active player.
        }
    }

    /// <summary>
    /// Applies a non-pass priority action. Returns <c>true</c> when the action
    /// committed, <c>false</c> when it was rejected / no-op'd (e.g. an illegal
    /// land over the CR 305.2 cap) so the caller can force a pass instead of
    /// re-handing priority to the proposing actor.
    /// </summary>
    private async Task<bool> ApplyActionAsync(Player actor, PriorityAction action, GameContext ctx, CancellationToken ct)
    {
        switch (action)
        {
            case PriorityAction.PlayLand land:
                if (_activePlayer == null)
                    throw new InvalidOperationException(
                        "PriorityLoop received PlayLand before RunUntilRoundEndsAsync set an active player.");
                {
                    var phase = _phaseAccessor() ?? PhaseStateType.PreCombatMain;
                    if (!_landDropTracker.CanPlayLand(
                        actor, _activePlayer, phase, _stack.IsEmpty, out var reason))
                    {
                        // CR 305.2 — illegal land proposal. Mirror the
                        // cast/activate paths' swallow-and-log posture so
                        // a misbehaving agent (or an over-eager bot that
                        // doesn't pre-check the per-turn cap) can't crash
                        // the whole turn. The land stays in hand;
                        // HeuristicBotAgent's per-turn failed-proposal
                        // memo will skip it on the next priority opportunity.
                        System.Console.Error.WriteLine(
                            $"PriorityLoop: rejected PlayLand({land.Land.Name}) by {actor.Name}: {reason}");
                        return false;
                    }
                    // PLAN 08 — play the land on the async zone-move path so a
                    // prompting ETB replacement on the land (shock-land "pay 2
                    // life") awaits the actor's agent off the resolution context
                    // instead of bridging sync-over-async on the priority thread.
                    var landCtx = ResolutionContext.For(
                        actor, ResolveAgentForController(actor), ctx, chosenTargets: null, ct);
                    await _zoneService.MoveCardToAsync(
                        land.Land, ZoneType.Battlefield, landCtx, controller: actor)
                        .ConfigureAwait(false);
                    _landDropTracker.RecordLandPlayed(actor);
                }
                break;
            case PriorityAction.CastSpell cast:
                if (_castDispatcher == null)
                    throw new InvalidOperationException(
                        "PriorityLoop received CastSpell but no castDispatcher was supplied.");
                await _castDispatcher(actor, cast, ctx).ConfigureAwait(false);
                break;
            case PriorityAction.ActivateAbility activate:
                if (_activateDispatcher == null)
                    throw new InvalidOperationException(
                        "PriorityLoop received ActivateAbility but no activateDispatcher was supplied.");
                await _activateDispatcher(actor, activate, ctx).ConfigureAwait(false);
                break;
            case PriorityAction.ActivateManaAbility mana:
                if (_manaAbilityDispatcher == null)
                    throw new InvalidOperationException(
                        "PriorityLoop received ActivateManaAbility but no manaAbilityDispatcher was supplied.");
                // CR 605.3a — mana abilities don't use the stack and don't
                // pass priority. The activator handles tapping + adding to
                // pool synchronously; HoldsPriority below keeps the same
                // player on the prompt so they can chain into a cast.
                _manaAbilityDispatcher(actor, mana);
                break;
            case PriorityAction.PassAction:
                _priority.PassPriority();
                break;
            default:
                throw new InvalidOperationException($"Unknown action {action.GetType().Name}");
        }

        // Every non-rejection path above committed its action.
        return true;
    }

    /// <summary>
    /// Slice 5a — server-side auto-pass gate. Returns <c>true</c> (with
    /// <see cref="PriorityAction.Pass"/>) when ALL of the following hold:
    /// <list type="number">
    ///   <item>A priority-kinds builder is wired AND it returns exactly
    ///         <c>[PassPriorityCommand]</c> for this priority moment
    ///         (the engine has narrowed to a "dead" window — no lands,
    ///         no castable spells, no activatable abilities). Belt and
    ///         braces with the agent's own narrowing: the kinds builder
    ///         is the single source of truth so the engine and the
    ///         portal can't disagree about which windows are dead.</item>
    ///   <item>A prefs provider is wired AND it returns a non-null
    ///         <see cref="IAutoPassPrefsView"/> for the current priority
    ///         holder (bot seats return null → never auto-pass; bots
    ///         drive themselves through ChoosePriorityActionAsync).</item>
    ///   <item><see cref="IAutoPassPrefsView.FullControl"/> is false —
    ///         the human is NOT holding the Full Control modifier.</item>
    ///   <item>No phase stop is configured for the active side at the
    ///         current wire phase label. Semantics match the portal's
    ///         <c>shouldAutoPass</c>: <c>"mine"</c> means stop on the
    ///         viewer's own turn; <c>"theirs"</c> on the opponent's.</item>
    ///   <item>We're past the stack-display window
    ///         (<see cref="AutoPassConstants.StackMutationDisplayMs"/>)
    ///         after the most recent stack mutation. Gives the user a
    ///         beat to register a freshly-landed trigger or spell
    ///         before it resolves silently.</item>
    /// </list>
    /// All five must be true for the agent to be skipped. A miss on any
    /// falls through to the normal <see cref="IPlayerAgent.ChoosePriorityActionAsync"/>
    /// path.
    ///
    /// <para>Concurrency: the prefs read is a snapshot. A user toggling
    /// FullControl while a window is being evaluated may see at most one
    /// extra auto-pass before the next priority window picks up the new
    /// setting — documented as acceptable in the spec.</para>
    /// </summary>
    private bool TryAutoPass(Player current, GameContext ctx, out PriorityAction autoAction)
    {
        autoAction = PriorityAction.Pass;

        if (_isPassOnlyDeadWindow == null) return false;
        if (_autoPassPrefsProvider == null) return false;

        // Gate 1 — engine-narrowed dead window. The predicate is supplied
        // by Majik.Core.Api (PriorityKinds) so PriorityLoop stays unaware
        // of the wire command types.
        if (!_isPassOnlyDeadWindow(ctx)) return false;

        // Gate 2 — prefs available for this seat (bot seats return null).
        var prefs = _autoPassPrefsProvider(current);
        if (prefs == null) return false;

        // Gate 3 — Full Control suppresses every auto-pass.
        if (prefs.FullControl) return false;

        // Gate 4 — phase stop on the active side at the current phase.
        // PhaseStops keys are wire phase labels (PhaseStateType.ToString()
        // — "Untap", "Upkeep", "PreCombatMain", …); values are "mine" /
        // "theirs". When the active player IS the viewer (=current), the
        // active side is "mine"; otherwise "theirs".
        var phase = ctx.CurrentPhase;
        if (phase is { } p)
        {
            var phaseLabel = p.ToString();
            if (prefs.PhaseStops.TryGetValue(phaseLabel, out var stopSide))
            {
                var activeSide = ReferenceEquals(ctx.ActivePlayer, current) ? "mine" : "theirs";
                if (string.Equals(stopSide, activeSide, StringComparison.Ordinal))
                {
                    return false;
                }
            }
        }

        // Gate 5 — stack-display window. AutoPassConstants.StackMutationDisplayMs
        // after the last stack mutation, suppress to give the user a beat
        // to register the change before it resolves silently.
        var elapsed = _clock() - _lastStackMutatedAt;
        if (elapsed < TimeSpan.FromMilliseconds(AutoPassConstants.StackMutationDisplayMs))
        {
            return false;
        }

        return true;
    }

    private GameContext MakeContext(Player self, Player activePlayer) =>
        new(self, _players, activePlayer, _turnNumberAccessor(), _phaseAccessor(), _stack);

    // PLAN 01 — resolve the agent for a stack object's controller when
    // resolving its effects. Prefer the engine-supplied seat agents (this
    // loop already holds them); fall back to the ambient AgentRegistry for
    // controllers not in the local map (e.g. tokens / effects that resolve
    // for a player the loop wasn't constructed with).
    private IPlayerAgent? ResolveAgentForController(Player controller)
    {
        if (controller == null) return null;
        if (_agents.TryGetValue(controller, out var seatAgent)) return seatAgent;
        return AgentRegistry.Get(controller);
    }

    private static bool HoldsPriority(PriorityAction action) => action switch
    {
        PriorityAction.CastSpell cs => cs.HoldPriority,
        PriorityAction.ActivateAbility a => a.HoldPriority,
        PriorityAction.PlayLand pl => pl.HoldPriority,
        // CR 605.3a — activating a mana ability does not cause the player
        // to pass priority. Implicit hold so the same player gets the next
        // prompt and can spend the mana they just produced.
        PriorityAction.ActivateManaAbility => true,
        _ => false,
    };
}
