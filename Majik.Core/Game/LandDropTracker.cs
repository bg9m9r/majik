using Majik.Core.Cards;
using Majik.Core.Players;
using Majik.Core.StateMachine;
using Majik.Core.Zones;

namespace Majik.Core.Game;

/// <summary>
/// CR 305.2 — a player may play one land per turn, only on their own turn,
/// during a main phase, when the stack is empty. Tracks per-player drops
/// used this turn; reset by <see cref="TurnDriver"/> on turn change.
///
/// Lands can normally be played up to once per turn (CR 505.5b — the
/// "play a land" turn-based permission resets each turn). Two independent
/// kinds of effect raise that cap:
///
/// <list type="bullet">
///   <item><b>One-shot, this-turn-only bumps</b> (Explore, Growth Spiral's
///   sibling, Sword of the Forge and Frontier) call
///   <see cref="SetMaxLandDropsThisTurn"/> on resolution. These live in
///   <see cref="_maxPerTurn"/> and are cleared by <see cref="ResetTurn"/>.</item>
///   <item><b>Persistent battlefield statics</b> (Azusa, Lost but Seeking +2;
///   Dryad of the Ilysian Grove / Exploration +1) — "you may play N
///   additional lands on each of your turns" (CR 720). These are NOT stored
///   here: they are summed live from
///   <see cref="Majik.Core.Cards.Permanent.AdditionalLandPlaysGranted"/> over
///   the battlefield permanents the player controls, so the bonus appears
///   when the source enters, vanishes when it leaves, stacks additively
///   across multiple sources, and is correct every turn with no
///   re-application (CR 603.6e — a static functions only while its source is
///   on the battlefield).</item>
/// </list>
///
/// The effective per-turn cap consulted by <see cref="CanPlayLand"/> is
/// <see cref="EffectiveMaxLandDropsThisTurn"/> = one-shot cap +
/// battlefield-static grant.
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

    /// <summary>
    /// CR 720 — the number of additional land plays granted to
    /// <paramref name="player"/> by battlefield permanents they control
    /// ("you may play N additional lands on each of your turns"). Summed
    /// live so the grant tracks the battlefield (enters/leaves) and stacks
    /// additively across multiple sources (two Azusas = +4). Returns 0 when
    /// the player controls no such permanent.
    /// </summary>
    public static int AdditionalLandPlaysFromBattlefield(Player player)
    {
        if (player == null) return 0;
        var total = 0;
        foreach (var card in player.Zones.Battlefield.GetCards())
        {
            if (card is Permanent p
                && p.Zone == ZoneType.Battlefield
                && ReferenceEquals(p.Controller, player))
            {
                total += p.AdditionalLandPlaysGranted;
            }
        }
        return total;
    }

    /// <summary>
    /// The effective per-turn land-play cap for <paramref name="player"/> —
    /// the one-shot cap (<see cref="MaxLandDropsThisTurn"/>, default 1) plus
    /// the live battlefield-static grant
    /// (<see cref="AdditionalLandPlaysFromBattlefield"/>). This is the value
    /// <see cref="CanPlayLand"/> enforces against
    /// <see cref="DropsUsedThisTurn"/>.
    /// </summary>
    public int EffectiveMaxLandDropsThisTurn(Player player) =>
        MaxLandDropsThisTurn(player) + AdditionalLandPlaysFromBattlefield(player);

    public int DropsUsedThisTurn(Player player) =>
        _used.TryGetValue(player, out var n) ? n : 0;

    public bool CanPlayLand(
        Player player,
        Player activePlayer,
        StepStateType phase,
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
        if (DropsUsedThisTurn(player) >= EffectiveMaxLandDropsThisTurn(player))
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

    /// <summary>
    /// Sim-resume seam (tree-state reuse): seed this tracker with the number of
    /// land drops <paramref name="player"/> has already used in the CURRENT
    /// (resumed) turn. A mid-turn snapshot restored into a fresh sandbox would
    /// otherwise start with a fresh tally and re-offer a land drop the
    /// snapshot's turn already consumed (CR 305.2). The seed is naturally
    /// cleared by <see cref="ResetTurn"/> when the next turn starts — exactly
    /// the live semantics.
    /// </summary>
    public void SeedDropsUsed(Player player, int dropsUsed)
    {
        ArgumentNullException.ThrowIfNull(player);
        if (dropsUsed < 0) throw new ArgumentOutOfRangeException(nameof(dropsUsed));
        _used[player] = dropsUsed;
    }

    /// <summary>Reset on turn change. Also resets any bumped per-turn max
    /// (extra-land effects re-evaluate each turn).</summary>
    public void ResetTurn()
    {
        _used.Clear();
        _maxPerTurn.Clear();
    }
}
