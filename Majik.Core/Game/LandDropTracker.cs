using Majik.Core.Players;
using Majik.Core.StateMachine;

namespace Majik.Core.Game;

/// <summary>
/// CR 305.2 — a player may play one land per turn, only on their own turn,
/// during a main phase, when the stack is empty. Tracks per-player drops
/// used this turn; reset by <see cref="TurnDriver"/> on turn change.
///
/// Lands can normally be played up to once per turn; cards that allow
/// extra land drops (e.g. Azusa, Lost but Seeking) bump
/// <see cref="MaxLandDropsThisTurn"/>.
/// </summary>
public sealed class LandDropTracker
{
    private readonly Dictionary<Player, int> _used = new();
    private readonly Dictionary<Player, int> _maxPerTurn = new();

    public int MaxLandDropsThisTurn(Player player) =>
        _maxPerTurn.TryGetValue(player, out var n) ? n : 1;

    public void SetMaxLandDropsThisTurn(Player player, int max)
    {
        if (max < 0) throw new ArgumentOutOfRangeException(nameof(max));
        _maxPerTurn[player] = max;
    }

    public int DropsUsedThisTurn(Player player) =>
        _used.TryGetValue(player, out var n) ? n : 0;

    public bool CanPlayLand(
        Player player,
        Player activePlayer,
        PhaseStateType phase,
        bool stackEmpty,
        out string reason)
    {
        if (!ReferenceEquals(player, activePlayer))
        {
            reason = "lands can only be played on your turn";
            return false;
        }
        if (!phase.IsMain())
        {
            reason = "lands can only be played during a main phase";
            return false;
        }
        if (!stackEmpty)
        {
            reason = "lands can only be played when the stack is empty";
            return false;
        }
        if (DropsUsedThisTurn(player) >= MaxLandDropsThisTurn(player))
        {
            reason = $"already played {DropsUsedThisTurn(player)} land(s) this turn";
            return false;
        }
        reason = string.Empty;
        return true;
    }

    public void RecordLandPlayed(Player player)
    {
        _used[player] = DropsUsedThisTurn(player) + 1;
    }

    /// <summary>Reset on turn change. Also resets any bumped per-turn max
    /// (extra-land effects re-evaluate each turn).</summary>
    public void ResetTurn()
    {
        _used.Clear();
        _maxPerTurn.Clear();
    }
}
