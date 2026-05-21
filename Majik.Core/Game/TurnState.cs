using Majik.Core.Players;

namespace Majik.Core.Game;

/// <summary>
/// Per-turn event tally. Reset at the start of each turn (Rule 800 series).
/// Cards and abilities consult this to enable conditional triggers and costs
/// (revolt — Rule 702.104, connive X, opponent-draw watchers, etc.).
///
/// Owned by <see cref="TurnDriver"/>; counters are incremented by subscribing
/// to <see cref="Majik.Core.Events.CardMovedEvent"/> and
/// <see cref="Majik.Core.Events.CardDrawnEvent"/> on the game's event bus.
/// </summary>
public sealed class TurnState
{
    /// <summary>Total number of creatures that died this turn (Rule 702.104b).</summary>
    public int CreaturesDiedThisTurn { get; private set; }

    /// <summary>Total number of permanents that left the battlefield this turn.</summary>
    public int PermanentsLeftBattlefieldThisTurn { get; private set; }

    private readonly Dictionary<Guid, int> _creaturesDiedByController = new();
    private readonly Dictionary<Guid, int> _permanentsLeftByController = new();
    private readonly Dictionary<Guid, int> _cardsDrawnByPlayer = new();

    /// <summary>
    /// How many creatures controlled by <paramref name="player"/> died this turn.
    /// </summary>
    public int CreaturesDiedByController(Player player) =>
        _creaturesDiedByController.TryGetValue(player.Id, out var v) ? v : 0;

    /// <summary>
    /// How many permanents controlled by <paramref name="player"/> left the
    /// battlefield this turn (all permanent types, not just creatures).
    /// </summary>
    public int PermanentsLeftByController(Player player) =>
        _permanentsLeftByController.TryGetValue(player.Id, out var v) ? v : 0;

    /// <summary>
    /// How many cards <paramref name="player"/> has drawn this turn.
    /// </summary>
    public int CardsDrawnByPlayer(Player player) =>
        _cardsDrawnByPlayer.TryGetValue(player.Id, out var v) ? v : 0;

    /// <summary>
    /// Whether revolt is active for <paramref name="player"/> — i.e. at least one
    /// permanent they controlled left the battlefield this turn (Rule 702.104a).
    /// </summary>
    public bool RevoltActive(Player player) => PermanentsLeftByController(player) > 0;

    /// <summary>
    /// Called when a creature dies (moves to any zone from the battlefield
    /// while it has the Creature card type). Increments both the global
    /// counter and the per-controller bucket.
    /// </summary>
    public void RecordCreatureDied(Player? formerController)
    {
        CreaturesDiedThisTurn++;
        if (formerController != null)
        {
            _creaturesDiedByController[formerController.Id] =
                _creaturesDiedByController.GetValueOrDefault(formerController.Id) + 1;
        }
    }

    /// <summary>
    /// Called when any permanent leaves the battlefield (to any zone).
    /// Increments both the global counter and the per-controller bucket.
    /// </summary>
    public void RecordPermanentLeftBattlefield(Player? formerController)
    {
        PermanentsLeftBattlefieldThisTurn++;
        if (formerController != null)
        {
            _permanentsLeftByController[formerController.Id] =
                _permanentsLeftByController.GetValueOrDefault(formerController.Id) + 1;
        }
    }

    /// <summary>
    /// Called when a player draws a card.
    /// </summary>
    public void RecordCardDrawn(Player player)
    {
        _cardsDrawnByPlayer[player.Id] =
            _cardsDrawnByPlayer.GetValueOrDefault(player.Id) + 1;
    }

    /// <summary>
    /// Reset all counters at the start of each turn (called by
    /// <see cref="TurnDriver"/> before the untap step).
    /// </summary>
    public void Reset()
    {
        CreaturesDiedThisTurn = 0;
        PermanentsLeftBattlefieldThisTurn = 0;
        _creaturesDiedByController.Clear();
        _permanentsLeftByController.Clear();
        _cardsDrawnByPlayer.Clear();
    }
}
