using Majik.Core.Players;

namespace Majik.Core.Game;

/// <summary>
/// CR 500.7 / 715 — extra-turn ordering. Effects that grant a player an
/// extra turn (Time Walk, Beacon of Tomorrows, Temporal Manipulation,
/// etc.) push onto this queue. The next turn the engine asks for is
/// pulled from here first; only when empty does normal round-robin
/// passing proceed.
///
/// CR 500.7 — if multiple extra turns are queued by overlapping effects,
/// the last-added turn is taken next (LIFO). Modelled as a stack.
/// </summary>
public sealed class ExtraTurnQueue
{
    private readonly Stack<Player> _pending = new();

    public int Pending => _pending.Count;

    public void EnqueueExtraTurn(Player player)
    {
        if (player == null) throw new ArgumentNullException(nameof(player));
        _pending.Push(player);
    }

    public bool TryDequeueNext(out Player? next)
    {
        if (_pending.Count == 0) { next = null; return false; }
        next = _pending.Pop();
        return true;
    }
}
