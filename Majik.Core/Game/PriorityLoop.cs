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

    public PriorityLoop(
        IReadOnlyList<Player> players,
        PriorityManager priority,
        Majik.Core.Stack.Stack stack,
        StackResolver stackResolver,
        ZoneService zoneService,
        IReadOnlyDictionary<Player, IPlayerAgent> agents,
        Func<int> turnNumberAccessor,
        Func<PhaseStateType?> phaseAccessor)
    {
        _players = players ?? throw new ArgumentNullException(nameof(players));
        _priority = priority ?? throw new ArgumentNullException(nameof(priority));
        _stack = stack ?? throw new ArgumentNullException(nameof(stack));
        _stackResolver = stackResolver ?? throw new ArgumentNullException(nameof(stackResolver));
        _zoneService = zoneService ?? throw new ArgumentNullException(nameof(zoneService));
        _agents = agents ?? throw new ArgumentNullException(nameof(agents));
        _turnNumberAccessor = turnNumberAccessor;
        _phaseAccessor = phaseAccessor;
    }

    /// <summary>
    /// Runs priority rounds until the stack is empty AND all players pass in
    /// succession on an empty stack (Rule 117.4 — phase can end).
    /// </summary>
    public async Task RunUntilRoundEndsAsync(Player activePlayer, CancellationToken ct = default)
    {
        while (true)
        {
            _priority.InitializeForPhase(activePlayer);

            // Drive one full pass: each player either acts (resets pass count)
            // or passes. Round ends when all players have passed in succession.
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

                ApplyAction(current, action);
                // Action taken — priority resets to active player (Rule 117.3c).
                _priority.HoldPriority();
                _priority.InitializeForPhase(activePlayer);
            }

            if (_stack.IsEmpty)
            {
                return;
            }

            _stackResolver.ResolveTop(_stack);
            // Loop back: start a fresh priority round with active player.
        }
    }

    private void ApplyAction(Player actor, PriorityAction action)
    {
        switch (action)
        {
            case PriorityAction.PlayLand land:
                _zoneService.MoveCardTo(land.Land, ZoneType.Battlefield, controller: actor);
                break;
            case PriorityAction.CastSpell:
            case PriorityAction.ActivateAbility:
                // Routed through SpellCastFlow / ability activation in later phases.
                throw new NotImplementedException(
                    "CastSpell / ActivateAbility require SpellCastFlow (phase 8.6).");
            case PriorityAction.PassAction:
                _priority.PassPriority();
                break;
            default:
                throw new InvalidOperationException($"Unknown action {action.GetType().Name}");
        }
    }

    private GameContext MakeContext(Player self, Player activePlayer) =>
        new(self, _players, activePlayer, _turnNumberAccessor(), _phaseAccessor(), _stack);
}
