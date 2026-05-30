namespace Majik.Core.Players;

/// <summary>
/// CR 720 ("Controlling Another Player") — per-game registry of active
/// player-control grants. An effect such as Mindslaver or Emrakul, the
/// Promised End's cast trigger calls <see cref="GrantControl"/> to mark
/// "<paramref name="controller"/> controls <paramref name="controlled"/>
/// during that player's next turn". When the controlled player's next turn
/// begins, the turn loop routes every decision the controlled player would
/// make to the controller's agent (CR 720.1 — the controller "makes all
/// decisions and choices the controlled player would normally be allowed to
/// make").
///
/// <para>CR 720.2 / CR 720.3 — control is over <em>decisions only</em>. The
/// controlled player's life total, cards, hand, library, and ownership of
/// permanents remain entirely theirs; the controller merely chooses which
/// plays they make. This registry holds nothing but the decision-routing
/// map; the controlled player's game objects are never reassigned.</para>
///
/// <para>Lifetime: a grant is consumed when the controlled player's next
/// turn starts (the turn loop calls <see cref="ConsumeControlFor"/> at
/// turn-start to take the snapshot for the duration of that one turn, then
/// <see cref="ClearActiveControl"/> when the turn ends). A grant that is
/// queued but whose controlled player never takes another turn (e.g. the
/// game ends first) simply expires unused.</para>
///
/// <para>Deferred sub-caveats (CR 720.5 / CR 720.6), documented but NOT
/// modelled here because the engine has no surface for them yet:
/// the controller still <b>can't</b> make the controlled player concede
/// (CR 720.6 — conceding is always the conceding player's own choice) and
/// "discard at random" / other random/hidden choices that the rules assign
/// to the controlled player are unaffected by who is making strategic
/// decisions. These caveats do not regress any existing behaviour — the
/// engine currently exposes no concede action through the agent surface and
/// random discard is resolved by the engine, not the agent — so nothing
/// here re-routes them.</para>
/// </summary>
public sealed class ControlPlayerRegistry
{
    // Pending grants keyed by the controlled player's Id. A later grant for
    // the same controlled player overwrites an earlier one (CR 720.4 — the
    // most recent control-changing effect's controller wins for that turn).
    private readonly Dictionary<Guid, Player> _pending = new();

    // The control in force for the turn currently being driven. Populated by
    // ConsumeControlFor at turn-start; cleared by ClearActiveControl at
    // turn-end. Null when the active turn's player is making their own
    // decisions.
    private (Player Controlled, Player Controller)? _active;

    /// <summary>True when a control grant is in force for the turn currently
    /// being driven.</summary>
    public bool HasActiveControl => _active is not null;

    /// <summary>The player whose decisions are currently being made by
    /// someone else, or <see langword="null"/> when no control is active.</summary>
    public Player? ActivelyControlled => _active?.Controlled;

    /// <summary>The player currently making the controlled player's
    /// decisions, or <see langword="null"/> when no control is active.</summary>
    public Player? ActiveController => _active?.Controller;

    /// <summary>
    /// CR 720.1 — record that <paramref name="controller"/> will control
    /// <paramref name="controlled"/> during that player's next turn. A
    /// player can't take control of themselves (no-op — CR 720.1 only
    /// applies to gaining control of a different player). A second grant for
    /// the same controlled player before that player's next turn overwrites
    /// the first (CR 720.4).
    /// </summary>
    public void GrantControl(Player controller, Player controlled)
    {
        ArgumentNullException.ThrowIfNull(controller);
        ArgumentNullException.ThrowIfNull(controlled);
        if (ReferenceEquals(controller, controlled)) return;
        _pending[controlled.Id] = controller;
    }

    /// <summary>True when a control grant is pending for
    /// <paramref name="player"/>'s next turn.</summary>
    public bool HasPendingControl(Player player)
    {
        ArgumentNullException.ThrowIfNull(player);
        return _pending.ContainsKey(player.Id);
    }

    /// <summary>
    /// Turn-start hook (CR 720.1): if a control grant is pending for
    /// <paramref name="turnPlayer"/>, promote it to the active control for
    /// the turn being driven and remove it from the pending set (a grant is
    /// for exactly one turn). Returns <see langword="true"/> and sets
    /// <paramref name="controller"/> when control is now active; otherwise
    /// returns <see langword="false"/> and leaves no active control.
    /// </summary>
    public bool ConsumeControlFor(Player turnPlayer, out Player? controller)
    {
        ArgumentNullException.ThrowIfNull(turnPlayer);
        if (_pending.TryGetValue(turnPlayer.Id, out var c))
        {
            _pending.Remove(turnPlayer.Id);
            _active = (turnPlayer, c);
            controller = c;
            return true;
        }
        controller = null;
        return false;
    }

    /// <summary>
    /// Turn-end hook: drop the active control so the next turn's player
    /// makes their own decisions again (CR 720.1 — control lasts for "that
    /// player's next turn", a single turn). Safe to call when no control is
    /// active.
    /// </summary>
    public void ClearActiveControl() => _active = null;

    /// <summary>
    /// CR 720.1 — resolve the player who currently makes
    /// <paramref name="player"/>'s decisions. Returns the active controller
    /// when <paramref name="player"/> is the actively-controlled player;
    /// otherwise returns <paramref name="player"/> unchanged (they make
    /// their own decisions).
    /// </summary>
    public Player EffectiveDecisionMaker(Player player)
    {
        ArgumentNullException.ThrowIfNull(player);
        if (_active is { } a && ReferenceEquals(a.Controlled, player))
        {
            return a.Controller;
        }
        return player;
    }
}
