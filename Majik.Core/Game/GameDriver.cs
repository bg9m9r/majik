using Majik.Core.Abilities;
using Majik.Core.Combat;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Rules;
using Majik.Core.Services;
using Majik.Core.Zones;

namespace Majik.Core.Game;

/// <summary>
/// Top-level game loop. Alternates active player each turn, calls
/// <see cref="TurnDriver"/>, and stops as soon as a player has lost OR
/// the turn cap is reached (bot games can stalemate forever; cap is the
/// safety valve).
///
/// Mulligan is intentionally not wired here yet — caller seeds opening
/// hands. Future revision: invoke <see cref="MulliganController"/> for
/// each player before turn 1.
/// </summary>
public sealed class GameDriver
{
    private readonly IReadOnlyList<Player> _players;
    private readonly IReadOnlyDictionary<Player, IPlayerAgent> _agents;
    private readonly TurnDriver _turnDriver;
    private readonly StateBasedActions _sba;

    public sealed record GameResult(int TurnsPlayed, Player? Winner);

    public GameDriver(
        IReadOnlyList<Player> players,
        IReadOnlyDictionary<Player, IPlayerAgent> agents,
        Majik.Core.Stack.Stack stack,
        ZoneService zoneService,
        TriggerManager triggerManager,
        StackResolver stackResolver,
        StateBasedActions stateBasedActions,
        PriorityManager priorityManager,
        CombatFlow combatFlow)
    {
        _players = players ?? throw new ArgumentNullException(nameof(players));
        _agents = agents ?? throw new ArgumentNullException(nameof(agents));
        _sba = stateBasedActions ?? throw new ArgumentNullException(nameof(stateBasedActions));

        _turnDriver = new TurnDriver(
            players, agents, stack, zoneService, triggerManager,
            stackResolver, stateBasedActions, priorityManager, combatFlow);
    }

    public async Task<GameResult> RunGameAsync(int maxTurns = 30, CancellationToken ct = default)
    {
        var turnNumber = 0;
        var activeIndex = 0;
        while (turnNumber < maxTurns)
        {
            // Check for game end via SBA before starting next turn.
            _sba.CheckStateBasedActions(
                _players,
                _players.SelectMany(p => p.Zones.Battlefield.GetCards()).ToList());

            var alive = _players.Where(p => !p.HasLost).ToList();
            if (alive.Count == 1)
            {
                return new GameResult(turnNumber, alive[0]);
            }

            if (alive.Count == 0)
            {
                return new GameResult(turnNumber, null);
            }

            turnNumber++;
            var active = _players[activeIndex];
            await _turnDriver.RunTurnAsync(active, turnNumber, ct);

            activeIndex = (activeIndex + 1) % _players.Count;
        }

        // Reached cap without a winner.
        _sba.CheckStateBasedActions(
            _players,
            _players.SelectMany(p => p.Zones.Battlefield.GetCards()).ToList());
        var stillAlive = _players.Where(p => !p.HasLost).ToList();
        return new GameResult(turnNumber, stillAlive.Count == 1 ? stillAlive[0] : null);
    }
}
