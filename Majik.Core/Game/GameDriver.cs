using Majik.Core.Abilities;
using Majik.Core.Combat;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Rules;
using Majik.Core.Services;
using Majik.Core.StateMachine;
using Majik.Core.Zones;

namespace Majik.Core.Game;

/// <summary>
/// Top-level game loop. Alternates active player each turn, calls
/// <see cref="TurnDriver"/>, and stops as soon as a player has lost OR
/// the turn cap is reached.
///
/// Phase 25 wiring:
///   1. Shuffle each player's library via <see cref="Majik.Core.Random.GameRandom"/>
///   2. Choose starting player (coin flip)
///   3. Each player runs the <see cref="MulliganController"/> (London)
///   4. Normal turn loop, first player skips draw step (already handled by TurnDriver)
/// </summary>
public sealed class GameDriver
{
    private readonly IReadOnlyList<Player> _players;
    private readonly IReadOnlyDictionary<Player, IPlayerAgent> _agents;
    private readonly TurnDriver _turnDriver;
    private readonly StateBasedActions _sba;
    private readonly Majik.Core.Random.GameRandom _rng;

    public sealed record GameResult(int TurnsPlayed, Player? Winner, Player? StartingPlayer);

    public GameDriver(
        IReadOnlyList<Player> players,
        IReadOnlyDictionary<Player, IPlayerAgent> agents,
        Majik.Core.Stack.Stack stack,
        ZoneService zoneService,
        TriggerManager triggerManager,
        StackResolver stackResolver,
        StateBasedActions stateBasedActions,
        PriorityManager priorityManager,
        CombatFlow combatFlow,
        Majik.Core.Random.GameRandom? rng = null)
    {
        _players = players ?? throw new ArgumentNullException(nameof(players));
        _agents = agents ?? throw new ArgumentNullException(nameof(agents));
        _sba = stateBasedActions ?? throw new ArgumentNullException(nameof(stateBasedActions));
        _rng = rng ?? new Majik.Core.Random.GameRandom();

        _turnDriver = new TurnDriver(
            players, agents, stack, zoneService, triggerManager,
            stackResolver, stateBasedActions, priorityManager, combatFlow);
    }

    public async Task<GameResult> RunGameAsync(int maxTurns = 30, CancellationToken ct = default)
    {
        // CR 103.1 — shuffle libraries.
        foreach (var p in _players)
        {
            ShuffleLibrary(p);
        }

        // CR 103.2 — determine starting player. Coin flip works for 2,
        // generalised to N-player by random pick.
        var startingIndex = _rng.Next(_players.Count);
        var startingPlayer = _players[startingIndex];

        // CR 103.4 — mulligan loop per player.
        var mulligan = new MulliganController();
        foreach (var p in _players)
        {
            var ctx = new GameContext(
                p, _players, startingPlayer, 0, PhaseStateType.Untap,
                new Majik.Core.Stack.Stack());
            await mulligan.RunAsync(p, _agents[p], ctx, ct: ct);
        }

        var turnNumber = 0;
        var activeIndex = startingIndex;
        while (turnNumber < maxTurns)
        {
            _sba.CheckStateBasedActions(
                _players,
                _players.SelectMany(p => p.Zones.Battlefield.GetCards()).ToList());

            var alive = _players.Where(p => !p.HasLost).ToList();
            if (alive.Count == 1) return new GameResult(turnNumber, alive[0], startingPlayer);
            if (alive.Count == 0) return new GameResult(turnNumber, null, startingPlayer);

            turnNumber++;
            var active = _players[activeIndex];
            await _turnDriver.RunTurnAsync(active, turnNumber, ct);

            activeIndex = (activeIndex + 1) % _players.Count;
        }

        _sba.CheckStateBasedActions(
            _players,
            _players.SelectMany(p => p.Zones.Battlefield.GetCards()).ToList());
        var stillAlive = _players.Where(p => !p.HasLost).ToList();
        return new GameResult(
            turnNumber, stillAlive.Count == 1 ? stillAlive[0] : null, startingPlayer);
    }

    private void ShuffleLibrary(Player p)
    {
        var lib = p.Zones.Library.GetCards().ToList();
        foreach (var c in lib) p.Zones.Library.RemoveCard(c);
        _rng.Shuffle(lib);
        foreach (var c in lib) p.Zones.Library.AddCard(c);
    }
}
