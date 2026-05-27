using Majik.Core.Cards;
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
        Action<Player, PriorityAction.ActivateManaAbility>? manaAbilityDispatcher = null)
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
                var action = await agent.ChoosePriorityActionAsync(ctx, ct);

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

                await ApplyActionAsync(current, action, ctx, ct);

                if (HoldsPriority(action))
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

            _stackResolver.ResolveTop(_stack);
            // Loop back: start a fresh priority round with active player.
        }
    }

    private async Task ApplyActionAsync(Player actor, PriorityAction action, GameContext ctx, CancellationToken ct)
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
                        break;
                    }
                    _zoneService.MoveCardTo(land.Land, ZoneType.Battlefield, controller: actor);
                    _landDropTracker.RecordLandPlayed(actor);
                }
                break;
            case PriorityAction.CastSpell cast:
                if (_castDispatcher == null)
                    throw new InvalidOperationException(
                        "PriorityLoop received CastSpell but no castDispatcher was supplied.");
                await _castDispatcher(actor, cast, ctx);
                break;
            case PriorityAction.ActivateAbility activate:
                if (_activateDispatcher == null)
                    throw new InvalidOperationException(
                        "PriorityLoop received ActivateAbility but no activateDispatcher was supplied.");
                await _activateDispatcher(actor, activate, ctx);
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
    }

    private GameContext MakeContext(Player self, Player activePlayer) =>
        new(self, _players, activePlayer, _turnNumberAccessor(), _phaseAccessor(), _stack);

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
